//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Timeline;

/// <summary>
/// 玩家时间线中**记录型行**的事件类型与来源词表。
/// <para>
/// 维护约束：本词表只承载域内**不是事件**的记录（身份实体、配置命中）——它们在各自域内没有事件常量可复用，
/// 故在此集中定义一份，避免多处手拼字符串而漂移。**事件型行**（会话 / 资产 / 对局 / 处罚）一律复用各域既有常量
/// （<c>OnlineSessionEvents</c> / <c>OnlineAssetEvents</c> / <c>OnlineMatchRuntimeEvents</c> / <c>OnlineSocialEvents</c>），
/// 禁止在本词表另造同义类型。
/// </para>
/// <para>
/// 类型值形如 <c>Online.&lt;域&gt;.&lt;动作&gt;</c>，与各域事件的构词规则一致；消费方（Admin）不解析该值，
/// 只按原值展示与过滤。
/// </para>
/// </summary>
public static class OnlinePlayerTimelineEventTypes
{
    /// <summary>
    /// 账号注册（<c>OnlineGameAccount</c> 建档，属身份域记录而非事件）。
    /// </summary>
    public const string AccountRegistered = "Online.Identity.AccountRegistered";

    /// <summary>
    /// 玩家档案创建（<c>OnlinePlayerProfile</c> 建档）。
    /// </summary>
    public const string PlayerProfileCreated = "Online.Identity.PlayerProfileCreated";

    /// <summary>
    /// 身份绑定（用户名 / 邮箱 / 设备 / 渠道 / 三方账号之一绑定到账号）。
    /// </summary>
    public const string IdentityBound = "Online.Identity.IdentityBound";

    /// <summary>
    /// 身份解绑（<c>UnboundAtTime</c> 落点）。
    /// </summary>
    public const string IdentityUnbound = "Online.Identity.IdentityUnbound";

    /// <summary>
    /// 运营配置命中（配置域记录，经 <see cref="IOnlineConfigHitProbe"/> 进入时间线）。
    /// </summary>
    public const string ConfigHit = "Online.LiveOps.ConfigHit";

    /// <summary>
    /// 身份域来源标识（记录型行的 <c>Source</c> 取值）。
    /// </summary>
    public const string SourceIdentity = "online-identity";

    /// <summary>
    /// 配置域来源标识（探针未给出具体来源域时的兜底取值）。
    /// </summary>
    public const string SourceConfig = "online-config";
}
