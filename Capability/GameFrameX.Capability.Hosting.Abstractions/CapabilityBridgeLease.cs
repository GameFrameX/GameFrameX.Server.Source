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
/// 能力桥租约（消费侧持有；每次访问 <see cref="Contract"/> 复查活跃位）。
/// <para>
/// 维护约束：已撤销 / 已替换的租约在访问时抛 <see cref="CapabilityBridgeUnavailableException"/>
/// （族层映射为 8002 显式失败）；租约不可续期、不可重新挂载——失效后消费方重新
/// <see cref="CapabilityBridge{TContract}.TryAcquire"/> 取新租约。
/// </para>
/// </summary>
/// <typeparam name="TContract">契约类型。</typeparam>
public sealed class CapabilityBridgeLease<TContract> where TContract : class
{
    /// <summary>
    /// 所属契约槽位。
    /// </summary>
    private readonly CapabilityContractSlot<TContract> _slot;

    /// <summary>
    /// 租约捕获的槽位版本号。
    /// </summary>
    private readonly int _version;

    /// <summary>
    /// 初始化 <see cref="CapabilityBridgeLease{TContract}"/>（仅机制构造）。
    /// </summary>
    /// <param name="slot">契约槽位。</param>
    /// <param name="version">获取租约时的槽位版本号。</param>
    internal CapabilityBridgeLease(CapabilityContractSlot<TContract> slot, int version)
    {
        _slot = slot;
        _version = version;
    }

    /// <summary>
    /// 获取契约实现（每次访问复查活跃位；已撤销 / 已替换抛 <see cref="CapabilityBridgeUnavailableException"/>）。
    /// </summary>
    public TContract Contract
    {
        get { return _slot.GetPublished(_version); }
    }
}
