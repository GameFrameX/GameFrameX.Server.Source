// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of citizens as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织不承担任何责任，开发者应自行承担。
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

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 社交能力包级装配选项（<see cref="OnlineSocialHostingExtensions.AddOnlineSocialRuntime"/> 回调承载面，C231）。
/// <para>
/// 维护约束（能力族全局约定，C227/C229 裁定）：编排参数一律经 <c>Action&lt;TOptions&gt;</c> 回调承载（.NET
/// <c>AddXxx(options =&gt; ...)</c> 惯例）；P3 期无包级编排参数（存储替换走 <c>UseOnlineSocial*Store</c> 缝族，
/// 配置段绑定归组合根），成员随真实需求落位；禁止回退平铺配置键。
/// </para>
/// </summary>
/// <remarks>
/// Package-level assembly options for the social capability (the callback face of
/// <see cref="OnlineSocialHostingExtensions.AddOnlineSocialRuntime"/>, C231). Following the capability-family
/// global convention (ruled by C227/C229), orchestration parameters ride an <c>Action&lt;TOptions&gt;</c> callback;
/// P3 carries no package-level knobs (store replacement goes through the <c>UseOnlineSocial*Store</c> seam family
/// and config-section binding belongs to composition roots); members land as real needs appear; flat
/// configuration keys stay forbidden.
/// </remarks>
public sealed class OnlineSocialOptions
{
}
