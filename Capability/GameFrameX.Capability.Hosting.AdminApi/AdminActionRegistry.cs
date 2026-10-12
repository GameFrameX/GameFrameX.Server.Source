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

namespace GameFrameX.Capability.Hosting.AdminApi;

/// <summary>
/// admin action 处理器委托（返回业务数据载荷；作用域已通过守卫）。
/// </summary>
/// <param name="request">请求包装。</param>
/// <param name="scope">已通过守卫的作用域（客户端声称 = 宿主授权）。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>业务数据载荷（成功响应的 Data，必须非 null）。</returns>
public delegate Task<object> AdminActionHandler(AdminApiRequest request, AdminApiClaimedScope scope, CancellationToken cancellationToken);

/// <summary>
/// admin action 注册表（能力侧向管理面贡献 action 的唯一入口；大小写不敏感）。
/// <para>
/// 维护约束：action 名跨能力全宿主唯一（重复注册即后者覆盖前者并视为装配缺陷，
/// 组合期对账会以「manifest 声明 ⊆ 实注册」拦截漏注册）。
/// </para>
/// </summary>
public sealed class AdminActionRegistry
{
    /// <summary>
    /// action 注册表。
    /// </summary>
    private readonly Dictionary<string, AdminActionRegistration> _actions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册 action。
    /// </summary>
    /// <param name="action">action 名（路由末段）。</param>
    /// <param name="handler">处理器（返回业务数据载荷；抛 <see cref="AdminActionException"/> 映射协议码）。</param>
    /// <param name="wrapIdempotency">是否经调度器层幂等包裹。</param>
    public void Register(string action, AdminActionHandler handler, bool wrapIdempotency)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            // Localization: Capability.Hosting.AdminApi.ActionNameRequired - Action 名称不能为 null 或空。
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.ActionNameRequired), nameof(action));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _actions[action] = new AdminActionRegistration(handler, wrapIdempotency);
    }

    /// <summary>
    /// 获取已注册 action 名集合（诊断与测试用）。
    /// </summary>
    public IReadOnlyCollection<string> ActionNames
    {
        get { return _actions.Keys; }
    }

    /// <summary>
    /// 查找 action 注册项。
    /// </summary>
    /// <param name="action">action 名。</param>
    /// <returns>注册项；未注册返回 null。</returns>
    public AdminActionRegistration Find(string action)
    {
        return action == null ? null : _actions.TryGetValue(action, out var registration) ? registration : null;
    }
}

/// <summary>
/// admin action 注册项。
/// </summary>
public sealed class AdminActionRegistration
{
    /// <summary>
    /// 初始化 <see cref="AdminActionRegistration"/>。
    /// </summary>
    /// <param name="handler">处理器。</param>
    /// <param name="wrapIdempotency">是否幂等包裹。</param>
    public AdminActionRegistration(AdminActionHandler handler, bool wrapIdempotency)
    {
        Handler = handler;
        WrapIdempotency = wrapIdempotency;
    }

    /// <summary>
    /// 获取处理器。
    /// </summary>
    public AdminActionHandler Handler { get; }

    /// <summary>
    /// 获取是否经调度器层幂等包裹。
    /// </summary>
    public bool WrapIdempotency { get; }
}


/// <summary>
/// admin action 贡献者契约（能力模块实现本接口向管理面注册 action；与 ICapabilityModule 搭配使用）。
/// </summary>
public interface IAdminActionContributor
{
    /// <summary>
    /// 向注册表贡献本能力的 admin action。
    /// </summary>
    /// <param name="registry">action 注册表。</param>
    void RegisterAdminActions(AdminActionRegistry registry);
}