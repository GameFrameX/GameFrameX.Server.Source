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

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Match;

/// <summary>
/// 重连服务（vault:C6 S5.6「断线 / 重连 / 超窗处置」的应用层入口）。
/// <para>
/// 维护约束（红线）：令牌只在此处签发（断线时），且在**服务端**绑定 (MatchId, PlayerId)。
/// 重连判定完全由 Actor 依据成员表 + 窗口 + 令牌三重校验做出，
/// 服务层只负责组装「快照 + 缺失增量」的补发内容——它不做任何状态判定（VC-5.6 / VC-5.7）。
/// </para>
/// </summary>
public sealed class OnlineMatchReconnectService
{
    /// <summary>对局运行时。</summary>
    private readonly OnlineMatchRuntime _runtime;

    /// <summary>
    /// 初始化重连服务。
    /// </summary>
    /// <param name="runtime">对局运行时。</param>
    public OnlineMatchReconnectService(OnlineMatchRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>
    /// 标记玩家断线并签发重连令牌（窗口起点 = 本调用时刻）。
    /// </summary>
    /// <param name="scope">请求作用域（必须含玩家主体位）。</param>
    /// <param name="matchId">对局标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重连令牌；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineMatchReconnectToken>> DisconnectAsync(OnlineScope scope, string matchId, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        if (scope == null || scope.PlayerId <= 0)
        {
            return OnlineResult<OnlineMatchReconnectToken>.Fail(OnlineErrorCode.ParameterInvalid, "断线操作必须带玩家主体位");
        }

        var actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, matchId, cancellationToken).ConfigureAwait(false);
        if (actor == null)
        {
            return OnlineResult<OnlineMatchReconnectToken>.Fail(OnlineErrorCode.ResourceNotFound, "对局不存在");
        }

        var token = "rt-" + Guid.NewGuid().ToString("N");
        var marked = await actor.MarkDisconnectedAsync(scope, token, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
        if (!marked.IsSuccess)
        {
            return OnlineResult<OnlineMatchReconnectToken>.Fail(marked.Code, marked.Message);
        }

        return OnlineResult<OnlineMatchReconnectToken>.Ok(new OnlineMatchReconnectToken
        {
            Token = token,
            MatchId = actor.MatchId,
            PlayerId = scope.PlayerId,
            IssuedTime = nowUnixMilliseconds,
            ExpiresTime = FindReconnectDeadline(marked.Data, scope.PlayerId),
        });
    }

    /// <summary>
    /// 窗口内重连：恢复成员并返回「完整快照 + 客户端缺失的增量」。
    /// </summary>
    /// <param name="scope">请求作用域（必须含玩家主体位）。</param>
    /// <param name="context">重连上下文。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重连结果；失败返回对应错误码（超窗为 <see cref="OnlineErrorCode.NetworkTimeout"/>）。</returns>
    public async Task<OnlineResult<OnlineMatchReconnectResult>> ReconnectAsync(OnlineScope scope, OnlineMatchReconnectContext context, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        if (scope == null || context == null)
        {
            return OnlineResult<OnlineMatchReconnectResult>.Fail(OnlineErrorCode.ParameterInvalid, "作用域与重连上下文不能为空");
        }

        if (scope.PlayerId != context.PlayerId)
        {
            return OnlineResult<OnlineMatchReconnectResult>.Fail(OnlineErrorCode.ScopeDenied, "重连上下文的玩家与作用域不一致");
        }

        var actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, context.MatchId, cancellationToken).ConfigureAwait(false);
        if (actor == null)
        {
            return OnlineResult<OnlineMatchReconnectResult>.Fail(OnlineErrorCode.ResourceNotFound, "对局不存在");
        }

        var resumed = await actor.ResumeAsync(scope, context.ReconnectToken, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
        if (!resumed.IsSuccess)
        {
            return OnlineResult<OnlineMatchReconnectResult>.Fail(resumed.Code, resumed.Message);
        }

        var snapshot = resumed.Data;
        OnlineMatchDelta delta = null;
        string instruction;
        if (actor.CanServeDelta(context.LastAcknowledgedSequence))
        {
            delta = actor.BuildDelta(context.LastAcknowledgedSequence);
        }

        if (delta == null)
        {
            instruction = "增量不可用，客户端必须以快照为准重建本地状态";
        }
        else
        {
            instruction = "先应用快照，再按序号应用增量";
        }

        return OnlineResult<OnlineMatchReconnectResult>.Ok(new OnlineMatchReconnectResult
        {
            Succeeded = true,
            FailureCode = OnlineErrorCode.None,
            MatchId = actor.MatchId,
            State = snapshot.State,
            ServerSequence = snapshot.ServerSequence,
            Snapshot = snapshot,
            Delta = delta,
            Instruction = instruction,
        });
    }

    /// <summary>
    /// 从快照中读取断线成员的重连截止时刻（窗口长度只由 Actor 计算，服务层不复制该规则）。
    /// </summary>
    /// <param name="snapshot">断线标记后的快照。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>重连截止时刻；玩家不在快照中返回 0。</returns>
    private static long FindReconnectDeadline(OnlineMatchSnapshot snapshot, long playerId)
    {
        if (snapshot == null || snapshot.Members == null)
        {
            return 0;
        }

        foreach (var member in snapshot.Members)
        {
            if (member != null && member.PlayerId == playerId)
            {
                return member.ReconnectDeadlineTime;
            }
        }

        return 0;
    }
}
