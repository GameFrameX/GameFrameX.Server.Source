// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://github.com/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Foundation;

using GameFrameX.Capability.Online.Abstractions.Scope;

/// <summary>
/// 宿主授权作用域桥实现（透出族基建预置的 <see cref="OnlineScope"/> 三元组）。
/// <para>
/// 维护约束：实例持有的作用域来自族配置段（Tenant / App / Server），全宿主单例语义；本类只透出
/// 不构造——消费方（Hotfix 协议薄适配器）<c>TryAcquire</c> 失败即显式 8002，不得回落自造默认作用域。
/// </para>
/// </summary>
public sealed class OnlineAuthorizedScopeProvider : IOnlineAuthorizedScopeProvider
{
    /// <summary>
    /// 族基建预置的授权作用域。
    /// </summary>
    private readonly OnlineScope _authorizedScope;

    /// <summary>
    /// 初始化 <see cref="OnlineAuthorizedScopeProvider"/>。
    /// </summary>
    /// <param name="authorizedScope">族基建预置的授权作用域。</param>
    public OnlineAuthorizedScopeProvider(OnlineScope authorizedScope)
    {
        _authorizedScope = authorizedScope ?? throw new ArgumentNullException(nameof(authorizedScope));
    }

    /// <summary>
    /// 获取构造时注入的族基建预置授权作用域，只透出不构造。
    /// </summary>
    /// <remarks>
    /// Gets the family-provisioned authorized scope injected at construction; the provider only exposes it and never builds a default.
    /// </remarks>
    /// <value>授权作用域，恒不为 null / Authorized scope, never null</value>
    public OnlineScope AuthorizedScope
    {
        get { return _authorizedScope; }
    }
}
