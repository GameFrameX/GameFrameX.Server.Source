//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
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

namespace GameFrameX.Online.Identity;

/// <summary>
/// 同一账号多端登录策略（vault:C3 S2.3：策略选项写入契约，默认策略待产品确认，代码可配不锁死）。
/// <para>
/// 维护约束：策略按「租户 + App + 玩家」维度生效；切换策略只影响新登录会话，
/// 既有会话按签发时策略处理；在线计数随策略语义为 1 或 N（VC-2.4）。
/// 本仓默认 <see cref="LatestWins"/>（顶号），装配层可按租户覆盖。
/// </para>
/// </summary>
public enum OnlineMultiDevicePolicy
{
    /// <summary>单端：已有活跃会话时拒绝新登录（7xxx 风控段外的 5xxx 状态语义由服务层映射）。</summary>
    SingleDevice = 1,

    /// <summary>顶号（默认）：新登录强制下线旧会话（旧 Token 立即失效，会话终态 Kicked/Replaced）。</summary>
    LatestWins = 2,

    /// <summary>并存：允许多端会话并存，各自独立 Token 与 Presence 记录。</summary>
    Coexist = 3,
}
