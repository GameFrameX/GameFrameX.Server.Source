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
/// 对局输入（vault:C6 S5.2「客户端发送输入或操作意图，不提交最终胜负、排名或奖励」）。
/// <para>
/// 维护约束（红线）：本类型**只有意图**，没有结果字段——胜负、排名、奖励一律由服务端产出
/// （VC-5.2：客户端上报「我方胜利」无效，因为协议里根本没有承载它的位置）。
/// <see cref="ClientSequence"/> 仅用于重复包与乱序包判定，不参与权威状态推进；
/// 权威序号由服务端生成（<see cref="OnlineMatchInputAck.ServerSequence"/>）。
/// </para>
/// </summary>
public sealed class OnlineMatchInput
{
    /// <summary>
    /// 获取或设置提交玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置客户端序号（该玩家视角下严格递增，从 1 开始）。
    /// </summary>
    public long ClientSequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置动作标识（玩法私有语义，由 <see cref="IOnlineMatchGame"/> 校验合法性）。
    /// </summary>
    public int ActionId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置客户端时刻（UTC 毫秒；仅用于诊断与延迟测量，不参与判定）。
    /// </summary>
    public long ClientTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关联标识（可空；用于端到端链路追踪）。
    /// </summary>
    public string CorrelationId
    {
        get;
        set;
    }

    /// <summary>
    /// 复制输入。
    /// </summary>
    /// <returns>输入副本。</returns>
    public OnlineMatchInput Copy()
    {
        return new OnlineMatchInput
        {
            PlayerId = PlayerId,
            ClientSequence = ClientSequence,
            ActionId = ActionId,
            ClientTime = ClientTime,
            CorrelationId = CorrelationId,
        };
    }
}
