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
/// 本地化键常量定义 - Database 分部类
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// 数据库模块日志键名
    /// </summary>
    public static class Database
    {

        /// <summary>
        /// MongoDB服务初始化失败，连接字符串：{0}，数据库名称：{1}
        /// </summary>
        /// <remarks>
        /// 键名: Database.MongoDb.InitializationFailed
        /// 用途: MongoDB服务初始化失败时记录
        /// 参数: {0} - 连接字符串, {1} - 数据库名称
        /// </remarks>
        public const string MongoDbInitializationFailed = "Database.MongoDb.InitializationFailed";

        /// <summary>
        /// MongoDbService 未初始化，Open() 未成功完成。
        /// </summary>
        /// <remarks>
        /// 键名: Database.MongoDb.ServiceUnavailable
        /// 用途: 当 MongoDbService 尚未初始化就执行数据库操作时抛出
        /// </remarks>
        public const string MongoDbServiceUnavailable = "Database.MongoDb.ServiceUnavailable";

        /// <summary>
        /// ExecuteInTransactionAsync 所有重试均失败，异常未知。
        /// </summary>
        /// <remarks>
        /// 键名: Database.MongoDb.ExecuteInTransactionFailed
        /// 用途: 事务执行重试全部失败后抛出
        /// </remarks>
        public const string MongoDbExecuteInTransactionFailed = "Database.MongoDb.ExecuteInTransactionFailed";

        /// <summary>
        /// CommitTransaction 所有重试均失败，异常未知。
        /// </summary>
        /// <remarks>
        /// 键名: Database.MongoDb.CommitTransactionFailed
        /// 用途: 事务提交重试全部失败后抛出
        /// </remarks>
        public const string MongoDbCommitTransactionFailed = "Database.MongoDb.CommitTransactionFailed";

        /// <summary>
        /// MongoDbService.{0} 所有重试均失败，异常未知。
        /// </summary>
        /// <remarks>
        /// 键名: Database.MongoDb.OperationRetryFailed
        /// 用途: 通用操作重试全部失败后抛出
        /// 参数: {0} - 操作名称
        /// </remarks>
        public const string MongoDbOperationRetryFailed = "Database.MongoDb.OperationRetryFailed";

        /// <summary>
        /// PostgreSQL服务初始化失败，连接目标：{0}，注册名：{1}
        /// </summary>
        /// <remarks>
        /// 键名: Database.PostgreSql.InitializationFailed
        /// 用途: PostgreSQL服务初始化失败时记录
        /// 参数: {0} - 连接目标, {1} - 注册名
        /// </remarks>
        public const string PostgreSqlInitializationFailed = "Database.PostgreSql.InitializationFailed";

        /// <summary>
        /// PostgreSqlDbService 未初始化，Open() 未成功完成。
        /// </summary>
        /// <remarks>
        /// 键名: Database.PostgreSql.ServiceUnavailable
        /// 用途: 当 PostgreSqlDbService 尚未初始化就执行数据库操作时抛出
        /// </remarks>
        public const string PostgreSqlServiceUnavailable = "Database.PostgreSql.ServiceUnavailable";

        /// <summary>
        /// PostgreSqlDbService.ExecuteInTransactionAsync 所有重试均失败，异常未知。
        /// </summary>
        /// <remarks>
        /// 键名: Database.PostgreSql.ExecuteInTransactionFailed
        /// 用途: 事务执行重试全部失败后抛出
        /// </remarks>
        public const string PostgreSqlExecuteInTransactionFailed = "Database.PostgreSql.ExecuteInTransactionFailed";

        /// <summary>
        /// PostgreSqlDbService 事务提交所有重试均失败，异常未知。
        /// </summary>
        /// <remarks>
        /// 键名: Database.PostgreSql.CommitTransactionFailed
        /// 用途: 事务提交重试全部失败后抛出
        /// </remarks>
        public const string PostgreSqlCommitTransactionFailed = "Database.PostgreSql.CommitTransactionFailed";

        /// <summary>
        /// PostgreSqlDbService.{0} 所有重试均失败，异常未知。
        /// </summary>
        /// <remarks>
        /// 键名: Database.PostgreSql.OperationRetryFailed
        /// 用途: 通用操作重试全部失败后抛出
        /// 参数: {0} - 操作名称
        /// </remarks>
        public const string PostgreSqlOperationRetryFailed = "Database.PostgreSql.OperationRetryFailed";
        /// <summary>
        /// 名为“{0}”的数据库已被注册。已注册名称：[{1}]
        /// </summary>
        /// <remarks>
        /// 键名: Database.Registry.AlreadyRegistered
        /// 用途: 以已存在的注册名重复注册数据库时抛出
        /// 参数: {0} - 注册名, {1} - 已注册库名列表
        /// </remarks>
        public const string RegistryAlreadyRegistered = "Database.Registry.AlreadyRegistered";
        /// <summary>
        /// 没有名为“{0}”的数据库被注册。已注册名称：[{1}]
        /// </summary>
        /// <remarks>
        /// 键名: Database.Registry.NotRegistered
        /// 用途: 按未注册的名称获取数据库时抛出
        /// 参数: {0} - 注册名, {1} - 已注册库名列表
        /// </remarks>
        public const string RegistryNotRegistered = "Database.Registry.NotRegistered";
        /// <summary>
        /// 默认数据库已被设置为“{0}”（set-once，C159）；不允许将其更改为“{1}”。已注册名称：[{2}]
        /// </summary>
        /// <remarks>
        /// 键名: Database.Registry.DefaultAlreadySet
        /// 用途: 默认库已显式指定后再次以不同名称 SetDefault 时抛出（含并发路径）
        /// 参数: {0} - 当前默认库名, {1} - 试图设置的库名, {2} - 已注册库名列表
        /// </remarks>
        public const string RegistryDefaultAlreadySet = "Database.Registry.DefaultAlreadySet";
        /// <summary>
        /// 数据库提供者枚举值“{0}”未配置解析映射。已支持：[{1}]。
        /// </summary>
        /// <remarks>
        /// 键名: Database.Provider.NotSupported
        /// 用途: DatabaseProviderType 成员未在 DbProviderResolver 映射表登记时抛出（新增枚举成员必须同步配映射）
        /// 参数: {0} - 枚举值名, {1} - 已支持的枚举值列表
        /// </remarks>
        public const string ProviderNotSupported = "Database.Provider.NotSupported";
        /// <summary>
        /// 无法解析数据库提供者“{0}”的实现类型“{1}”：宿主进程可能未引用对应的 Provider 工程（工程名即类型名中的程序集名）。请添加工程引用，或改用泛型 GameDb.Init&lt;T&gt;(...) 显式指定实现类型（AOT/裁剪场景逃生门）。
        /// </summary>
        /// <remarks>
        /// 键名: Database.Provider.NotInstalled
        /// 用途: 按命名约定反射解析实现类型失败（Type.GetType 返回 null）时抛出，消息含自诊断与逃生门指引
        /// 参数: {0} - 枚举值名, {1} - 期望的程序集限定类型名
        /// </remarks>
        public const string ProviderNotInstalled = "Database.Provider.NotInstalled";
        /// <summary>
        /// 创建数据库提供者实例失败：类型“{0}”（原因：{1}）。请确认该实现具有公共无参构造函数。
        /// </summary>
        /// <remarks>
        /// 键名: Database.Provider.ActivationFailed
        /// 用途: 约定解析的实例化分支失败（如缺少公共无参构造函数）时抛出
        /// 参数: {0} - 程序集限定类型名, {1} - 底层异常消息
        /// </remarks>
        public const string ProviderActivationFailed = "Database.Provider.ActivationFailed";
        /// <summary>
        /// 数据库提供者类型“{0}”未实现 IDatabaseService 接口。
        /// </summary>
        /// <remarks>
        /// 键名: Database.Provider.NotDatabaseService
        /// 用途: 约定解析得到的实例无法转型为 IDatabaseService 时抛出
        /// 参数: {0} - 程序集限定类型名
        /// </remarks>
        public const string ProviderNotDatabaseService = "Database.Provider.NotDatabaseService";
        /// <summary>
        /// 批处理大小必须大于 0。
        /// </summary>
        /// <remarks>
        /// 键名: Database.BatchSizeInvalid
        /// 用途: SaveBulkAsync 的 batchSize &lt;= 0 时抛出（PostgreSql / Mongo 共用）
        /// </remarks>
        public const string BatchSizeInvalid = "Database.BatchSizeInvalid";
        /// <summary>
        /// 必须设置 ConnectionName。
        /// </summary>
        /// <remarks>
        /// 键名: Database.Discovery.ConnectionNameRequired
        /// 用途: 发现层激活参数 ConnectionName 未设置时抛出（Mongo / PostgreSQL 共用）
        /// </remarks>
        public const string DiscoveryConnectionNameRequired = "Database.Discovery.ConnectionNameRequired";
        /// <summary>
        /// PostgreSqlDbContext：文档类型“{0}”声明了字典属性“{1}”（{2}）。EF owned JSON 列无法以兼容形状映射字典；请将其重构为 owned entries 集合或标量载荷。
        /// </summary>
        /// <remarks>
        /// 键名: Database.Ef.DocumentDictionaryPropertyNotSupported
        /// 用途: 文档类型成员为 Dictionary&lt;,&gt; 时在 owned JSON 形状配置阶段显式抛出
        /// 参数: {0} - 文档类型名, {1} - 属性名, {2} - 属性类型名
        /// </remarks>
        public const string EfDocumentDictionaryPropertyNotSupported = "Database.Ef.DocumentDictionaryPropertyNotSupported";
        /// <summary>
        /// GameDb.ImplicitDefaultBinding 已注册多个数据库（{0}）但无声明式默认库提名（DbOptions.IsDefault 均为 false），静态门面当前指向首个注册库；多库部署请在业务库 Init 时保留缺省 IsDefault = true（或显式调用 GameDb.SetDefault(业务库名)）。
        /// </summary>
        /// <remarks>
        /// 键名: Database.GameDb.ImplicitDefaultBindingWarningLog
        /// 用途: 多库注册且无声明式提名时首次门面调用的一次性 Warning 日志（合并原外壳模板与隐式绑定警告文案）
        /// 参数: {0} - 已注册库名列表
        /// </remarks>
        public const string GameDbImplicitDefaultBindingWarningLog = "Database.GameDb.ImplicitDefaultBindingWarningLog";

        /// <summary>
        /// PostgreSQL 载体日志键名（Database.PostgreSql.*）
        /// </summary>
        public static class PostgreSql
        {
            /// <summary>
            /// PostgreSqlDbService.Open {0} {1} PostgreSQL服务初始化成功，连接目标：{2}，注册名：{3}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.OpenInitializedSuccessfully
            /// 用途: PostgreSqlDbService.Open 成功时的 Info 日志（含外壳与初始化成功文案的完整模板）
            /// 参数: {0} - 注册名, {1} - 连接目标, {2} - 连接目标, {3} - 注册名
            /// </remarks>
            public const string OpenInitializedSuccessfully = "Database.PostgreSql.OpenInitializedSuccessfully";

            /// <summary>
            /// PostgreSqlDbService.Open 重试 {0}/{1} {2} {3} {4}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.OpenRetryWarning
            /// 用途: PostgreSqlDbService.Open 连接重试时的 Warning 日志
            /// 参数: {0} - 当前重试次数, {1} - 最大重试次数, {2} - 注册名, {3} - 连接目标, {4} - 异常消息
            /// </remarks>
            public const string OpenRetryWarning = "Database.PostgreSql.OpenRetryWarning";

            /// <summary>
            /// PostgreSqlDbService.Open 异常 {0} {1} {2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.OpenExceptionFatal
            /// 用途: PostgreSqlDbService.Open 最终失败时的 Fatal 日志
            /// 参数: {0} - 注册名, {1} - 连接目标, {2} - 最后异常
            /// </remarks>
            public const string OpenExceptionFatal = "Database.PostgreSql.OpenExceptionFatal";

            /// <summary>
            /// PostgreSqlDbService.Open 异常 {0} {1} {2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.OpenExceptionError
            /// 用途: PostgreSqlDbService.Open 最终失败时的 Error 日志（输出本地化失败消息）
            /// 参数: {0} - 注册名, {1} - 连接目标, {2} - 本地化失败消息
            /// </remarks>
            public const string OpenExceptionError = "Database.PostgreSql.OpenExceptionError";

            /// <summary>
            /// PostgreSqlDbService.TryReconnectAndPingAsync 失败。error={0}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.TryReconnectAndPingFailed
            /// 用途: 重连与探活失败时的 Warning 日志
            /// 参数: {0} - 异常消息
            /// </remarks>
            public const string TryReconnectAndPingFailed = "Database.PostgreSql.TryReconnectAndPingFailed";

            /// <summary>
            /// PostgreSqlDbService 状态迁移 {0} -&gt; {1}。原因：{2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.StateTransition
            /// 用途: 可用性状态机迁移时的 Warning 日志
            /// 参数: {0} - 原状态, {1} - 新状态, {2} - 迁移原因
            /// </remarks>
            public const string StateTransition = "Database.PostgreSql.StateTransition";

            /// <summary>
            /// PostgreSqlDbService 读降级 {0}，状态={1}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.ReadFallback
            /// 用途: 降级状态下读操作回退时的 Warning 日志
            /// 参数: {0} - 操作名称, {1} - 当前状态
            /// </remarks>
            public const string ReadFallback = "Database.PostgreSql.ReadFallback";

            /// <summary>
            /// PostgreSqlDbService.SaveBulkAsync 批次未被确认。状态名：{0}，批次索引：{1}，批次总数：{2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.SaveBulkBatchNotAcknowledged
            /// 用途: SaveBulkAsync 批次写入未获确认时的 Error 日志
            /// 参数: {0} - 状态名, {1} - 批次索引, {2} - 批次总数
            /// </remarks>
            public const string SaveBulkBatchNotAcknowledged = "Database.PostgreSql.SaveBulkBatchNotAcknowledged";

            /// <summary>
            /// PostgreSqlDbService.SaveBulkAsync 批次失败。状态名：{0}，批次索引：{1}，批次总数：{2}，错误：{3}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.SaveBulkBatchFailed
            /// 用途: SaveBulkAsync 批次写入异常时的 Error 日志（逐批隔离继续）
            /// 参数: {0} - 状态名, {1} - 批次索引, {2} - 批次总数, {3} - 异常
            /// </remarks>
            public const string SaveBulkBatchFailed = "Database.PostgreSql.SaveBulkBatchFailed";

            /// <summary>
            /// PostgreSqlDbService.ExecuteInTransactionAsync 瞬时错误，重试 {0}/{1}。error={2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.ExecuteInTransactionTransientError
            /// 用途: 事务执行遇瞬时错误重试时的 Warning 日志
            /// 参数: {0} - 当前重试次数, {1} - 最大重试次数, {2} - 异常消息
            /// </remarks>
            public const string ExecuteInTransactionTransientError = "Database.PostgreSql.ExecuteInTransactionTransientError";

            /// <summary>
            /// PostgreSqlDbService.{0} 瞬时错误，重试 {1}/{2}。error={3}
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.OperationTransientError
            /// 用途: 通用操作遇瞬时错误重试时的 Warning 日志
            /// 参数: {0} - 操作名称, {1} - 当前重试次数, {2} - 最大重试次数, {3} - 异常消息
            /// </remarks>
            public const string OperationTransientError = "Database.PostgreSql.OperationTransientError";

            /// <summary>
            /// [PostgreSqlDiscoveryRuntime] 未配置广播端口（{0}）；心跳写入侧被跳过，本进程仅观察拓扑
            /// </summary>
            /// <remarks>
            /// 键名: Database.PostgreSql.DiscoveryNoAdvertisePort
            /// 用途: 发现层激活时未配置广播端口环境变量的 Warning 日志
            /// 参数: {0} - 广播端口环境变量名
            /// </remarks>
            public const string DiscoveryNoAdvertisePort = "Database.PostgreSql.DiscoveryNoAdvertisePort";
        }

        /// <summary>
        /// MongoDB 载体日志键名（Database.Mongo.*）
        /// </summary>
        public static class Mongo
        {
            /// <summary>
            /// MongoDbService.Open {0} {1} MongoDB服务初始化成功，连接字符串：{2}，数据库名称：{3}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.OpenInitializedSuccessfully
            /// 用途: MongoDbService.Open 成功时的 Info 日志（含外壳与初始化成功文案的完整模板）
            /// 参数: {0} - 注册名, {1} - 连接目标, {2} - 连接目标, {3} - 注册名
            /// </remarks>
            public const string OpenInitializedSuccessfully = "Database.Mongo.OpenInitializedSuccessfully";

            /// <summary>
            /// MongoDbService.Open 重试 {0}/{1} {2} {3} {4}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.OpenRetryWarning
            /// 用途: MongoDbService.Open 连接重试时的 Warning 日志
            /// 参数: {0} - 当前重试次数, {1} - 最大重试次数, {2} - 注册名, {3} - 连接目标, {4} - 异常消息
            /// </remarks>
            public const string OpenRetryWarning = "Database.Mongo.OpenRetryWarning";

            /// <summary>
            /// MongoDbService.Open 异常 {0} {1} {2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.OpenExceptionFatal
            /// 用途: MongoDbService.Open 最终失败时的 Fatal 日志
            /// 参数: {0} - 注册名, {1} - 连接目标, {2} - 最后异常
            /// </remarks>
            public const string OpenExceptionFatal = "Database.Mongo.OpenExceptionFatal";

            /// <summary>
            /// MongoDbService.Open 异常 {0} {1} {2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.OpenExceptionError
            /// 用途: MongoDbService.Open 最终失败时的 Error 日志（输出本地化失败消息）
            /// 参数: {0} - 注册名, {1} - 连接目标, {2} - 本地化失败消息
            /// </remarks>
            public const string OpenExceptionError = "Database.Mongo.OpenExceptionError";

            /// <summary>
            /// MongoDbService.TryReconnectAndPingAsync 失败。error={0}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.TryReconnectAndPingFailed
            /// 用途: 重连与探活失败时的 Warning 日志
            /// 参数: {0} - 异常消息
            /// </remarks>
            public const string TryReconnectAndPingFailed = "Database.Mongo.TryReconnectAndPingFailed";

            /// <summary>
            /// MongoDbService 状态迁移 {0} -&gt; {1}。原因：{2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.StateTransition
            /// 用途: 可用性状态机迁移时的 Warning 日志
            /// 参数: {0} - 原状态, {1} - 新状态, {2} - 迁移原因
            /// </remarks>
            public const string StateTransition = "Database.Mongo.StateTransition";

            /// <summary>
            /// MongoDbService 读降级 {0}，状态={1}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.ReadFallback
            /// 用途: 降级状态下读操作回退时的 Warning 日志
            /// 参数: {0} - 操作名称, {1} - 当前状态
            /// </remarks>
            public const string ReadFallback = "Database.Mongo.ReadFallback";

            /// <summary>
            /// MongoDbService.SaveBulkAsync 批次未被确认。状态名：{0}，批次索引：{1}，批次总数：{2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.SaveBulkBatchNotAcknowledged
            /// 用途: SaveBulkAsync 批次写入未获确认时的 Error 日志
            /// 参数: {0} - 状态名, {1} - 批次索引, {2} - 批次总数
            /// </remarks>
            public const string SaveBulkBatchNotAcknowledged = "Database.Mongo.SaveBulkBatchNotAcknowledged";

            /// <summary>
            /// MongoDbService.SaveBulkAsync 批次失败。状态名：{0}，批次索引：{1}，批次总数：{2}，错误：{3}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.SaveBulkBatchFailed
            /// 用途: SaveBulkAsync 批次写入异常时的 Error 日志（逐批隔离继续）
            /// 参数: {0} - 状态名, {1} - 批次索引, {2} - 批次总数, {3} - 异常
            /// </remarks>
            public const string SaveBulkBatchFailed = "Database.Mongo.SaveBulkBatchFailed";

            /// <summary>
            /// MongoDbService.ExecuteInTransactionAsync 瞬时错误，重试 {0}/{1}。error={2}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.ExecuteInTransactionTransientError
            /// 用途: 事务执行遇瞬时错误重试时的 Warning 日志
            /// 参数: {0} - 当前重试次数, {1} - 最大重试次数, {2} - 异常消息
            /// </remarks>
            public const string ExecuteInTransactionTransientError = "Database.Mongo.ExecuteInTransactionTransientError";

            /// <summary>
            /// MongoDbService.{0} 瞬时错误，重试 {1}/{2}。error={3}
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.OperationTransientError
            /// 用途: 通用操作遇瞬时错误重试时的 Warning 日志
            /// 参数: {0} - 操作名称, {1} - 当前重试次数, {2} - 最大重试次数, {3} - 异常消息
            /// </remarks>
            public const string OperationTransientError = "Database.Mongo.OperationTransientError";

            /// <summary>
            /// [MongoDiscoveryRuntime] 未配置广播端口（{0}）；心跳写入侧被跳过，本进程仅观察拓扑
            /// </summary>
            /// <remarks>
            /// 键名: Database.Mongo.DiscoveryNoAdvertisePort
            /// 用途: 发现层激活时未配置广播端口环境变量的 Warning 日志
            /// 参数: {0} - 广播端口环境变量名
            /// </remarks>
            public const string DiscoveryNoAdvertisePort = "Database.Mongo.DiscoveryNoAdvertisePort";
        }
    }
}