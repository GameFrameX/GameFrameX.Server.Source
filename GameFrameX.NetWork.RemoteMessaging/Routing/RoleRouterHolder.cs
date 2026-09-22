// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 跨 Role 路由器全局持有者（C143c）。
/// </summary>
/// <remarks>
/// Global holder for the process-wide <see cref="IRoleRouter"/> instance,
/// following the UnifiedMessageSenderHolder pattern (this stack has no DI container;
/// global infrastructure is reached through typed holders).
/// <see cref="Initialize"/> is called by the launch flow (GameApp) right after the
/// process role snapshot is published; business code reads <see cref="Current"/>.
/// </remarks>
public static class RoleRouterHolder
{
    /// <summary>
    /// 全局路由器实例。
    /// </summary>
    /// <remarks>
    /// The global router instance.
    /// </remarks>
    private static IRoleRouter _router;

    /// <summary>
    /// 初始化互斥锁。
    /// </summary>
    /// <remarks>
    /// The initialization lock.
    /// </remarks>
    private static readonly object InitializeLock = new object();

    /// <summary>
    /// 获取全局跨 Role 路由器实例。必须在调用 <see cref="Initialize"/> 之后使用。
    /// </summary>
    /// <remarks>
    /// Gets the process-wide role router. Must be used only after <see cref="Initialize"/>.
    /// Reading before initialization throws instead of returning null: routing against a
    /// missing router must fail loudly at the call site (never a silent drop).
    /// </remarks>
    /// <value>全局路由器实例 / The process-wide router instance</value>
    /// <exception cref="InvalidOperationException">当尚未调用 <see cref="Initialize"/> 时抛出 / Thrown when <see cref="Initialize"/> has not been called</exception>
    public static IRoleRouter Current
    {
        get
        {
            if (_router == null)
            {
                throw new InvalidOperationException("RoleRouterHolder has not been initialized; call RoleRouterHolder.Initialize during launch before routing.");
            }

            return _router;
        }
    }

    /// <summary>
    /// 初始化全局路由器。启动流程调用一次。
    /// </summary>
    /// <remarks>
    /// Initializes the process-wide router. Called once during launch
    /// (after the role snapshot is published, before any host starts).
    /// </remarks>
    /// <param name="router">路由器实例 / The router instance</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="router"/> 为 null 时抛出 / Thrown when <paramref name="router"/> is null</exception>
    public static void Initialize(IRoleRouter router)
    {
        ArgumentNullException.ThrowIfNull(router, nameof(router));

        lock (InitializeLock)
        {
            _router = router;
        }
    }
}
