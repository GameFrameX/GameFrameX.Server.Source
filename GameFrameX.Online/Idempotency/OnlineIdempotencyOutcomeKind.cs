// ==========================================================================================
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

namespace GameFrameX.Online.Idempotency;

/// <summary>
/// Online 幂等判定结果类型（vault:C2：相同键相同请求返回原结果；相同键不同请求返回冲突错误 6xxx）。
/// <para>
/// 语义对照 Foundation <c>IdempotencyDecisionKind</c>（Execute/Replay/Conflict/Busy），
/// 另增 <see cref="InvalidKey"/> 承载 Online 侧业务键格式校验失败（4xxx，未触达 Foundation 判定）。
/// </para>
/// </summary>
public enum OnlineIdempotencyOutcomeKind
{
    /// <summary>
    /// 允许执行：调用方执行业务并在结束后经 Complete/Fail 落定记录。
    /// </summary>
    Execute = 0,

    /// <summary>
    /// 成功回放：相同键相同请求命中首次结果，直接返回 <see cref="OnlineIdempotencyOutcome.FirstResponse"/>，不重复执行。
    /// </summary>
    Replay = 1,

    /// <summary>
    /// 键冲突：相同幂等键承载了不同业务意图（同键不同请求体），拒绝并映射 6xxx（VC-1.4）。
    /// </summary>
    Conflict = 2,

    /// <summary>
    /// 并发占位未落定：判定轮次内并发方仍在执行，映射 8xxx 可重试（VC-1.3 并发半边）。
    /// </summary>
    Busy = 3,

    /// <summary>
    /// 业务键非法：Online 侧键格式校验失败（空/超长/非法字符），映射 4xxx，未触达幂等存储。
    /// </summary>
    InvalidKey = 4,
}
