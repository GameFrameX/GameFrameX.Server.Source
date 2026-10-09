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
/// 本地化键常量定义 - RemoteMessaging 分部类
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// RemoteMessaging 模块相关错误消息资源键
    /// </summary>
    public static class RemoteMessaging
    {
        /// <summary>
        /// 压缩算法类别键
        /// </summary>
        public static class Compression
        {
            /// <summary>
            /// AlgorithmId 0 保留给未压缩载荷
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Compression.AlgorithmIdReserved
            /// 用途: 当注册的压缩算法 AlgorithmId 为 0 时抛出
            /// </remarks>
            public const string AlgorithmIdReserved = "RemoteMessaging.Compression.AlgorithmIdReserved";
        }

        /// <summary>
        /// 消息编解码器类别键
        /// </summary>
        public static class Codec
        {
            /// <summary>
            /// 压缩阈值必须大于等于 0
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Codec.CompressionThresholdInvalid
            /// 用途: 当压缩阈值小于 0 时抛出
            /// </remarks>
            public const string CompressionThresholdInvalid = "RemoteMessaging.Codec.CompressionThresholdInvalid";

            /// <summary>
            /// 最大包大小必须大于等于 {0}
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Codec.MaxPacketSizeInvalid
            /// 用途: 当最大包大小小于包头长度时抛出
            /// 参数: {0} - 内层包头长度
            /// </remarks>
            public const string MaxPacketSizeInvalid = "RemoteMessaging.Codec.MaxPacketSizeInvalid";

            /// <summary>
            /// 最大解压大小必须大于等于 0
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Codec.MaxDecompressedSizeInvalid
            /// 用途: 当最大解压大小小于 0 时抛出
            /// </remarks>
            public const string MaxDecompressedSizeInvalid = "RemoteMessaging.Codec.MaxDecompressedSizeInvalid";

            /// <summary>
            /// 压缩算法 '{0}' 未注册
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Codec.CompressionAlgorithmNotRegistered
            /// 用途: 当指定的压缩算法 ID 未在注册表中注册时抛出
            /// 参数: {0} - 压缩算法 ID
            /// </remarks>
            public const string CompressionAlgorithmNotRegistered = "RemoteMessaging.Codec.CompressionAlgorithmNotRegistered";

            /// <summary>
            /// 远程连接已关闭
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Codec.RemoteConnectionClosed
            /// 用途: 当读取流时对端关闭连接（读取到 0 字节）时抛出
            /// </remarks>
            public const string RemoteConnectionClosed = "RemoteMessaging.Codec.RemoteConnectionClosed";

            /// <summary>
            /// 远程消息包长度非法：{0}，允许范围 {1}-{2}
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Codec.InvalidPacketLength
            /// 用途: 当远程消息包总长度超出允许范围时抛出
            /// 参数: {0} - 实际包长度; {1} - 最小允许长度; {2} - 最大允许长度
            /// </remarks>
            public const string InvalidPacketLength = "RemoteMessaging.Codec.InvalidPacketLength";

            /// <summary>
            /// 远程消息载荷长度超限。载荷长度：{0}，最大：{1}
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Codec.PayloadLengthExceedsLimit
            /// 用途: 当解码后的载荷长度超过最大解压大小限制时抛出
            /// 参数: {0} - 实际载荷长度; {1} - 最大解压大小
            /// </remarks>
            public const string PayloadLengthExceedsLimit = "RemoteMessaging.Codec.PayloadLengthExceedsLimit";
        }

        /// <summary>
        /// 统一消息发送器类别键
        /// </summary>
        public static class Unified
        {
            /// <summary>
            /// UnifiedMessageSenderHolder 尚未初始化。请在启动期间先调用 Initialize()
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Unified.SenderNotInitialized
            /// 用途: 当在调用 Initialize 之前访问全局 Sender 时抛出
            /// </remarks>
            public const string SenderNotInitialized = "RemoteMessaging.Unified.SenderNotInitialized";

            /// <summary>
            /// UnifiedMessageSender 指标不可用。请先使用 UnifiedMessageSender 调用 Initialize()
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Unified.MetricsUnavailable
            /// 用途: 当未初始化或发送器未提供指标实例时访问 Metrics 抛出
            /// </remarks>
            public const string MetricsUnavailable = "RemoteMessaging.Unified.MetricsUnavailable";
        }

        /// <summary>
        /// 故障注入类别键
        /// </summary>
        public static class FaultInjection
        {
            /// <summary>
            /// 故障注入：模拟超时
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.FaultInjection.SimulatedTimeout
            /// 用途: 故障注入拦截器模拟超时故障时抛出
            /// </remarks>
            public const string SimulatedTimeout = "RemoteMessaging.FaultInjection.SimulatedTimeout";

            /// <summary>
            /// 故障注入：模拟连接断开
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.FaultInjection.SimulatedConnectionDrop
            /// 用途: 故障注入拦截器模拟连接断开故障时抛出
            /// </remarks>
            public const string SimulatedConnectionDrop = "RemoteMessaging.FaultInjection.SimulatedConnectionDrop";
        }

        /// <summary>
        /// 路由类别键
        /// </summary>
        public static class Routing
        {
            /// <summary>
            /// DiscoveryRoutingWire.AttachLocalDispatcher 必须在 Initialize 之后调用；路由器重建需要托管角色快照以及由 Initialize 创建的远程路由器
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Routing.AttachLocalDispatcherOrderInvalid
            /// 用途: 当在 Initialize 之前调用 AttachLocalDispatcher 时抛出
            /// </remarks>
            public const string AttachLocalDispatcherOrderInvalid = "RemoteMessaging.Routing.AttachLocalDispatcherOrderInvalid";

            /// <summary>
            /// 无法路由缺少目标角色的信封
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Routing.TargetRoleMissing
            /// 用途: 当消息信封缺少目标角色时路由抛出
            /// </remarks>
            public const string TargetRoleMissing = "RemoteMessaging.Routing.TargetRoleMissing";

            /// <summary>
            /// 角色 '{1}' 的目标实例 '{0}' 不在双视图路由表中（ID 未知、离线或已被移除）
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Routing.TargetInstanceNotFound
            /// 用途: 当指定实例投递时目标实例在双视图路由表中不存在时抛出
            /// 参数: {0} - 目标实例 ID; {1} - 目标角色
            /// </remarks>
            public const string TargetInstanceNotFound = "RemoteMessaging.Routing.TargetInstanceNotFound";

            /// <summary>
            /// 角色 '{0}' 在双视图路由表中没有 Active 实例；该角色已缩容至零或正在完全排水
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Routing.RoleNoActiveInstance
            /// 用途: 当目标角色在路由表中没有任何 Active 实例时抛出
            /// 参数: {0} - 目标角色
            /// </remarks>
            public const string RoleNoActiveInstance = "RemoteMessaging.Routing.RoleNoActiveInstance";

            /// <summary>
            /// 时间窗必须为正数
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Routing.WindowMustBePositive
            /// 用途: 当单飞去重时间窗不为正数时抛出
            /// </remarks>
            public const string WindowMustBePositive = "RemoteMessaging.Routing.WindowMustBePositive";

            /// <summary>
            /// RoleRouterHolder 尚未初始化；请在路由前于启动阶段调用 RoleRouterHolder.Initialize
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Routing.HolderNotInitialized
            /// 用途: 当在调用 Initialize 之前访问全局路由器时抛出
            /// </remarks>
            public const string HolderNotInitialized = "RemoteMessaging.Routing.HolderNotInitialized";

            /// <summary>
            /// 目标角色由本进程承载但未配置本地消息派发器
            /// </summary>
            /// <remarks>
            /// 键名: RemoteMessaging.Routing.LocalDispatcherNotConfigured
            /// 用途: 目标 Role 属于本进程角色集但本地派发器为空时抛出
            /// 参数: {0} - 目标角色名
            /// </remarks>
            public const string LocalDispatcherNotConfigured = "RemoteMessaging.Routing.LocalDispatcherNotConfigured";
        }
    }
}
