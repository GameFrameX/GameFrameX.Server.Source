// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legitimate rights and interests of others as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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


namespace GameFrameX.Discovery;

/// <summary>
/// 当前进程已激活发现层的公共槽位：Provider Runtime 激活时登记写侧，宿主就绪时无分派地把心跳从 Booting 切到 Active。
/// </summary>
/// <remarks>
/// The process-wide slot for the activated discovery layer. Provider runtimes
/// (Mongo / PostgreSQL) publish their started <see cref="DiscoveryRegistry"/> here
/// at the end of <c>Activate</c>, so startup flows flip the announced status from
/// Booting to Active through <see cref="MarkActiveAsync"/> without branching on the
/// database provider enum. The slot holds whichever runtime actually activated
/// (rather than what a setting claims) and stays null when the discovery layer was
/// never activated — <see cref="MarkActiveAsync"/> is then a harmless no-op.
/// </remarks>
public static class ActiveDiscoveryRuntime
{
    /// <summary>
    /// 已登记的心跳写侧（激活完成时由 Provider Runtime 写入）。
    /// </summary>
    /// <remarks>
    /// The bound heartbeat write side, published by the provider runtime once its activation completes.
    /// </remarks>
    private static volatile DiscoveryRegistry _registry;

    /// <summary>
    /// 登记激活完成的心跳写侧（由 Provider Runtime 在 Activate 末尾调用）。
    /// </summary>
    /// <remarks>
    /// Publishes the started registry. Production activation is mutually exclusive
    /// per process (the provider runtimes' first-call-wins guard), so this runs at
    /// most once; a later write replaces an earlier one so test-isolation flows
    /// that reset and re-activate keep the slot consistent.
    /// </remarks>
    /// <param name="registry">激活完成的心跳写侧 / The started heartbeat write side</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="registry"/> 为 null 时抛出 / Thrown when <paramref name="registry"/> is null</exception>
    public static void Bind(DiscoveryRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry, nameof(registry));

        _registry = registry;
    }

    /// <summary>
    /// 把本进程心跳从 Booting 切换为 Active（启动阶段真正完成、服务就绪后调用）。
    /// </summary>
    /// <remarks>
    /// Flips this process's announced status from Booting to Active on the bound
    /// registry. The startup flows await this right after their readiness point
    /// (<c>MarkStartUpReady</c>: databases, components, and listeners up) so other
    /// processes never discover and route traffic to a not-yet-ready instance.
    /// No-op when the discovery layer was not activated or the write side was
    /// skipped (no advertise identity).
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public static Task MarkActiveAsync(CancellationToken cancellationToken = default)
    {
        return _registry?.MarkActiveAsync(cancellationToken) ?? Task.CompletedTask;
    }
}
