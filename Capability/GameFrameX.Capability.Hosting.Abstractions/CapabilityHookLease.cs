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

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 能力钩子挂载租约（Hotfix 侧持有；Dispose 摘下当前挂载、回能力声明的默认策略）。
/// <para>
/// 维护约束：新版本 <see cref="CapabilityHook{TContract}.Attach"/> 会使旧租约失效——
/// 失效租约的 Dispose 为空操作（不得摘下新挂载）；挂载编排接入热更装载 / 退役流程，
/// 旧版本退役时其租约已失效。
/// </para>
/// </summary>
public sealed class CapabilityHookLease<TContract> : IDisposable where TContract : class
{

    /// <summary>
    /// 摘下回调（指向所属槽位的版本化摘下）。
    /// </summary>
    private readonly Action _detach;

    /// <summary>
    /// 初始化 <see cref="CapabilityHookLease{TContract}"/>（仅机制构造）。
    /// </summary>
    /// <param name="detach">摘下回调。</param>
    internal CapabilityHookLease(Action detach)
    {
        _detach = detach ?? throw new ArgumentNullException(nameof(detach));
    }

    /// <summary>
    /// 摘下当前挂载（已被替换的旧租约为空操作；摘下后能力读到默认策略）。
    /// </summary>
    public void Dispose()
    {
        _detach();
    }
}
