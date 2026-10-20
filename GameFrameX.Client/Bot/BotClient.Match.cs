// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and related international regulations.
//
//  使用本项目须严格遵守相应法律法规与开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录的 LICENSE 文件。
//  please refer to the LICENSE file at the source code root for the full license text.
//
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any disputes and liabilities arising from secondary development,
//  本组织不承担任何责任，开发者须自行承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository: https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Client.Bot;

/// <summary>
/// 匹配场景脚本（C246 匹配域接管真会话 E2E 载体，沿好友 / RPS 场景形态）：
/// 登录成功后入队（1v1 / 评分 1000）——即时窗口成组者响应直接带对局标识，
/// 等待方经 <c>NotifyMatchMade</c> 服务端推送或状态轮询获知，随后双方各自入场断言同一运行态对局。
/// </summary>
public sealed partial class BotClient
{
    /// <summary>匹配状态轮询上限（等待方兜底；推送先到则不再轮询）。</summary>
    private const int MaxMatchStatusPolls = 20;

    /// <summary>匹配状态轮询间隔（毫秒）。</summary>
    private const int MatchStatusPollIntervalMs = 500;

    /// <summary>
    /// 启动匹配场景：发送入队请求。
    /// </summary>
    private void StartMatchScenario()
    {
        // Localization: Client.Bot.MatchScenarioStarted - 机器人-{0}匹配场景启动（1v1，评分 1000）
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.MatchScenarioStarted, m_BotName));
        SendToTransport(new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000 });
    }

    /// <summary>
    /// 入队响应：即时窗口成组（MatchId≠0）直接入场；排队中转入推送等待 + 有界状态轮询。
    /// </summary>
    /// <param name="msg">入队响应。</param>
    private void OnMatchEnqueueResponse(RespMatchEnqueue msg)
    {
        // Localization: Client.Bot.MatchEnqueue - 机器人-{0}入队响应 MatchId={1} ErrorCode={2}
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.MatchEnqueue, m_BotName, msg.MatchId, msg.ErrorCode));
        if (msg.ErrorCode != 0)
        {
            return;
        }

        if (msg.MatchId != 0)
        {
            SendMatchEnter(msg.MatchId);
            return;
        }

        _matchStatusPollAttempts = 0;
        ScheduleMatchStatusPoll();
    }

    /// <summary>
    /// 匹配成功推送（服务端单向通知；本行即真会话推送实证）。
    /// </summary>
    /// <param name="msg">推送消息。</param>
    private void OnMatchMadePush(NotifyMatchMade msg)
    {
        // Localization: Client.Bot.MatchPush - 机器人-{0}收到匹配成功推送 MatchId={1} Mode={2}
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.MatchPush, m_BotName, msg.MatchId, msg.Mode));
        SendMatchEnter(msg.MatchId);
    }

    /// <summary>
    /// 状态轮询响应：已撮合（MatchId≠0）入场；仍在排队则继续有界轮询。
    /// </summary>
    /// <param name="msg">状态响应。</param>
    private void OnMatchStatusResponse(RespMatchStatus msg)
    {
        // Localization: Client.Bot.MatchStatus - 机器人-{0}状态查询 Queuing={1} MatchId={2} ErrorCode={3}
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.MatchStatus, m_BotName, msg.Queuing, msg.MatchId, msg.ErrorCode));
        if (msg.ErrorCode != 0 || msg.MatchId != 0)
        {
            if (msg.MatchId != 0)
            {
                SendMatchEnter(msg.MatchId);
            }

            return;
        }

        ScheduleMatchStatusPoll();
    }

    /// <summary>
    /// 入场响应：断言双方到达同一运行态对局。
    /// </summary>
    /// <param name="msg">入场响应。</param>
    private void OnMatchEnterResponse(RespMatchEnter msg)
    {
        // Localization: Client.Bot.MatchEnter - 机器人-{0}入场响应 MatchId={1} Started={2} ErrorCode={3}
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.MatchEnter, m_BotName, msg.MatchId, msg.Started, msg.ErrorCode));
    }

    /// <summary>
    /// 发送入场请求（幂等守卫：推送与轮询双路径先到者生效）。
    /// </summary>
    /// <param name="matchId">对局标识。</param>
    private void SendMatchEnter(long matchId)
    {
        if (_matchEntered || matchId <= 0)
        {
            return;
        }

        _matchEntered = true;
        SendToTransport(new ReqMatchEnter { MatchId = matchId });
    }

    /// <summary>
    /// 调度下一轮状态轮询（有界后台循环，RPS 轮询同型；到达上限自然退出——真会话 E2E 由 run-seconds 收口）。
    /// </summary>
    private void ScheduleMatchStatusPoll()
    {
        if (_matchEntered)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_matchEntered && _matchStatusPollAttempts < MaxMatchStatusPolls)
                {
                    await Task.Delay(MatchStatusPollIntervalMs);
                    if (_matchEntered)
                    {
                        return;
                    }

                    _matchStatusPollAttempts++;
                    SendToTransport(new ReqMatchStatus());
                }
            }
            catch (Exception e)
            {
                // Localization: Client.Bot.MatchPollError - 机器人-{0}匹配场景-状态轮询异常:{1}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Client.Bot.MatchPollError, m_BotName, e.Message));
            }
        });
    }
}
