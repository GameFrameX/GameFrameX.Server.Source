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
/// 能力桥消费入口（按契约类型的进程内定位；发布侧只经
/// <see cref="CapabilityActivationContext"/> 的 <c>Bridges.Publish&lt;TContract&gt;</c>，撤销由宿主停止期自动完成）。
/// <para>
/// 维护约束（桥显式失败红线）：消费方逐调用复查活跃位；发布者缺席 <see cref="TryAcquire"/> 返回 false、
/// 已撤销 / 已替换抛 <see cref="CapabilityBridgeUnavailableException"/>（族层映射为 8002）——不回旧数据、不降级、
/// 各域不得自写缺席日志。本入口只在 Hotfix / Apps 消费侧合法（GFX0018 符号检查）。
/// </para>
/// </summary>
/// <typeparam name="TContract">契约类型（每域一个契约接口）。</typeparam>
public static class CapabilityBridge<TContract> where TContract : class
{
    /// <summary>
    /// 契约槽位（与 <see cref="CapabilityHook{TContract}"/> 各自独立、共用同一套槽位实现）。
    /// </summary>
    private static readonly CapabilityContractSlot<TContract> Slot = new CapabilityContractSlot<TContract>();

    /// <summary>
    /// 尝试获取契约租约。
    /// </summary>
    /// <param name="lease">输出：契约租约（发布者缺席为 null）。</param>
    /// <returns>发布者在场返回 true；缺席返回 false（消费方按依赖不可用显式失败处理）。</returns>
    public static bool TryAcquire(out CapabilityBridgeLease<TContract> lease)
    {
        return Slot.TryAcquire(out lease);
    }

    /// <summary>
    /// 发布契约实例（仅激活上下文的桥发布面调用；发布者身份 = 当前模块 Id；同一契约重复发布 fail-fast）。
    /// </summary>
    /// <param name="contract">契约实现。</param>
    /// <param name="publisherId">发布者能力 Id。</param>
    /// <returns>撤销凭据（宿主停止期统一撤销）。</returns>
    internal static IDisposable Publish(TContract contract, string publisherId)
    {
        return Slot.Publish(contract, publisherId);
    }
}
