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
/// 本地化键常量定义 - Network 分部类
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// Network模块相关日志和错误消息资源键
    /// </summary>
    public static class Network
    {
        /// <summary>
        /// ---发送{0}
        /// </summary>
        /// <remarks>
        /// 键名: Network.MessageSent
        /// 用途: 记录发送消息的日志信息
        /// 参数: {0} - 格式化的消息内容
        /// </remarks>
        public const string MessageSent = "Network.MessageSent";

        /// <summary>
        /// 消息发送超时被取消:{0}
        /// </summary>
        /// <remarks>
        /// 键名: Network.MessageSendTimeout
        /// 用途: 当消息发送超时被取消时记录错误
        /// 参数: {0} - 异常消息
        /// </remarks>
        public const string MessageSendTimeout = "Network.MessageSendTimeout";

        /// <summary>
        /// Type {0} must implement {1} interface / 类型 {0} 必须实现 {1} 接口
        /// </summary>
        /// <remarks>
        /// 键名: Network.TypeMustImplementInterface
        /// 用途: 当类型未实现指定接口时抛出异常
        /// 参数: {0} - 类型名称, {1} - 接口名称
        /// </remarks>
        public const string TypeMustImplementInterface = "Network.TypeMustImplementInterface";

        /// <summary>
        /// Type {0} must be a class / 类型 {0} 必须是类
        /// </summary>
        /// <remarks>
        /// 键名: Network.TypeMustBeClass
        /// 用途: 当类型不是类时抛出异常
        /// 参数: {0} - 类型名称
        /// </remarks>
        public const string TypeMustBeClass = "Network.TypeMustBeClass";

        /// <summary>
        /// Type {0} must have a parameterless constructor / 类型 {0} 必须有无参构造函数
        /// </summary>
        /// <remarks>
        /// 键名: Network.TypeMustHaveParameterlessConstructor
        /// 用途: 当类型没有无参构造函数时抛出异常
        /// 参数: {0} - 类型名称
        /// </remarks>
        public const string TypeMustHaveParameterlessConstructor = "Network.TypeMustHaveParameterlessConstructor";

        /// <summary>
        /// Cannot find Create method on ObjectPoolProvider / 无法在 ObjectPoolProvider 上找到 Create 方法
        /// </summary>
        /// <remarks>
        /// 键名: Network.CannotFindCreateMethod
        /// 用途: 当无法在ObjectPoolProvider上找到Create方法时抛出异常
        /// </remarks>
        public const string CannotFindCreateMethod = "Network.CannotFindCreateMethod";

        /// <summary>
        /// Failed to create object pool for type {0} / 无法为类型 {0} 创建对象池
        /// </summary>
        /// <remarks>
        /// 键名: Network.FailedToCreateObjectPool
        /// 用途: 当无法为类型创建对象池时抛出异常
        /// 参数: {0} - 类型名称
        /// </remarks>
        public const string FailedToCreateObjectPool = "Network.FailedToCreateObjectPool";

        /// <summary>
        /// RPC call timeout! Message is: {0} / RPC调用超时！消息为：{0}
        /// </summary>
        /// <remarks>
        /// 键名: Network.RpcCallTimeout
        /// 用途: 当RPC调用超时时记录错误
        /// 参数: {0} - 消息内容
        /// </remarks>
        public const string RpcCallTimeout = "Network.RpcCallTimeout";

        /// <summary>
        /// If the message object is encoded abnormally, check the error log, exception: {0} / 如果消息对象编码异常，请检查错误日志，异常：{0}
        /// </summary>
        /// <remarks>
        /// 键名: Network.MessageEncodingError
        /// 用途: 当消息对象编码异常时记录错误
        /// 参数: {0} - 异常消息
        /// </remarks>
        public const string MessageEncodingError = "Network.MessageEncodingError";

        /// <summary>
        /// Session type '{0}' can not be cast to '{1}' / 会话类型 '{0}' 无法转换为 '{1}'
        /// </summary>
        /// <remarks>
        /// 键名: Network.SessionTypeCastInvalid
        /// 用途: 当会话类型无法转换为目标 WebSocket 会话类型时抛出异常
        /// 参数: {0} - 实际会话类型全名; {1} - 目标会话类型全名
        /// </remarks>
        public const string SessionTypeCastInvalid = "Network.SessionTypeCastInvalid";

        /// <summary>
        /// Send Message Timeout:{0} {1}
        /// </summary>
        /// <remarks>
        /// 键名: Network.SendMessageTimeout
        /// 用途: 发送消息超时被取消时记录错误
        /// 参数: {0} - 角色Id, {1} - 超时消息
        /// </remarks>
        public const string SendMessageTimeout = "Network.SendMessageTimeout";

        /// <summary>
        /// Send Message Error:{0} {1}
        /// </summary>
        /// <remarks>
        /// 键名: Network.SendMessageError
        /// 用途: 发送消息发生异常时记录错误
        /// 参数: {0} - 角色Id, {1} - 异常消息
        /// </remarks>
        public const string SendMessageError = "Network.SendMessageError";

        /// <summary>
        /// Send HeartBeat Message:{0} {1}
        /// </summary>
        /// <remarks>
        /// 键名: Network.SendHeartBeatMessage
        /// 用途: 开启调试时记录心跳消息发送日志
        /// 参数: {0} - 角色Id, {1} - 格式化的消息内容
        /// </remarks>
        public const string SendHeartBeatMessage = "Network.SendHeartBeatMessage";

        /// <summary>
        /// Send Message:{0} {1} {2}
        /// </summary>
        /// <remarks>
        /// 键名: Network.SendMessage
        /// 用途: 开启调试时记录非心跳消息发送日志
        /// 参数: {0} - 角色Id, {1} - 响应错误码, {2} - 格式化的消息内容
        /// </remarks>
        public const string SendMessage = "Network.SendMessage";

        /// <summary>
        /// Session authentication rejected and closing: SessionId: {0}, RemoteEndPoint: {1}, MessageId: {2}
        /// </summary>
        /// <remarks>
        /// 键名: Network.SessionAuthenticationRejected
        /// 用途: 会话认证被拒绝并关闭时记录警告
        /// 参数: {0} - 会话Id, {1} - 远程端点, {2} - 消息Id
        /// </remarks>
        public const string SessionAuthenticationRejected = "Network.SessionAuthenticationRejected";

        /// <summary>
        /// Failed to close unauthenticated session: SessionId: {0}, exception: {1}
        /// </summary>
        /// <remarks>
        /// 键名: Network.CloseUnauthenticatedSessionFailed
        /// 用途: 关闭未认证会话失败时记录错误
        /// 参数: {0} - 会话Id, {1} - 异常消息
        /// </remarks>
        public const string CloseUnauthenticatedSessionFailed = "Network.CloseUnauthenticatedSessionFailed";

        /// <summary>
        /// Error happened when scanning authentication timeout sessions: {0}
        /// </summary>
        /// <remarks>
        /// 键名: Network.ScanAuthenticationTimeoutSessionsError
        /// 用途: 扫描认证超时会话发生异常时记录错误
        /// 参数: {0} - 异常消息
        /// </remarks>
        public const string ScanAuthenticationTimeoutSessionsError = "Network.ScanAuthenticationTimeoutSessionsError";

        /// <summary>
        /// Session authentication timeout, closing: SessionId: {0}, RemoteEndPoint: {1}
        /// </summary>
        /// <remarks>
        /// 键名: Network.SessionAuthenticationTimeout
        /// 用途: 会话认证超时关闭时记录警告
        /// 参数: {0} - 会话Id, {1} - 远程端点
        /// </remarks>
        public const string SessionAuthenticationTimeout = "Network.SessionAuthenticationTimeout";

        /// <summary>
        /// Failed to close authentication-timeout session: SessionId: {0}, exception: {1}
        /// </summary>
        /// <remarks>
        /// 键名: Network.CloseAuthenticationTimeoutSessionFailed
        /// 用途: 关闭认证超时会话失败时记录错误
        /// 参数: {0} - 会话Id, {1} - 异常消息
        /// </remarks>
        public const string CloseAuthenticationTimeoutSessionFailed = "Network.CloseAuthenticationTimeoutSessionFailed";

        /// <summary>
        /// Error happened when closing authentication timeout sessions: {0}
        /// </summary>
        /// <remarks>
        /// 键名: Network.CloseAuthenticationTimeoutSessionsError
        /// 用途: 批量关闭认证超时会话发生异常时记录错误
        /// 参数: {0} - 异常消息
        /// </remarks>
        public const string CloseAuthenticationTimeoutSessionsError = "Network.CloseAuthenticationTimeoutSessionsError";

        /// <summary>
        /// Create NetworkMessagePackage Error {0} {1} {2} {3}
        /// </summary>
        /// <remarks>
        /// 键名: Network.CreateNetworkMessagePackageError
        /// 用途: 创建网络消息包失败时记录错误
        /// 参数: {0} - 消息Id, {1} - 操作类型, {2} - 唯一Id, {3} - 异常消息
        /// </remarks>
        public const string CreateNetworkMessagePackageError = "Network.CreateNetworkMessagePackageError";

        /// <summary>
        /// FormatMessage Error {0} {1} {2} {3} {4}
        /// </summary>
        /// <remarks>
        /// 键名: Network.FormatMessageError
        /// 用途: 格式化消息日志失败时记录错误
        /// 参数: {0} - 消息Id, {1} - 操作类型, {2} - 唯一Id, {3} - 角色Id, {4} - 异常对象
        /// </remarks>
        public const string FormatMessageError = "Network.FormatMessageError";

        /// <summary>
        /// MessageObjectEncodeException {0} {1} {2} {3}
        /// </summary>
        /// <remarks>
        /// 键名: Network.MessageObjectEncodeException
        /// 用途: 消息对象编码发生异常时记录错误
        /// 参数: {0} - 消息Id, {1} - 操作类型, {2} - 唯一Id, {3} - 异常对象
        /// </remarks>
        public const string MessageObjectEncodeException = "Network.MessageObjectEncodeException";
    }
}