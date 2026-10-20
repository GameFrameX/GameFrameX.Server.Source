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
/// 本地化键常量定义 - Launcher 分部类
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// Launcher模块相关日志和错误消息资源键
    /// </summary>
    public static class Launcher
    {
        /// <summary>
        /// 开始启动服务器{0}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ServerStartBegin
        /// 用途: 记录服务器启动开始的信息
        /// 参数: {0} - 服务器类型
        /// </remarks>
        public const string ServerStartBegin = "Launcher.ServerStartBegin";

        /// <summary>
        /// 开始配置Actor限制逻辑...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ActorLimitConfigBegin
        /// 用途: 记录开始配置Actor限制逻辑的日志
        /// </remarks>
        public const string ActorLimitConfigBegin = "Launcher.ActorLimitConfigBegin";

        /// <summary>
        /// 配置Actor限制逻辑结束...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ActorLimitConfigEnd
        /// 用途: 记录配置Actor限制逻辑结束的日志
        /// </remarks>
        public const string ActorLimitConfigEnd = "Launcher.ActorLimitConfigEnd";

        /// <summary>
        /// 开始启动数据库服务...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.DatabaseServiceStartBegin
        /// 用途: 记录开始启动数据库服务的日志
        /// </remarks>
        public const string DatabaseServiceStartBegin = "Launcher.DatabaseServiceStartBegin";

        /// <summary>
        /// 数据库服务启动失败的异常消息
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.DatabaseServiceStartFailed
        /// 用途: 当数据库服务启动失败时抛出异常
        /// </remarks>
        public const string DatabaseServiceStartFailed = "Launcher.DatabaseServiceStartFailed";

        /// <summary>
        /// 启动数据库服务 结束...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.DatabaseServiceStartEnd
        /// 用途: 记录数据库服务启动结束的控制台日志
        /// </remarks>
        public const string DatabaseServiceStartEnd = "Launcher.DatabaseServiceStartEnd";

        /// <summary>
        /// 注册组件开始...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ComponentRegisterBegin
        /// 用途: 记录组件注册开始的日志
        /// </remarks>
        public const string ComponentRegisterBegin = "Launcher.ComponentRegisterBegin";

        /// <summary>
        /// 注册组件结束...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ComponentRegisterEnd
        /// 用途: 记录组件注册结束的日志
        /// </remarks>
        public const string ComponentRegisterEnd = "Launcher.ComponentRegisterEnd";

        /// <summary>
        /// 开始加载热更新模块...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.HotfixModuleLoadBegin
        /// 用途: 记录开始加载热更新模块的日志
        /// </remarks>
        public const string HotfixModuleLoadBegin = "Launcher.HotfixModuleLoadBegin";

        /// <summary>
        /// 加载热更新模块结束...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.HotfixModuleLoadEnd
        /// 用途: 记录热更新模块加载结束的日志
        /// </remarks>
        public const string HotfixModuleLoadEnd = "Launcher.HotfixModuleLoadEnd";

        /// <summary>
        /// 开始装配 Online 平台管理面（admin API，端口 {0}）...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineAdminStartBegin
        /// 用途: 记录开始装配 Online 管理面的日志
        /// 参数: {0} - admin API 监听端口
        /// </remarks>
        public const string OnlineAdminStartBegin = "Launcher.OnlineAdminStartBegin";

        /// <summary>
        /// Online 平台管理面装配完成（租户 {0}，应用 {1}，区服 {2}）...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineAdminStartEnd
        /// 用途: 记录 Online 管理面装配完成的日志
        /// 参数: {0} - 租户标识；{1} - 应用标识；{2} - 区服标识
        /// </remarks>
        public const string OnlineAdminStartEnd = "Launcher.OnlineAdminStartEnd";

        /// <summary>
        /// Online 好友查询桥已发布（租户 {0}，应用 {1}，区服 {2}）...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineFriendBridgePublished
        /// 用途: 记录好友查询桥租约发布（C243 组合根接线）
        /// 参数: {0} - 租户标识；{1} - 应用标识；{2} - 区服标识
        /// </remarks>
        public const string OnlineFriendBridgePublished = "Launcher.OnlineFriendBridgePublished";

        /// <summary>
        /// Online 好友查询桥已撤销...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineFriendBridgeRetracted
        /// 用途: 记录好友查询桥租约撤销（进程停止路径）
        /// </remarks>
        public const string OnlineFriendBridgeRetracted = "Launcher.OnlineFriendBridgeRetracted";

        /// <summary>
        /// Online 好友查询桥验证种子已建立（仅供部署验证）：请求主体 {0}，种子好友数 {1}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineFriendBridgeSeeded
        /// 用途: 披露验证种子经真实服务写路径建立的好友关系（C243）
        /// 参数: {0} - 请求主体（玩家全局 actor）标识；{1} - 建立成功的好友数
        /// </remarks>
        public const string OnlineFriendBridgeSeeded = "Launcher.OnlineFriendBridgeSeeded";

        /// <summary>
        /// Online 好友域桥契约面缺失（online-social 能力被排除或未装配）——好友消息将显式失败
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineFriendBridgeFaceMissing
        /// 用途: C244 好友域接管：发布期无法解析好友域契约面或授权作用域（Error 披露，不终止启动）
        /// </remarks>
        public const string OnlineFriendBridgeFaceMissing = "Launcher.OnlineFriendBridgeFaceMissing";

        /// <summary>
        /// 旧好友关系迁移单条失败：{0} &lt;-&gt; {1}, Code: {2}, Message: {3}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineFriendLegacyMigrationEntryFailed
        /// 用途: C244 旧好友数据迁移单条收敛失败时逐条披露（containment，不中断整体迁移）
        /// 参数: {0} - 一侧玩家ID；{1} - 另一侧玩家ID；{2} - 协议错误码；{3} - 失败描述
        /// </remarks>
        public const string OnlineFriendLegacyMigrationEntryFailed = "Launcher.OnlineFriendLegacyMigrationEntryFailed";

        /// <summary>
        /// 旧好友数据迁移完成：已建立 {0}，待答申请 {1}，失败 {2}（旧数据零改动）
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineFriendLegacyMigrationDone
        /// 用途: C244 旧好友数据一次性迁移的汇总披露
        /// 参数: {0} - 已建立关系数；{1} - 待答申请数；{2} - 失败数
        /// </remarks>
        public const string OnlineFriendLegacyMigrationDone = "Launcher.OnlineFriendLegacyMigrationDone";

        /// <summary>
        /// Online 对局桥已发布（租户 {0}，应用 {1}，区服 {2}）...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchBridgePublished
        /// 用途: 记录对局桥租约发布（C245 组合根接线）
        /// 参数: {0} - 租户标识；{1} - 应用标识；{2} - 区服标识
        /// </remarks>
        public const string OnlineMatchBridgePublished = "Launcher.OnlineMatchBridgePublished";

        /// <summary>
        /// Online 对局桥已撤销...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchBridgeRetracted
        /// 用途: 记录对局桥租约撤销（进程停止路径）
        /// </remarks>
        public const string OnlineMatchBridgeRetracted = "Launcher.OnlineMatchBridgeRetracted";

        /// <summary>
        /// Online 对局桥榜单已确保存在（仅供样例验证）：{0}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchBridgeBoardEnsured
        /// 用途: 披露组合根经真实榜单服务确保写榜目标存在（C245）；已存在时幂等跳过
        /// 参数: {0} - 榜单标识
        /// </remarks>
        public const string OnlineMatchBridgeBoardEnsured = "Launcher.OnlineMatchBridgeBoardEnsured";

        /// <summary>
        /// 对局桥开关开启但业务运行时未启用（IsEnableOnline=false）——不支持该拓扑，启动终止
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchBridgeRuntimeGateMissing
        /// 用途: C245 组合根 fail-fast：桥开关与业务运行时开关矛盾
        /// </remarks>
        public const string OnlineMatchBridgeRuntimeGateMissing = "Launcher.OnlineMatchBridgeRuntimeGateMissing";

        /// <summary>
        /// 对局桥开关开启但作用域三元组缺失（OnlineTenantId/OnlineAppId/ServerId）——启动终止
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchBridgeScopeMissing
        /// 用途: C245 组合根 fail-fast：桥开关开启但授权作用域不完整
        /// </remarks>
        public const string OnlineMatchBridgeScopeMissing = "Launcher.OnlineMatchBridgeScopeMissing";

        /// <summary>
        /// 对局桥契约面缺失（online-match 能力被排除或未装配）——不支持该拓扑，启动终止
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchBridgeFaceMissing
        /// 用途: C245 组合根 fail-fast：发布期无法解析对局桥契约面或授权作用域
        /// </remarks>
        public const string OnlineMatchBridgeFaceMissing = "Launcher.OnlineMatchBridgeFaceMissing";

        /// <summary>
        /// 对局桥榜单确保失败：{0}，代码：{1}，消息：{2}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchBridgeBoardEnsureFailed
        /// 用途: C245 组合根：写榜目标榜单确保失败（配置矛盾或榜单服务不可用），启动终止
        /// 参数: {0} - 榜单标识；{1} - 错误码；{2} - 错误消息
        /// </remarks>
        public const string OnlineMatchBridgeBoardEnsureFailed = "Launcher.OnlineMatchBridgeBoardEnsureFailed";

        /// <summary>
        /// 匹配域桥契约面不可解析（online-match/matchmaking 未装配或被排除），420 匹配消息将显式失败
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchDomainBridgeFaceMissing
        /// 用途: C246 组合根：发布期无法解析匹配域契约面或授权作用域，Error 披露不抛出
        /// </remarks>
        public const string OnlineMatchDomainBridgeFaceMissing = "Launcher.OnlineMatchDomainBridgeFaceMissing";

        /// <summary>
        /// Online 匹配域桥已发布（租户 {0}，应用 {1}，区服 {2}）
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchDomainBridgePublished
        /// 用途: C246 组合根：匹配域桥租约发布确认
        /// 参数: {0} - 租户标识；{1} - 应用标识；{2} - 区服标识
        /// </remarks>
        public const string OnlineMatchDomainBridgePublished = "Launcher.OnlineMatchDomainBridgePublished";

        /// <summary>
        /// Online 匹配成功通知未装配（事件总线或契约面不可解析），等待方经状态查询兜底
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchDomainBridgeNotifyMissing
        /// 用途: C246 组合根：匹配成功通知订阅装配缺席披露
        /// </remarks>
        public const string OnlineMatchDomainBridgeNotifyMissing = "Launcher.OnlineMatchDomainBridgeNotifyMissing";

        /// <summary>
        /// Online 匹配域桥已撤销
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineMatchDomainBridgeRetracted
        /// 用途: C246 组合根：匹配域桥租约撤销确认
        /// </remarks>
        public const string OnlineMatchDomainBridgeRetracted = "Launcher.OnlineMatchDomainBridgeRetracted";

        /// <summary>
        /// Online 会话域桥已发布（租户 {0}，应用 {1}，区服 {2}）
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineSessionDomainBridgePublished
        /// 用途: C204 组合根：会话域桥租约发布确认
        /// 参数: {0} - 租户标识；{1} - 应用标识；{2} - 区服标识
        /// </remarks>
        public const string OnlineSessionDomainBridgePublished = "Launcher.OnlineSessionDomainBridgePublished";

        /// <summary>
        /// Online 会话域桥契约面不可解析（online-foundation 未装配或被排除），会话恢复不可用
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineSessionDomainBridgeFaceMissing
        /// 用途: C204 组合根：会话域桥契约面缺席披露（登录不下发凭据、恢复请求显式失败）
        /// </remarks>
        public const string OnlineSessionDomainBridgeFaceMissing = "Launcher.OnlineSessionDomainBridgeFaceMissing";

        /// <summary>
        /// Online 会话域桥已撤销
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.OnlineSessionDomainBridgeRetracted
        /// 用途: C204 组合根：会话域桥租约撤销确认
        /// </remarks>
        public const string OnlineSessionDomainBridgeRetracted = "Launcher.OnlineSessionDomainBridgeRetracted";


        /// <summary>
        /// 进入游戏主循环...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.EnterMainLoop
        /// 用途: 记录进入游戏主循环的日志
        /// </remarks>
        public const string EnterMainLoop = "Launcher.EnterMainLoop";

        /// <summary>
        /// 服务器{0}启动结束...
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ServerStartEnd
        /// 用途: 记录服务器启动结束的信息
        /// 参数: {0} - 服务器类型
        /// </remarks>
        public const string ServerStartEnd = "Launcher.ServerStartEnd";

        /// <summary>
        /// 服务器执行异常，e:{0}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ServerExecutionException
        /// 用途: 记录服务器执行异常的信息
        /// 参数: {0} - 异常详情
        /// </remarks>
        public const string ServerExecutionException = "Launcher.ServerExecutionException";

        /// <summary>
        /// 退出服务器开始
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ServerExitBegin
        /// 用途: 记录服务器开始退出的日志
        /// </remarks>
        public const string ServerExitBegin = "Launcher.ServerExitBegin";

        /// <summary>
        /// 退出服务器成功
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ServerExitSuccess
        /// 用途: 记录服务器退出成功的日志
        /// </remarks>
        public const string ServerExitSuccess = "Launcher.ServerExitSuccess";

        /// <summary>
        /// ---发送[{0} To {1}]  {2}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.SendMessage
        /// 用途: 记录发送消息的调试信息
        /// 参数: {0} - 发送方服务器类型, {1} - 接收方服务器类型, {2} - 消息内容
        /// </remarks>
        public const string SendMessage = "Launcher.SendMessage";

        /// <summary>
        /// ---收到[{0} To {1}]  {2}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ReceiveMessage
        /// 用途: 记录接收消息的调试信息
        /// 参数: {0} - 发送方服务器类型, {1} - 接收方服务器类型, {2} - 消息内容
        /// </remarks>
        public const string ReceiveMessage = "Launcher.ReceiveMessage";

        /// <summary>
        /// 注册玩家成功：{0}  {1}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.PlayerRegisterSuccess
        /// 用途: 记录玩家注册成功的日志
        /// 参数: {0} - 玩家ID, {1} - 玩家信息
        /// </remarks>
        public const string PlayerRegisterSuccess = "Launcher.PlayerRegisterSuccess";

        /// <summary>
        /// 注销玩家成功：{0}  {1}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.PlayerUnregisterSuccess
        /// 用途: 记录玩家注销成功的日志
        /// 参数: {0} - 玩家ID, {1} - 玩家信息
        /// </remarks>
        public const string PlayerUnregisterSuccess = "Launcher.PlayerUnregisterSuccess";

        /// <summary>
        /// 注册服务成功：{0}  {1}  {2}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ServiceRegisterSuccess
        /// 用途: 记录服务注册成功的日志
        /// 参数: {0} - 服务器类型, {1} - 服务器名称, {2} - 注册信息
        /// </remarks>
        public const string ServiceRegisterSuccess = "Launcher.ServiceRegisterSuccess";

        /// <summary>
        /// 注销服务成功：{0}  {1}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ServiceUnregisterSuccess
        /// 用途: 记录服务注销成功的日志
        /// 参数: {0} - 服务器ID, {1} - 服务器实例ID
        /// </remarks>
        public const string ServiceUnregisterSuccess = "Launcher.ServiceUnregisterSuccess";

        /// <summary>
        /// 有外部服务连接到中心服务器成功。链接信息：SessionID:{0} RemoteEndPoint:{1}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ExternalServiceConnected
        /// 用途: 记录外部服务连接到中心服务器成功的日志
        /// 参数: {0} - SessionID, {1} - RemoteEndPoint
        /// </remarks>
        public const string ExternalServiceConnected = "Launcher.ExternalServiceConnected";

        /// <summary>
        /// 有外部服务从中心服务器断开。链接信息：断开原因:{0}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ExternalServiceDisconnected
        /// 用途: 记录外部服务从中心服务器断开的日志
        /// 参数: {0} - 断开原因
        /// </remarks>
        public const string ExternalServiceDisconnected = "Launcher.ExternalServiceDisconnected";

        /// <summary>
        /// 玩家下线回调，通知其他服务器: {0} / Player offline callback, notify other servers: {0}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.PlayerOfflineCallback
        /// 用途: 记录玩家下线时的调试信息
        /// 参数: {0} - 玩家信息的JSON序列化结果
        /// </remarks>
        public const string PlayerOfflineCallback = "Launcher.PlayerOfflineCallback";

        /// <summary>
        /// 玩家上线回调，通知其他服务器: {0} / Player online callback, notify other servers: {0}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.PlayerOnlineCallback
        /// 用途: 记录玩家上线时的调试信息
        /// 参数: {0} - 玩家信息的JSON序列化结果
        /// </remarks>
        public const string PlayerOnlineCallback = "Launcher.PlayerOnlineCallback";

        /// <summary>
        /// 服务下线回调，通知其他服务器: {0} / Service offline callback, notify other servers: {0}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ServiceOfflineCallback
        /// 用途: 记录服务下线时的调试信息
        /// 参数: {0} - 服务信息
        /// </remarks>
        public const string ServiceOfflineCallback = "Launcher.ServiceOfflineCallback";

        /// <summary>
        /// 服务上线回调，通知其他服务器: {0} / Service online callback, notify other servers: {0}
        /// </summary>
        /// <remarks>
        /// 键名: Launcher.ServiceOnlineCallback
        /// 用途: 记录服务上线时的调试信息
        /// 参数: {0} - 服务信息
        /// </remarks>
        public const string ServiceOnlineCallback = "Launcher.ServiceOnlineCallback";

    }
}
