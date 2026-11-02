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

using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.AdminApi;

public sealed class AdminActionRegistry
{
    /// <summary>
    /// action 注册表。
    /// </summary>
    private readonly Dictionary<string, AdminActionRegistration> _actions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 当前贡献者能力 Id（组装器逐模块注册前设置；同名冲突诊断用）。
    /// </summary>
    private string _currentOwnerCapabilityId;

    /// <summary>
    /// 注册 action（同名重复 = 装配失败）。
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

        if (_actions.ContainsKey(action))
        {
            // Localization: Capability.Hosting.AdminApi.ActionNameDuplicate - Admin action 重复注册。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.ActionNameDuplicate), action, _actions[action].OwnerCapabilityId));
        }

        _actions[action] = new AdminActionRegistration(handler, wrapIdempotency, _currentOwnerCapabilityId);
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

    /// <summary>
    /// 设置当前贡献者能力 Id（组装器逐模块注册前调用；同名冲突诊断与审计用）。
    /// </summary>
    /// <param name="ownerCapabilityId">贡献者能力 Id（元端点注册时可传机制标记）。</param>
    internal void SetCurrentOwnerCapabilityId(string ownerCapabilityId)
    {
        _currentOwnerCapabilityId = ownerCapabilityId;
    }
}
