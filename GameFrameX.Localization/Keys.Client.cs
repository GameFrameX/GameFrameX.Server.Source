// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Localization;

/// <summary>
/// 本地化键常量定义 - Client 分部类
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// 客户端相关日志消息资源键
    /// </summary>
    public static class Client
    {
        /// <summary>
        /// 尝试连接到服务器...
        /// </summary>
        /// <remarks>
        /// 键名: Client.AttemptingToConnect
        /// 用途: 客户端开始连接到服务器时记录
        /// </remarks>
        public const string AttemptingToConnect = "Client.AttemptingToConnect";

        /// <summary>
        /// 未连接到服务器, 尝试重连 (尝试次数: {0}/{1})
        /// </summary>
        /// <remarks>
        /// 键名: Client.RetryConnect
        /// 用途: 客户端未连接到服务器，准备重试时记录
        /// 参数: {0} - 当前尝试次数, {1} - 最大重试次数
        /// </remarks>
        public const string RetryConnect = "Client.RetryConnect";

        /// <summary>
        /// 重连次数已达到上限，停止尝试。
        /// </summary>
        /// <remarks>
        /// 键名: Client.MaxRetryReached
        /// 用途: 客户端重连次数达到上限时记录
        /// </remarks>
        public const string MaxRetryReached = "Client.MaxRetryReached";

        /// <summary>
        /// 客户端发生错误: {0}
        /// </summary>
        /// <remarks>
        /// 键名: Client.ErrorOccurred
        /// 用途: 客户端操作过程中发生错误时记录
        /// 参数: {0} - 错误信息
        /// </remarks>
        public const string ErrorOccurred = "Client.ErrorOccurred";

        /// <summary>
        /// 客户端断开连接
        /// </summary>
        /// <remarks>
        /// 键名: Client.Disconnected
        /// 用途: 客户端与服务器断开连接时记录
        /// </remarks>
        public const string Disconnected = "Client.Disconnected";

        /// <summary>
        /// 客户端成功连接到服务器
        /// </summary>
        /// <remarks>
        /// 键名: Client.ConnectedSuccessfully
        /// 用途: 客户端成功连接到服务器时记录
        /// </remarks>
        public const string ConnectedSuccessfully = "Client.ConnectedSuccessfully";

        /// <summary>
        /// 机器人 HTTP 客户端相关消息资源键
        /// </summary>
        public static class Bot
        {
            /// <summary>
            /// 发送 POST 数据失败。状态码：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.PostFailed
            /// 用途: 机器人客户端发送 POST 请求失败时抛出
            /// 参数: {0} - HTTP 响应状态码
            /// </remarks>
            public const string PostFailed = "Client.Bot.PostFailed";

            /// <summary>
            /// 断开连接失败: {0}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.DisconnectFailed
            /// 用途: 机器人客户端断开连接发生异常时记录
            /// 参数: {0} - 错误信息
            /// </remarks>
            public const string DisconnectFailed = "Client.Bot.DisconnectFailed";

            /// <summary>
            /// EntryAsync Error: {0}| Thread ID:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.EntryAsyncError
            /// 用途: 机器人入口方法执行发生异常时记录
            /// 参数: {0} - 错误信息, {1} - 托管线程 ID
            /// </remarks>
            public const string EntryAsyncError = "Client.Bot.EntryAsyncError";

            /// <summary>
            /// [METRICS] connect={0} auth={1} list={2} create={3} login={4} total={5}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.Metrics
            /// 用途: 机器人压测各阶段耗时指标输出
            /// 参数: {0} - 连接耗时, {1} - 验证耗时, {2} - 角色列表耗时, {3} - 创建角色耗时, {4} - 登录耗时, {5} - 总耗时
            /// </remarks>
            public const string Metrics = "Client.Bot.Metrics";

            /// <summary>
            /// SendAccountLoginMessage Error: {0}| Thread ID:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.SendAccountLoginError
            /// 用途: 机器人发送账号登录消息发生异常时记录
            /// 参数: {0} - 错误信息, {1} - 托管线程 ID
            /// </remarks>
            public const string SendAccountLoginError = "Client.Bot.SendAccountLoginError";

            /// <summary>
            /// 机器人-{0}账号登录失败，错误码:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.AccountLoginFailed
            /// 用途: 机器人账号登录返回错误码时记录
            /// 参数: {0} - 机器人名称, {1} - 错误码
            /// </remarks>
            public const string AccountLoginFailed = "Client.Bot.AccountLoginFailed";

            /// <summary>
            /// 机器人-{0}账号验证成功,id:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.AccountLoginSuccess
            /// 用途: 机器人账号验证成功时记录
            /// 参数: {0} - 机器人名称, {1} - 账号 ID
            /// </remarks>
            public const string AccountLoginSuccess = "Client.Bot.AccountLoginSuccess";

            /// <summary>
            /// 机器人-{0}请求角色列表失败，错误码:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.PlayerListFailed
            /// 用途: 机器人请求角色列表返回错误码时记录
            /// 参数: {0} - 机器人名称, {1} - 错误码
            /// </remarks>
            public const string PlayerListFailed = "Client.Bot.PlayerListFailed";

            /// <summary>
            /// 机器人-{0}角色列表为空，开始创建角色。
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.PlayerListEmpty
            /// 用途: 机器人角色列表为空转创建角色流程时记录
            /// 参数: {0} - 机器人名称
            /// </remarks>
            public const string PlayerListEmpty = "Client.Bot.PlayerListEmpty";

            /// <summary>
            /// 角色列表 Id:{0}-昵称:{1}-等级:{2}-角色状态:{3}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.PlayerListItem
            /// 用途: 输出角色列表中的单个角色信息
            /// 参数: {0} - 角色 ID, {1} - 昵称, {2} - 等级, {3} - 角色状态
            /// </remarks>
            public const string PlayerListItem = "Client.Bot.PlayerListItem";

            /// <summary>
            /// 机器人-{0}创建角色失败，错误码:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.PlayerCreateFailed
            /// 用途: 机器人创建角色返回错误码时记录
            /// 参数: {0} - 机器人名称, {1} - 错误码
            /// </remarks>
            public const string PlayerCreateFailed = "Client.Bot.PlayerCreateFailed";

            /// <summary>
            /// 创建角色 Id:{0}-昵称:{1}-等级:{2}-角色状态:{3}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.PlayerCreated
            /// 用途: 输出新创建角色的信息
            /// 参数: {0} - 角色 ID, {1} - 昵称, {2} - 等级, {3} - 角色状态
            /// </remarks>
            public const string PlayerCreated = "Client.Bot.PlayerCreated";

            /// <summary>
            /// 机器人-{0}登录成功,id:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.PlayerLoginSuccess
            /// 用途: 机器人角色登录成功时记录
            /// 参数: {0} - 机器人名称, {1} - 角色 ID
            /// </remarks>
            public const string PlayerLoginSuccess = "Client.Bot.PlayerLoginSuccess";

            /// <summary>
            /// 机器人-{0}好友场景启动失败，玩家ID非法:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendScenarioInvalidPlayerId
            /// 用途: 好友场景因玩家 ID 非法启动失败时记录
            /// 参数: {0} - 机器人名称, {1} - 玩家 ID
            /// </remarks>
            public const string FriendScenarioInvalidPlayerId = "Client.Bot.FriendScenarioInvalidPlayerId";

            /// <summary>
            /// 机器人-{0}好友场景启动失败，未找到可用好友目标。
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendScenarioNoTarget
            /// 用途: 好友场景未找到可用好友目标时记录
            /// 参数: {0} - 机器人名称
            /// </remarks>
            public const string FriendScenarioNoTarget = "Client.Bot.FriendScenarioNoTarget";

            /// <summary>
            /// 机器人-{0}开始执行好友场景，目标玩家:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendScenarioStarted
            /// 用途: 好友场景开始执行时记录
            /// 参数: {0} - 机器人名称, {1} - 目标玩家 ID
            /// </remarks>
            public const string FriendScenarioStarted = "Client.Bot.FriendScenarioStarted";

            /// <summary>
            /// 机器人-{0}好友场景-加好友失败，Success:{1}, ErrorCode:{2}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendAddFailed
            /// 用途: 好友场景添加好友失败时记录
            /// 参数: {0} - 机器人名称, {1} - 是否成功, {2} - 错误码
            /// </remarks>
            public const string FriendAddFailed = "Client.Bot.FriendAddFailed";

            /// <summary>
            /// 机器人-{0}好友场景-加好友成功，开始拉取好友列表。
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendAddSuccess
            /// 用途: 好友场景添加好友成功后转拉取列表时记录
            /// 参数: {0} - 机器人名称
            /// </remarks>
            public const string FriendAddSuccess = "Client.Bot.FriendAddSuccess";

            /// <summary>
            /// 机器人-{0}好友场景-删好友失败，Success:{1}, ErrorCode:{2}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendDeleteFailed
            /// 用途: 好友场景删除好友失败时记录
            /// 参数: {0} - 机器人名称, {1} - 是否成功, {2} - 错误码
            /// </remarks>
            public const string FriendDeleteFailed = "Client.Bot.FriendDeleteFailed";

            /// <summary>
            /// 机器人-{0}好友场景-删好友成功，开始二次拉取好友列表。
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendDeleteSuccess
            /// 用途: 好友场景删除好友成功后二次拉取列表时记录
            /// 参数: {0} - 机器人名称
            /// </remarks>
            public const string FriendDeleteSuccess = "Client.Bot.FriendDeleteSuccess";

            /// <summary>
            /// 机器人-{0}好友场景-拉取列表失败，ErrorCode:{1}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendListFailed
            /// 用途: 好友场景拉取好友列表失败时记录
            /// 参数: {0} - 机器人名称, {1} - 错误码
            /// </remarks>
            public const string FriendListFailed = "Client.Bot.FriendListFailed";

            /// <summary>
            /// 机器人-{0}好友场景-首次列表成功，数量:{1}，开始删好友。
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendListFirstSuccess
            /// 用途: 好友场景首次拉取列表成功转删好友时记录
            /// 参数: {0} - 机器人名称, {1} - 好友数量
            /// </remarks>
            public const string FriendListFirstSuccess = "Client.Bot.FriendListFirstSuccess";

            /// <summary>
            /// 机器人-{0}好友场景执行完成，二次列表数量:{1}。
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.FriendScenarioCompleted
            /// 用途: 好友场景执行完成时记录
            /// 参数: {0} - 机器人名称, {1} - 二次列表数量
            /// </remarks>
            public const string FriendScenarioCompleted = "Client.Bot.FriendScenarioCompleted";

            /// <summary>
            /// 机器人-{0}主动断开连接，模拟离线。
            /// </summary>
            /// <remarks>
            /// 键名: Client.Bot.DisconnectSimulated
            /// 用途: 机器人按计划主动断开连接模拟离线时记录
            /// 参数: {0} - 机器人名称
            /// </remarks>
            public const string DisconnectSimulated = "Client.Bot.DisconnectSimulated";

            /// <summary>
            /// 机器人 KCP 客户端相关日志消息资源键
            /// </summary>
            public static class Kcp
            {
                /// <summary>
                /// 连接中状态超时（{0}毫秒），放弃当前会话
                /// </summary>
                /// <remarks>
                /// 键名: Client.Bot.Kcp.ConnectingTimeoutAbandonSession
                /// 用途: KCP 客户端连接中状态超时放弃会话时记录
                /// 参数: {0} - 超时毫秒数
                /// </remarks>
                public const string ConnectingTimeoutAbandonSession = "Client.Bot.Kcp.ConnectingTimeoutAbandonSession";

                /// <summary>
                /// SendToServer failed: {0}
                /// </summary>
                /// <remarks>
                /// 键名: Client.Bot.Kcp.SendToServerFailed
                /// 用途: KCP 客户端发送消息到服务器失败时记录
                /// 参数: {0} - 错误信息
                /// </remarks>
                public const string SendToServerFailed = "Client.Bot.Kcp.SendToServerFailed";

                /// <summary>
                /// KCP connect probe failed: {0}
                /// </summary>
                /// <remarks>
                /// 键名: Client.Bot.Kcp.ConnectProbeFailed
                /// 用途: KCP 连接探测失败时记录
                /// 参数: {0} - 错误信息
                /// </remarks>
                public const string ConnectProbeFailed = "Client.Bot.Kcp.ConnectProbeFailed";

                /// <summary>
                /// KCP receive error: {0}
                /// </summary>
                /// <remarks>
                /// 键名: Client.Bot.Kcp.ReceiveError
                /// 用途: KCP 接收循环发生异常时记录
                /// 参数: {0} - 错误信息
                /// </remarks>
                public const string ReceiveError = "Client.Bot.Kcp.ReceiveError";
            }

            /// <summary>
            /// 机器人 TCP 客户端相关日志消息资源键
            /// </summary>
            public static class Tcp
            {
                /// <summary>
                /// 连接中状态超时（{0}毫秒），重建会话
                /// </summary>
                /// <remarks>
                /// 键名: Client.Bot.Tcp.ConnectingTimeoutRecreateSession
                /// 用途: TCP 客户端连接中状态超时重建会话时记录
                /// 参数: {0} - 超时毫秒数
                /// </remarks>
                public const string ConnectingTimeoutRecreateSession = "Client.Bot.Tcp.ConnectingTimeoutRecreateSession";
            }

            /// <summary>
            /// 机器人 HTTP 客户端相关日志消息资源键
            /// </summary>
            public static class Http
            {
                /// <summary>
                /// 响应消息类型无效。期望“{0}”，实际为“{1}”。
                /// </summary>
                /// <remarks>
                /// 键名: Client.Bot.Http.ResponseTypeInvalid
                /// 用途: HTTP 响应消息类型与期望类型不符时记录
                /// 参数: {0} - 期望类型全名, {1} - 实际类型全名
                /// </remarks>
                public const string ResponseTypeInvalid = "Client.Bot.Http.ResponseTypeInvalid";
            }
        }

        /// <summary>
        /// 客户端入口程序相关日志消息资源键
        /// </summary>
        public static class Program
        {
            /// <summary>
            /// 机器人选项: 机器人数量={0}, tcp={1}:{2}, 场景={3}, 断开循环={4}, 登录后断开秒数={5}, 运行秒数={6}
            /// </summary>
            /// <remarks>
            /// 键名: Client.Program.BotOptions
            /// 用途: 压测机器人启动时输出运行选项
            /// 参数: {0} - 机器人数量, {1} - TCP 主机, {2} - TCP 端口, {3} - 场景, {4} - 是否启用断开循环, {5} - 登录后断开秒数, {6} - 运行秒数
            /// </remarks>
            public const string BotOptions = "Client.Program.BotOptions";
        }
    }
}