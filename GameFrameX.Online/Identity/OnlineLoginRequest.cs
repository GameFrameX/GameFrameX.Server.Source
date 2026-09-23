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
//   please see the LICENSE file in the root directory of the source code.
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

namespace GameFrameX.Online.Identity;

/// <summary>
/// 登录解析请求（<see cref="OnlineIdentityService.ResolveLoginAsync"/> 的唯一提交形态，客户端登录主路径）。
/// <para>
/// 维护约束：请求只表达「以什么身份登录哪个区服」；身份/账号/玩家三件套一律由服务端解析或生成，
/// 客户端不得自行拼接身份关系。设备信息三连（<see cref="DeviceIdentifier"/>/<see cref="DevicePlatform"/>）
/// 为可选追加维度，后续设备字段演进只改本载荷。
/// </para>
/// </summary>
public sealed class OnlineLoginRequest
{
    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    /// <remarks>Gets or sets the tenant id.</remarks>
    public long TenantId { get; init; }

    /// <summary>
    /// 获取或设置应用标识。
    /// </summary>
    /// <remarks>Gets or sets the app id.</remarks>
    public long AppId { get; init; }

    /// <summary>
    /// 获取或设置区服标识。
    /// </summary>
    /// <remarks>Gets or sets the server id.</remarks>
    public long ServerId { get; init; }

    /// <summary>
    /// 获取或设置身份类型。
    /// </summary>
    /// <remarks>Gets or sets the identity kind.</remarks>
    public OnlineIdentityKind Kind { get; init; }

    /// <summary>
    /// 获取或设置身份标识串。
    /// </summary>
    /// <remarks>Gets or sets the identity identifier.</remarks>
    public string Identifier { get; init; }

    /// <summary>
    /// 获取或设置自动注册时的玩家显示名（可空，缺省按玩家标识生成）。
    /// </summary>
    /// <remarks>Gets or sets the display name for auto-registration (optional; defaults to a generated name).</remarks>
    public string PlayerName { get; init; }

    /// <summary>
    /// 获取或设置登录设备标识（可空；提供时参与换绑策略校验）。
    /// </summary>
    /// <remarks>Gets or sets the login device identifier (optional; participates in rebinding policy checks).</remarks>
    public string DeviceIdentifier { get; init; }

    /// <summary>
    /// 获取或设置登录设备平台描述（可空）。
    /// </summary>
    /// <remarks>Gets or sets the login device platform description (optional).</remarks>
    public string DevicePlatform { get; init; }
}
