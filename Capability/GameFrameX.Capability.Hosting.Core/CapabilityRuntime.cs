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
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 能力组合产物（一次 <see cref="CapabilityComposition.Compose"/> 的双面承载：业务宿主 + 管理面 + 报告；
/// 未启用的面为 null）。
/// <para>
/// 维护约束（启停序）：启动 = 业务宿主先启（拓扑序 Activate + 调度）再启管理面（监听开放时桥已发布）；
/// 停止 = 管理面先停再停业务宿主（停调度 → 逆序 Deactivate → 机制自动撤销全部桥）。
/// admin-only 形态（<c>EnableRuntime=false</c>）下 <see cref="Host"/> 为 null——模块已注册（管理面 action 可用）
/// 但宿主不激活、调度不启动。
/// </para>
/// </summary>
public sealed class CapabilityRuntime
{
    /// <summary>
    /// 初始化 <see cref="CapabilityRuntime"/>（仅机制组合器构造）。
    /// </summary>
    /// <param name="host">业务能力宿主（业务面未启用时为 null）。</param>
    /// <param name="adminApi">管理面（未启用时为 null）。</param>
    /// <param name="report">能力报告（业务与管理面共用同一份）。</param>
    internal CapabilityRuntime(CapabilityHost host, ICapabilityAdminApiPlane adminApi, CapabilityReport report)
    {
        Host = host;
        AdminApi = adminApi;
        Report = report;
    }

    /// <summary>
    /// 获取业务能力宿主（业务面未启用时为 null）。
    /// </summary>
    public CapabilityHost Host { get; }

    /// <summary>
    /// 获取管理面（未启用时为 null）。
    /// </summary>
    public ICapabilityAdminApiPlane AdminApi { get; }

    /// <summary>
    /// 获取启动能力报告。
    /// </summary>
    public CapabilityReport Report { get; }

    /// <summary>
    /// 启动（业务宿主先启：拓扑序 Activate + 调度；管理面后启：监听开放时桥已发布；可重复调用幂等）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Host != null)
        {
            await Host.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        if (AdminApi != null)
        {
            await AdminApi.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 停止（管理面先停；业务宿主后停：停调度 → 逆序 Deactivate → 撤销全部桥）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (AdminApi != null)
        {
            await AdminApi.StopAsync().ConfigureAwait(false);
        }

        if (Host != null)
        {
            await Host.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
