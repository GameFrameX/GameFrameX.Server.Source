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
        public static class StartUp
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
            /// 数据包接收心跳: {0}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.StartUp.DataPackageReceiveHeartBeat
            /// 用途: 调试开关开启时记录收到的心跳消息内容。
            /// 参数: {0} - 心跳消息格式化内容
            /// </remarks>
            public const string DataPackageReceiveHeartBeat = "Hotfix.StartUp.DataPackageReceiveHeartBeat";

            /// <summary>
            /// 数据包接收: {0}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.StartUp.DataPackageReceive
            /// 用途: 调试开关开启时记录收到的网络消息内容。
            /// 参数: {0} - 消息格式化内容
            /// </remarks>
            public const string DataPackageReceive = "Hotfix.StartUp.DataPackageReceive";

            /// <summary>
            /// 数据包接收: 找不到消息处理器, 消息ID: {0}, 消息类型: {1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.StartUp.MessageHandlerNotFound
            /// 用途: 按消息ID查找TCP处理器失败时记录。
            /// 参数: {0} - 消息ID, {1} - 消息类型
            /// </remarks>
            public const string MessageHandlerNotFound = "Hotfix.StartUp.MessageHandlerNotFound";

            /// <summary>
            /// 数据包接收: 调用消息处理器出错, 消息ID: {0}, 消息类型: {1}, 异常: {2}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.StartUp.MessageHandlerInvokeError
            /// 用途: 消息处理器调用抛出异常时记录。
            /// 参数: {0} - 消息ID, {1} - 消息类型, {2} - 异常信息
            /// </remarks>
            public const string MessageHandlerInvokeError = "Hotfix.StartUp.MessageHandlerInvokeError";
        }

        /// <summary>
        /// Event 事件分发消息键。
        /// </summary>
        public static class Event
        {
            /// <summary>
            /// EventDispatcherExtensions.SelfHandle {0} 事件类型：{1} 没有找到任何监听者
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Event.SelfHandleNoListeners
            /// 用途: actor 自处理事件时未找到任何监听者的警告（收编原 Events.NoListenersFound 混合外壳）。
            /// 参数: {0} - 事件参数类型名, {1} - 事件参数类型名（监听者查找）
            /// </remarks>
            public const string SelfHandleNoListeners = "Hotfix.Event.SelfHandleNoListeners";

            /// <summary>
            /// EventDispatcherExtensions.SelfHandle 处理事件 {0} 时发生异常: {1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Event.SelfHandleException
            /// 用途: actor 自处理事件时监听器执行抛出异常的记录。
            /// 参数: {0} - 事件参数类型名, {1} - 异常信息
            /// </remarks>
            public const string SelfHandleException = "Hotfix.Event.SelfHandleException";
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
            /// FriendComponentAgent.OnFriendList 统一消息发送失败, StatusCode: {0}, Error: {1}, TraceId: {2}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.FriendListSendFailed
            /// 用途: 好友列表统一消息发送失败时记录。
            /// 参数: {0} - 状态码, {1} - 错误信息, {2} - 追踪ID
            /// </remarks>
            public const string FriendListSendFailed = "Hotfix.Friend.FriendListSendFailed";

            /// <summary>
            /// FriendComponentAgent.OnFriendList 统一消息发送异常
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.FriendListSendException
            /// 用途: 好友列表统一消息发送抛出异常时记录。
            /// </remarks>
            public const string FriendListSendException = "Hotfix.Friend.FriendListSendException";

            /// <summary>
            /// FriendComponentAgent.OnAddFriend 统一消息发送失败, StatusCode: {0}, Error: {1}, TraceId: {2}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.AddFriendSendFailed
            /// 用途: 添加好友统一消息发送失败时记录。
            /// 参数: {0} - 状态码, {1} - 错误信息, {2} - 追踪ID
            /// </remarks>
            public const string AddFriendSendFailed = "Hotfix.Friend.AddFriendSendFailed";

            /// <summary>
            /// FriendComponentAgent.OnAddFriend 统一消息发送异常
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.AddFriendSendException
            /// 用途: 添加好友统一消息发送抛出异常时记录。
            /// </remarks>
            public const string AddFriendSendException = "Hotfix.Friend.AddFriendSendException";

            /// <summary>
            /// FriendComponentAgent.OnDeleteFriend 统一消息发送失败, StatusCode: {0}, Error: {1}, TraceId: {2}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.DeleteFriendSendFailed
            /// 用途: 删除好友统一消息发送失败时记录。
            /// 参数: {0} - 状态码, {1} - 错误信息, {2} - 追踪ID
            /// </remarks>
            public const string DeleteFriendSendFailed = "Hotfix.Friend.DeleteFriendSendFailed";

            /// <summary>
            /// FriendComponentAgent.OnDeleteFriend 统一消息发送异常
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.DeleteFriendSendException
            /// 用途: 删除好友统一消息发送抛出异常时记录。
            /// </remarks>
            public const string DeleteFriendSendException = "Hotfix.Friend.DeleteFriendSendException";

            /// <summary>
            /// FriendComponentAgent.{0} 返回语义异常: Success=false 且 ErrorCode=0, PlayerId={1}
            /// </summary>
            /// <remarks>
            /// 键名: Hotfix.Friend.SemanticInvalidResult
            /// 用途: 好友操作返回 Success=false 且 ErrorCode=0 的语义异常时记录。
            /// 参数: {0} - 操作名, {1} - 玩家ID
            /// </remarks>
            public const string SemanticInvalidResult = "Hotfix.Friend.SemanticInvalidResult";
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
