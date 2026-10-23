//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes and liabilities arising from secondary development,
//   本组织不承担任何责任，开发者须自行承担。
//   The organization shall not be responsible for any disputes; developers shall bear the responsibility.
//  ==========================================================================================

using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Bridges;

/// <summary>
/// 匹配域桥发布器：进程级类型化 holder——
/// 组合根（Game 型启动）在业务运行时启动后发布 <see cref="OnlineMatchDomainBridgeLease"/>，
/// 进程停止前撤销；消费方（Hotfix 薄适配器）逐请求读取 <see cref="Current"/>，不缓存引用。
/// <para>
/// 维护约束：发布 / 撤销必须成对出现在同一组合根（AppStartUpGame）；再发布替换并吊销既有租约，
/// 使任何历史引用立即显式失效；holder 不承载能力装配与路由——只有发布 / 撤销生命周期对。
/// </para>
/// </summary>
public static class OnlineMatchDomainBridgeHolder
{
    /// <summary>当前租约（volatile 读；未发布 / 已撤销为 null）。</summary>
    private static volatile OnlineMatchDomainBridgeLease current;

    /// <summary>
    /// 获取当前租约（未发布或已撤销时为 <c>null</c>；调用方不得跨请求缓存本值）。
    /// </summary>
    public static OnlineMatchDomainBridgeLease Current
    {
        get { return current; }
    }

    /// <summary>
    /// 获取是否已发布有效租约。
    /// </summary>
    public static bool IsPublished
    {
        get { return current != null; }
    }

    /// <summary>
    /// 发布租约（组合根在业务运行时启动后调用）：替换并撤销既有租约，返回新租约。
    /// </summary>
    /// <param name="domain">域契约面。</param>
    /// <param name="authorizedScope">授权作用域（三元组）。</param>
    /// <returns>新发布的租约。</returns>
    public static OnlineMatchDomainBridgeLease Publish(IOnlineMatchDomain domain, OnlineScope authorizedScope)
    {
        var lease = new OnlineMatchDomainBridgeLease(domain, authorizedScope);
        var previous = current;
        current = lease;
        previous?.Deactivate();
        return lease;
    }

    /// <summary>
    /// 撤销租约（组合根在进程停止路径调用）：清空当前位并吊销全部既有租约（幂等）。
    /// </summary>
    public static void Retract()
    {
        var previous = current;
        current = null;
        previous?.Deactivate();
    }
}
