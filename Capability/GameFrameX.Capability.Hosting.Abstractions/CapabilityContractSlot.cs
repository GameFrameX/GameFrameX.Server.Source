// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 契约槽位（<see cref="CapabilityBridge{TContract}"/> 与 <see cref="CapabilityHook{TContract}"/> 共用的
/// 每契约静态槽实现，方向相反：桥是能力发布、消费方取租约；钩子是外部挂载、能力读当前实现）。
/// <para>
/// 维护约束：逐调用复查活跃位（版本号变化即失效）；撤销 / 替换后旧租约显式失败、不回旧数据、不降级；
/// 线程安全（独立锁；槽内不做任何日志或回调，避免锁内重入）。
/// </para>
/// </summary>
/// <typeparam name="TContract">契约类型。</typeparam>
internal sealed class CapabilityContractSlot<TContract> where TContract : class
{
    /// <summary>
    /// 槽位锁（发布 / 撤销 / 挂载 / 摘下 / 读取共用）。
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    /// 当前发布 / 挂载的实现实例（null = 空槽，钩子形态回退默认实现）。
    /// </summary>
    private TContract _published;

    /// <summary>
    /// 钩子默认实现（能力声明；桥形态恒为 null）。
    /// </summary>
    private TContract _defaultImplementation;

    /// <summary>
    /// 当前发布者 / 声明者能力 Id（重复发布 / 重复声明诊断用）。
    /// </summary>
    private string _ownerId;

    /// <summary>
    /// 版本号（每次发布 / 撤销 / 挂载 / 摘下递增；租约凭版本失效）。
    /// </summary>
    private int _version;

    /// <summary>
    /// 是否存在活跃发布 / 挂载。
    /// </summary>
    private bool _hasPublished;

    /// <summary>
    /// 是否已声明默认实现（钩子形态审计用）。
    /// </summary>
    private bool _hasDefault;

    /// <summary>
    /// 默认声明版本号（每次声明 / 释放递增；释放凭据凭版本生效，已被新声明取代的旧凭据为空操作）。
    /// </summary>
    private int _declarationVersion;

    /// <summary>
    /// 发布一个契约实例（桥形态；同一契约重复发布 = 装配期 fail-fast）。
    /// </summary>
    /// <param name="contract">契约实现（不可为 null）。</param>
    /// <param name="publisherId">发布者能力 Id（诊断用）。</param>
    /// <returns>撤销凭据（宿主停止期统一撤销）。</returns>
    internal IDisposable Publish(TContract contract, string publisherId)
    {
        if (contract == null)
        {
            throw new ArgumentNullException(nameof(contract));
        }

        var capturedVersion = 0;
        lock (_sync)
        {
            if (_hasPublished)
            {
                // Localization: Capability.Hosting.BridgeDuplicatePublish - 能力桥契约重复发布。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.BridgeDuplicatePublish), typeof(TContract).FullName, _ownerId, publisherId));
            }

            _published = contract;
            _ownerId = publisherId;
            _hasPublished = true;
            _version++;
            capturedVersion = _version;
        }

        var capturedSlot = this;
        return new CapabilityBridgePublication(() => capturedSlot.Retract(capturedVersion));
    }

    /// <summary>
    /// 撤销发布（仅当凭据版本仍是当前活跃版本时生效；重复撤销为空操作）。
    /// </summary>
    /// <param name="capturedVersion">撤销凭据捕获的版本号。</param>
    private void Retract(int capturedVersion)
    {
        lock (_sync)
        {
            if (_hasPublished && _version == capturedVersion)
            {
                _published = null;
                _ownerId = null;
                _hasPublished = false;
                _version++;
            }
        }
    }

    /// <summary>
    /// 尝试获取契约租约（发布者缺席返回 false）。
    /// </summary>
    /// <param name="lease">输出：契约租约。</param>
    /// <returns>发布者在场返回 true。</returns>
    internal bool TryAcquire(out CapabilityBridgeLease<TContract> lease)
    {
        lock (_sync)
        {
            if (!_hasPublished)
            {
                lease = null;
                return false;
            }

            lease = new CapabilityBridgeLease<TContract>(this, _version);
            return true;
        }
    }

