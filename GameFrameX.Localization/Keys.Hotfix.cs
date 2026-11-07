// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc/alianblank.com/
//   Official Documentation: https://gameframex.doc/alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Localization;

/// <summary>
/// Hotfix 模块本地化消息键。
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// Hotfix 模块消息键。
    /// </summary>
    public static class Hotfix
    {
        /// <summary>
        /// Campaign 参数非法，code={0}
        /// </summary>
        /// <remarks>
        /// 键名: Hotfix.MailCampaign.InvalidParameter
        /// 用途: 发布 / 预览 Campaign 前参数校验失败时抛出。
        /// 参数: {0} - 校验错误码（MailCampaignErrorCode）
        /// </remarks>
        public const string MailCampaignInvalidParameter = "Hotfix.MailCampaign.InvalidParameter";

        /// <summary>
        /// Campaign 已发布或已撤回，主体字段不可修改（B1）。CampaignId={0}
        /// </summary>
        /// <remarks>
        /// 键名: Hotfix.MailCampaign.PublishedOrRevokedImmutable
        /// 用途: 已发布或已撤回的 Campaign 再次提交发布时抛出（B1 不可逆边界）。
        /// 参数: {0} - CampaignId
        /// </remarks>
        public const string MailCampaignPublishedOrRevokedImmutable = "Hotfix.MailCampaign.PublishedOrRevokedImmutable";

        /// <summary>
        /// 登录消息类型 {0} 缺少 {1}。
        /// </summary>
        /// <remarks>
        /// 键名: Hotfix.Startup.LoginMessageTypeMissingAttribute
        /// 用途: 启动时读取登录消息码，消息类型缺少 MessageTypeHandlerAttribute 特性时抛出。
        /// 参数: {0} - 消息类型全名；{1} - 缺少的特性名
        /// </remarks>
        public const string LoginMessageTypeMissingAttribute = "Hotfix.Startup.LoginMessageTypeMissingAttribute";

        /// <summary>
        /// StartUp 启动流程消息键。
        /// </summary>
        public static class StartUpModule
        {
            /// <summary>
            /// 客户端断开连接 - 会话ID: {0}, 断开原因: {1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.StartUp.ClientDisconnected
            /// 用途: 主服务器会话断开回调记录断开会话ID与原因。
            /// 参数: {0} - 会话ID, {1} - 断开原因
            /// </remarks>
            public const string ClientDisconnected = "Hotfix.StartUp.ClientDisconnected";

            /// <summary>
            /// 数据包接收: 找不到消息处理器, 消息ID: {0}, 消息类型: {1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.StartUp.MessageHandlerNotFound
            /// 用途: 按消息ID查找消息处理器失败时记录。
            /// 参数: {0} - 消息ID, {1} - 消息类型
            /// </remarks>
            public const string MessageHandlerNotFound = "Hotfix.StartUp.MessageHandlerNotFound";

        }

        /// <summary>
        /// Server 服务器组件消息键。
        /// </summary>
        public static class Server
        {
            /// <summary>
            /// ServerCompAgent.TestDelayTimer.延时3秒执行.执行一次
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Server.TestDelayTimerExecuted
            /// 用途: 延时定时器测试执行的调试日志。
            /// </remarks>
            public const string TestDelayTimerExecuted = "Hotfix.Server.TestDelayTimerExecuted";

            /// <summary>
            /// ServerCompAgent.TestSchedueTimer.延时1秒执行.每隔10秒执行
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Server.TestScheduleTimerExecuted
            /// 用途: 周期定时器测试执行的调试日志。
            /// </remarks>
            public const string TestScheduleTimerExecuted = "Hotfix.Server.TestScheduleTimerExecuted";

            /// <summary>
            /// ServerCompAgent.CrossDayTimeHandler.跨天定时器执行{0}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Server.CrossDayTimerExecuted
            /// 用途: 跨天定时器触发时记录当前UTC时间。
            /// 参数: {0} - 当前UTC时间字符串
            /// </remarks>
            public const string CrossDayTimerExecuted = "Hotfix.Server.CrossDayTimerExecuted";
        }

        /// <summary>
        /// Friend 好友模块消息键。
        /// </summary>
        public static class Friend
        {
            /// <summary>
            /// 好友域桥不可用（未发布或已撤销）, PlayerId: {0}, Code: {1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.DomainBridgeUnavailable
            /// 用途: 好友域桥租约不可用（宿主未启/已停/已撤销）时记录。
            /// 参数: {0} - 请求主体ID, {1} - 协议错误码
            /// </remarks>
            public const string DomainBridgeUnavailable = "Hotfix.Friend.DomainBridgeUnavailable";

            /// <summary>
            /// 好友域桥返回失败, Code: {0}, Message: {1}, PlayerId: {2}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.DomainBridgeFailed
            /// 用途: 好友域桥契约面返回失败结果时记录。
            /// 参数: {0} - 协议错误码, {1} - 失败描述, {2} - 请求主体ID
            /// </remarks>
            public const string DomainBridgeFailed = "Hotfix.Friend.DomainBridgeFailed";

            /// <summary>
            /// 好友域桥调用异常, PlayerId: {0}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.DomainBridgeException
            /// 用途: 好友域桥调用抛出异常时记录。
            /// 参数: {0} - 请求主体ID
            /// </remarks>
            public const string DomainBridgeException = "Hotfix.Friend.DomainBridgeException";
        }

        /// <summary>
        /// Match 匹配模块消息键。
        /// </summary>
        public static class Match
        {
            /// <summary>
            /// 匹配域桥不可用（未发布或已撤销）, PlayerId: {0}, Code: {1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Match.DomainBridgeUnavailable
            /// 用途: 匹配域桥租约不可用（宿主未启/已停/已撤销）时记录。
            /// 参数: {0} - 请求主体ID, {1} - 协议错误码
            /// </remarks>
            public const string DomainBridgeUnavailable = "Hotfix.Match.DomainBridgeUnavailable";

            /// <summary>
            /// 匹配域桥返回失败, Code: {0}, Message: {1}, PlayerId: {2}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Match.DomainBridgeFailed
            /// 用途: 匹配域桥契约面返回失败结果时记录。
            /// 参数: {0} - 协议错误码, {1} - 失败描述, {2} - 请求主体ID
            /// </remarks>
            public const string DomainBridgeFailed = "Hotfix.Match.DomainBridgeFailed";

            /// <summary>
            /// 匹配域桥调用异常, PlayerId: {0}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Match.DomainBridgeException
            /// 用途: 匹配域桥调用抛出异常时记录。
            /// 参数: {0} - 请求主体ID
            /// </remarks>
            public const string DomainBridgeException = "Hotfix.Match.DomainBridgeException";
        }

        /// <summary>
        /// SessionResume 会话恢复模块消息键。
        /// </summary>
        public static class SessionResume
        {
            /// <summary>
            /// 会话域桥不可用（未发布或已撤销）, PlayerId: {0}, Code: {1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.SessionResume.BridgeUnavailable
            /// 用途: 会话域桥租约不可用（宿主未启/已停/已撤销）时记录。
            /// 参数: {0} - 请求主体ID, {1} - 协议错误码
            /// </remarks>
            public const string BridgeUnavailable = "Hotfix.SessionResume.BridgeUnavailable";

            /// <summary>
            /// 登录签发会话恢复凭据失败, Code: {0}, PlayerId: {1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.SessionResume.IssueFailed
            /// 用途: 登录时经会话域桥签发恢复凭据返回失败时记录（登录主流程不受影响）。
            /// 参数: {0} - 协议错误码, {1} - 玩家ID
            /// </remarks>
            public const string IssueFailed = "Hotfix.SessionResume.IssueFailed";

            /// <summary>
            /// 会话恢复请求, 会话ID: {0}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.SessionResume.ResumeRequested
            /// 用途: 收到新连接会话恢复请求时记录。
            /// 参数: {0} - 会话ID
            /// </remarks>
            public const string ResumeRequested = "Hotfix.SessionResume.ResumeRequested";

            /// <summary>
            /// 会话恢复成功, PlayerId: {0}, 新会话ID: {1}, 对局恢复: {2}, MatchId: {3}, ServerSequence: {4}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.SessionResume.ResumeSucceeded
            /// 用途: 会话恢复（身份 + 对局）成功时记录。
            /// 参数: {0} - 玩家ID, {1} - 新会话ID, {2} - 是否恢复对局, {3} - 对局标识, {4} - 服务器序号
            /// </remarks>
            public const string ResumeSucceeded = "Hotfix.SessionResume.ResumeSucceeded";

            /// <summary>
            /// 会话恢复失败, Code: {0}, Message: {1}, 会话ID: {2}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.SessionResume.ResumeFailed
            /// 用途: 会话恢复显式失败（凭据无效/过期/错主体等）时记录。
            /// 参数: {0} - 协议错误码, {1} - 失败描述, {2} - 会话ID
            /// </remarks>
            public const string ResumeFailed = "Hotfix.SessionResume.ResumeFailed";

            /// <summary>
            /// 断线两层协同, PlayerId: {0}, 会话域结果: {1}, 对局域结果: {2}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.SessionResume.DisconnectCoordination
            /// 用途: 物理断线后 Online 会话域 / 对局域协同通知结果时记录。
            /// 参数: {0} - 玩家ID, {1} - 会话域通知结果, {2} - 对局域通知结果
            /// </remarks>
            public const string DisconnectCoordination = "Hotfix.SessionResume.DisconnectCoordination";

            /// <summary>
            /// 断线跳过协同（旧回调迟到，玩家已有新会话）, PlayerId: {0}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.SessionResume.DisconnectStaleSkipped
            /// 用途: 旧连接断线回调迟到且玩家已持新会话时跳过 Online 侧开窗记录。
            /// 参数: {0} - 玩家ID
            /// </remarks>
            public const string DisconnectStaleSkipped = "Hotfix.SessionResume.DisconnectStaleSkipped";
        }

        /// <summary>
        /// Game 游戏模块消息键。
        /// </summary>
        public static class Game
        {
            /// <summary>
            /// 对局桥不可用（未发布或已撤销）, ActorId: {0}, Code: {1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Game.MatchBridgeUnavailable
            /// 用途: 对局桥开关开启但租约不可用（宿主未启/已停/已撤销）时记录。
            /// 参数: {0} - 请求主体ID, {1} - 协议错误码
            /// </remarks>
            public const string MatchBridgeUnavailable = "Hotfix.Game.MatchBridgeUnavailable";

            /// <summary>
            /// 对局桥返回失败, Code: {0}, Message: {1}, ActorId: {2}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Game.MatchBridgeFailed
            /// 用途: 对局桥契约面返回失败结果时记录。
            /// 参数: {0} - 协议错误码, {1} - 失败描述, {2} - 请求主体ID
            /// </remarks>
            public const string MatchBridgeFailed = "Hotfix.Game.MatchBridgeFailed";

            /// <summary>
            /// 对局桥调用异常, ActorId: {0}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Game.MatchBridgeException
            /// 用途: 对局桥调用抛出异常时记录。
            /// 参数: {0} - 请求主体ID
            /// </remarks>
            public const string MatchBridgeException = "Hotfix.Game.MatchBridgeException";
        }

        /// <summary>
        /// Player 玩家模块消息键。
        /// </summary>
        public static class Player
        {
            /// <summary>
            /// 代理对象为空
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Player.AgentIsNull
            /// 用途: 事件监听器收到空代理时记录错误。
            /// </remarks>
            public const string AgentIsNull = "Hotfix.Player.AgentIsNull";
        }

        /// <summary>
        /// Bag 背包模块消息键。
        /// </summary>
        public static class Bag
        {
            /// <summary>
            /// ReqPlayerSendItemHttpHandler 离线通知发送失败, playerId: {0}, status: {1}, error: {2}, traceId: {3}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Bag.OfflineNotifySendFailed
            /// 用途: 发放道具后离线通知发送失败（非离线原因）时记录。
            /// 参数: {0} - 玩家ID, {1} - 投递状态, {2} - 错误信息, {3} - 追踪ID
            /// </remarks>
            public const string OfflineNotifySendFailed = "Hotfix.Bag.OfflineNotifySendFailed";
        }

        /// <summary>
        /// Unified 统一消息投递消息键。
        /// </summary>
        public static class Unified
        {
            /// <summary>
            /// SendToPlayerInnerHandler 投递失败, PlayerId:{0}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Unified.DeliveryFailed
            /// 用途: 向玩家会话投递内部消息抛出异常时记录。
            /// 参数: {0} - 目标玩家ID
            /// </remarks>
            public const string DeliveryFailed = "Hotfix.Unified.DeliveryFailed";
        }
    }
}
