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

namespace GameFrameX.Online.Match;

/// <summary>
/// 玩法扩展接口（vault:C6「对局超时、异常退出、托管和观战必须有玩法级扩展点」）。
/// <para>
/// 维护约束（红线）：实现方**只能**通过传入的 <see cref="OnlineMatch"/> 读写玩法状态，
/// 且必须保持纯裁决语义——<see cref="ApplyInput"/> 只依据服务端持有的状态与成员身份判定合法性，
/// 绝不读取任何来自客户端的「结果」（VC-5.2）。实现方自行产生的
/// <see cref="OnlineMatchServerEvent"/> 不携带服务器序号，序号一律由 Actor 统一分配。
/// </para>
/// <para>
/// 不实现本接口的玩法无法被 <see cref="OnlineMatchRuntime"/> 装配；每个
/// <see cref="Mode"/> 至多注册一个实现。
/// </para>
/// </summary>
public interface IOnlineMatchGame
{
    /// <summary>
    /// 获取本玩法对应的模式标识（与 <see cref="OnlineMatch.Mode"/> 匹配）。
    /// </summary>
    int Mode
    {
        get;
    }

    /// <summary>
    /// 获取本玩法要求的最小参与人数。
    /// </summary>
    int MinPlayers
    {
        get;
    }

    /// <summary>
    /// 获取本玩法允许的最大参与人数。
    /// </summary>
    int MaxPlayers
    {
        get;
    }

    /// <summary>
    /// 创建玩法初始状态。
    /// </summary>
    /// <param name="match">对局（成员表已就位）。</param>
    /// <returns>玩法私有状态载荷。</returns>
    byte[] CreateInitialState(OnlineMatch match);

    /// <summary>
    /// 裁决并应用一次玩家输入。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="member">提交者（已通过成员与状态校验）。</param>
    /// <param name="input">输入意图。</param>
    /// <returns>裁决结果；未接受时 <see cref="OnlineMatchGameStepResult.GameState"/> 保持不变。</returns>
    OnlineMatchGameStepResult ApplyInput(OnlineMatch match, OnlineMatchMember member, OnlineMatchInput input);

    /// <summary>
    /// 按时间推进玩法（回合超时判定等，VC-5.12）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="emittedEvents">
    /// 本次推进要追加的服务器事件（可为空列表）。序号由 Actor 统一分配，玩法不得预填
    /// <see cref="OnlineMatchServerEvent.Sequence"/>——客户端正是靠事件流感知「超时判负」这类
    /// 无输入触发的推进，仅有状态变化而不发事件会让重连增量补发出现空洞（VC-5.6）。
    /// </param>
    /// <returns>本次推进是否改写了玩法状态或成员状态；未改变返回 <c>false</c>（避免 Tick 产生无意义落库）。</returns>
    bool Advance(OnlineMatch match, long nowUnixMilliseconds, List<OnlineMatchServerEvent> emittedEvents);

    /// <summary>
    /// 判定玩法是否已分出结果。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <returns>已分出结果返回 <c>true</c>。</returns>
    bool IsCompleted(OnlineMatch match);

    /// <summary>
    /// 产出权威结算结果（仅在 <see cref="IsCompleted"/> 为 <c>true</c> 时调用）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <returns>结算结果（<see cref="OnlineMatchResult.MatchResultId"/> 由结算服务统一分配）。</returns>
    OnlineMatchResult BuildResult(OnlineMatch match);
}