    /// <summary>
    /// 按租约版本读取契约实现（版本不符 = 已撤销 / 已替换，显式失败）。
    /// </summary>
    /// <param name="capturedVersion">租约捕获的版本号。</param>
    /// <returns>契约实现。</returns>
    internal TContract GetPublished(int capturedVersion)
    {
        lock (_sync)
        {
            if (!_hasPublished || _version != capturedVersion)
            {
                // Localization: Capability.Hosting.BridgeUnavailable - 能力桥契约已撤销或被替换。
                throw new CapabilityBridgeUnavailableException(typeof(TContract).FullName);
            }

            return _published;
        }
    }

    /// <summary>
    /// 声明钩子默认实现（能力 Register 期；同一契约在活跃声明期内重复声明 = 装配期 fail-fast）。
    /// <para>
    /// 维护约束：槽位是进程级静态的，声明必须随声明方宿主的生命周期释放（宿主停止 / 激活失败 / 装配失败），
    /// 否则同进程第二次组合（重组、测试进程内多次装配）会被残留声明误判为重复声明。
    /// </para>
    /// </summary>
    /// <param name="defaultImplementation">默认实现（不可为 null）。</param>
    /// <param name="declarerId">声明者能力 Id（诊断用）。</param>
    /// <returns>释放凭据（宿主统一释放；仅当凭据版本仍是当前声明版本时生效，重复释放为空操作）。</returns>
    internal IDisposable DeclareDefault(TContract defaultImplementation, string declarerId)
    {
        if (defaultImplementation == null)
        {
            throw new ArgumentNullException(nameof(defaultImplementation));
        }

        var capturedVersion = 0;
        lock (_sync)
        {
            if (_hasDefault)
            {
                // Localization: Capability.Hosting.HookDuplicateDeclaration - 能力钩子重复声明。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.HookDuplicateDeclaration), typeof(TContract).FullName, _ownerId, declarerId));
            }

            _defaultImplementation = defaultImplementation;
            _ownerId = declarerId;
            _hasDefault = true;
            _declarationVersion++;
            capturedVersion = _declarationVersion;
        }

        var capturedSlot = this;
        return new CapabilityBridgePublication(() => capturedSlot.ReleaseDefault(capturedVersion));
    }

    /// <summary>
    /// 释放默认声明（仅当凭据版本仍是当前声明版本时生效；挂载实现不受影响，由挂载方租约自行摘下）。
    /// </summary>
    /// <param name="capturedVersion">释放凭据捕获的声明版本号。</param>
    private void ReleaseDefault(int capturedVersion)
    {
        lock (_sync)
        {
            if (_hasDefault && _declarationVersion == capturedVersion)
            {
                _defaultImplementation = null;
                _ownerId = null;
                _hasDefault = false;
                _declarationVersion++;
            }
        }
    }

    /// <summary>
    /// 挂载一个钩子实现（原子替换旧挂载并使旧租约失效；返回可摘下的挂载回调）。
    /// </summary>
    /// <param name="implementation">挂载实现（不可为 null）。</param>
    /// <returns>摘下回调（仅当挂载仍是当前版本时生效；已被替换的旧回调为空操作）。</returns>
    internal Action Attach(TContract implementation)
    {
        if (implementation == null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }

        var capturedVersion = 0;
        lock (_sync)
        {
            _published = implementation;
            _hasPublished = true;
            _version++;
            capturedVersion = _version;
        }

        var capturedSlot = this;
        return () => capturedSlot.Detach(capturedVersion);
    }

    /// <summary>
    /// 摘下挂载（仅当租约版本仍是当前挂载版本时生效；回默认实现）。
    /// </summary>
    /// <param name="capturedVersion">挂载租约捕获的版本号。</param>
    private void Detach(int capturedVersion)
    {
        lock (_sync)
        {
            if (_hasPublished && _version == capturedVersion)
            {
                _published = null;
                _hasPublished = false;
                _version++;
            }
        }
    }

    /// <summary>
    /// 获取钩子当前生效实现（挂载在场用挂载实现，否则用声明的默认实现）。
    /// </summary>
    internal TContract GetCurrent()
    {
        lock (_sync)
        {
            if (_hasPublished)
            {
                return _published;
            }

            return _defaultImplementation;
        }
    }

    /// <summary>
    /// 获取是否存在活跃挂载（装配期「已挂载但无能力声明」审计用）。
    /// </summary>
    internal bool IsAttached
    {
        get
        {
            lock (_sync)
            {
                return _hasPublished;
            }
        }
    }

    /// <summary>
    /// 获取是否已声明默认实现（装配期「已挂载但无能力声明」审计用）。
    /// </summary>
    internal bool HasDefault
    {
        get
        {
            lock (_sync)
            {
                return _hasDefault;
            }
        }
    }
}
