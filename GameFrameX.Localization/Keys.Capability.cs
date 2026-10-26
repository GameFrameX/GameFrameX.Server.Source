// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Localization;

/// <summary>
/// 本地化键常量定义 - Capability 分部类
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// 能力组件机制（Hosting 三件套）相关消息资源键
    /// </summary>
    public static class Capability
    {
        /// <summary>
        /// 能力宿主机制相关消息
        /// </summary>
        public static class Hosting
        {
            /// <summary>
            /// 能力宿主选项非法（作用域三元组或调度间隔）。
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.OptionsInvalid
            /// 用途: CapabilityHost 构造时选项未通过 IsValid 校验
            /// </remarks>
            public const string OptionsInvalid = "Capability.Hosting.OptionsInvalid";

            /// <summary>
            /// 能力 Id 重复注册：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.DuplicateCapability
            /// 用途: 装配器发现重复 manifest Id
            /// 参数: {0} - 重复的能力 Id
            /// </remarks>
            public const string DuplicateCapability = "Capability.Hosting.DuplicateCapability";

            /// <summary>
            /// 部署目录能力程序集加载失败：{0}（{1}）
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.DeploymentAssemblyLoadFailed
            /// 用途: 部署目录扫描按磁盘路径加载命中命名约定的产物失败（损坏 / 残缺 / 非托管）
            /// 参数: {0} - 文件路径；{1} - 底层异常消息
            /// </remarks>
            public const string DeploymentAssemblyLoadFailed = "Capability.Hosting.DeploymentAssemblyLoadFailed";

            /// <summary>
            /// 能力程序集类型扫描失败：{0}（{1} 个类型加载错误）
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.ModuleTypeScanFailed
            /// 用途: 能力包程序集 GetTypes 抛 ReflectionTypeLoadException（如依赖缺失）
            /// 参数: {0} - 程序集名；{1} - 加载失败类型数
            /// </remarks>
            public const string ModuleTypeScanFailed = "Capability.Hosting.ModuleTypeScanFailed";

            /// <summary>
            /// 装配过滤器引用了未发现的能力：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.FilterTargetMissing
            /// 用途: 配置排除表或能力清单段引用未发现的能力 Id
            /// 参数: {0} - 未发现的能力 Id
            /// </remarks>
            public const string FilterTargetMissing = "Capability.Hosting.FilterTargetMissing";

            /// <summary>
            /// 能力 {0} 依赖的能力未装配：{1}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.RequirementMissing
            /// 用途: 闭包校验发现 Requires 指向未装配能力
            /// 参数: {0} - 依赖方能力 Id；{1} - 被依赖能力 Id
            /// </remarks>
            public const string RequirementMissing = "Capability.Hosting.RequirementMissing";

            /// <summary>
            /// 能力依赖成环，无法完成拓扑定序。
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.RequirementCycle
            /// 用途: Requires 拓扑定序存在环
            /// </remarks>
            public const string RequirementCycle = "Capability.Hosting.RequirementCycle";

            /// <summary>
            /// 管理面 HTTP 管道相关消息
            /// </summary>
            public static class AdminApi
            {
                /// <summary>
                /// Action 名称不能为 null 或为空。
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.ActionNameRequired
                /// 用途: 向 action 注册表注册空名 action
                /// </remarks>
                public const string ActionNameRequired = "Capability.Hosting.AdminApi.ActionNameRequired";

                /// <summary>
                /// admin API 路由前缀不能为空。
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.PrefixRequired
                /// 用途: AdminApiServer 启动时前缀为空
                /// </remarks>
                public const string PrefixRequired = "Capability.Hosting.AdminApi.PrefixRequired";

                /// <summary>
                /// 内层响应 JSON 不能为 null 或为空。
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.InnerJsonRequired
                /// 用途: 信封 WriteRaw 收到空内层 JSON（编程错误兜底）
                /// </remarks>
                public const string InnerJsonRequired = "Capability.Hosting.AdminApi.InnerJsonRequired";

                /// <summary>
                /// 能力声明的 action 未注册：{0}（{1}）
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.ActionNotRegistered
                /// 用途: 组合期对账发现 manifest 声明的 action 未实际注册
                /// 参数: {0} - action 名；{1} - 能力 Id
                /// </remarks>
                public const string ActionNotRegistered = "Capability.Hosting.AdminApi.ActionNotRegistered";

                /// <summary>
                /// 管理面监听选项非法（端口 / 前缀）。
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.OptionsInvalid
                /// 用途: 管理面组合工厂校验 AdminApiOptions 未通过
                /// </remarks>
                public const string AdminApiOptionsInvalid = "Capability.Hosting.AdminApi.OptionsInvalid";

                /// <summary>
                /// Admin action 重复注册：{0}（能力或来源：{1}）
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.ActionNameDuplicate
                /// 用途: 同名 action 第二次注册（fail-fast，不再静默覆盖）
                /// 参数: {0} - action 名；{1} - 能力或来源
                /// </remarks>
                public const string ActionNameDuplicate = "Capability.Hosting.AdminApi.ActionNameDuplicate";

                /// <summary>
                /// 能力 {1} 注册的 action 未在其 manifest 声明：{0}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.ActionNotDeclared
                /// 用途: 按能力分组的双向对账：某能力实注册 ⊄ 该能力声明即失败
                /// 参数: {0} - action 名；{1} - 注册该 action 的能力 Id
                /// </remarks>
                public const string ActionNotDeclared = "Capability.Hosting.AdminApi.ActionNotDeclared";

                /// <summary>
                /// 能力 {1} 声明的 action {0} 被能力 {2} 注册（跨能力冒名）
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.ActionOwnerMismatch
                /// 用途: 按能力分组的双向对账：声明者与注册者不是同一能力即失败（防止调用被路由到错误的域处理器）
                /// 参数: {0} - action 名；{1} - 声明该 action 的能力 Id；{2} - 实际注册该 action 的能力 Id
                /// </remarks>
                public const string ActionOwnerMismatch = "Capability.Hosting.AdminApi.ActionOwnerMismatch";

                /// <summary>
                /// 管理面缺少必需适配器服务：{0}（族基建须注册）
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.AdapterMissing
                /// 用途: 管理面组装所需适配器服务缺席（fail-fast）
                /// 参数: {0} - 适配器服务类型
                /// </remarks>
                public const string AdapterMissing = "Capability.Hosting.AdminApi.AdapterMissing";

                /// <summary>
                /// Admin action 对账失败（{0} 项）：
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.AdminApi.ReconcileFailed
                /// 用途: 双向对账失败汇总标题（Error），后接逐条问题
                /// 参数: {0} - 问题条目数
                /// </remarks>
                public const string ReconcileFailed = "Capability.Hosting.AdminApi.ReconcileFailed";
            }

            /// <summary>
            /// 组合入口相关消息
            /// </summary>
            public static class Composition
            {
                /// <summary>
                /// 未启用任何能力面（业务与管理面至少启用其一）。
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Composition.NoPlaneEnabled
                /// 用途: CapabilityComposition.Compose 时 EnableRuntime 为 false 且未设置管理面工厂
                /// </remarks>
                public const string NoPlaneEnabled = "Capability.Hosting.Composition.NoPlaneEnabled";
            }

            /// <summary>
            /// 装配日志相关消息（内容从 CapabilityReport 渲染，级别只用 Info 与 Error）
            /// </summary>
            public static class Assembly
            {
                /// <summary>
                /// 能力装配开始：发现 {0} 个，启用 {1} 个，排除 {2} 个
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.Started
                /// 用途: 装配日志首行（Info）
                /// 参数: {0} - 发现的能力数；{1} - 过滤后启用的能力数；{2} - 被排除的能力数
                /// </remarks>
                public const string AssemblyStarted = "Capability.Hosting.Assembly.Started";

                /// <summary>
                /// 已排除 {0}：{1}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.Excluded
                /// 用途: 逐条披露被过滤器排除的能力（Info）
                /// 参数: {0} - 能力 Id；{1} - 排除原因
                /// </remarks>
                public const string AssemblyExcluded = "Capability.Hosting.Assembly.Excluded";

                /// <summary>
                /// #{0} {1} {2} {3}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.ModuleNoDependency
                /// 用途: 无依赖能力的逐能力汇总行（Info）
                /// 参数: {0} - 装配序号；{1} - 能力 Id；{2} - 版本；{3} - 程序集名
                /// </remarks>
                public const string AssemblyModuleNoDependency = "Capability.Hosting.Assembly.ModuleNoDependency";

                /// <summary>
                /// #{0} {1} {2} {3}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.ModuleWithDependency
                /// 用途: 有依赖能力的逐能力汇总行（Info），依赖描述由 依赖/可选状态 键拼接
                /// 参数: {0} - 装配序号；{1} - 能力 Id；{2} - 版本；{3} - 依赖描述
                /// </remarks>
                public const string AssemblyModuleWithDependency = "Capability.Hosting.Assembly.ModuleWithDependency";

                /// <summary>
                /// 必选 {0}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.DependencyRequired
                /// 用途: 汇总行内单条必选依赖片段
                /// 参数: {0} - 被依赖能力 Id
                /// </remarks>
                public const string AssemblyDependencyRequired = "Capability.Hosting.Assembly.DependencyRequired";

                /// <summary>
                /// 可选 {0}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.DependencyOptionalListed
                /// 用途: 汇总行内单条可选依赖片段（后接装配状态）
                /// 参数: {0} - 被依赖能力 Id（含装配状态后缀）
                /// </remarks>
                public const string AssemblyDependencyOptionalListed = "Capability.Hosting.Assembly.DependencyOptionalListed";

                /// <summary>
                /// 可选 {0} 个已装配，{1} 个未装配
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.DependencyOptionalCounted
                /// 用途: 可选依赖较多时汇总行的计数式描述
                /// 参数: {0} - 已装配的可选依赖数；{1} - 未装配的可选依赖数
                /// </remarks>
                public const string AssemblyDependencyOptionalCounted = "Capability.Hosting.Assembly.DependencyOptionalCounted";

                /// <summary>
                /// {0}(已装配)
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.OptionalStateAssembled
                /// 用途: 可选依赖已装配状态后缀
                /// 参数: {0} - 被依赖能力 Id
                /// </remarks>
                public const string AssemblyOptionalStateAssembled = "Capability.Hosting.Assembly.OptionalStateAssembled";

                /// <summary>
                /// {0}(未装配)
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.OptionalStateNotAssembled
                /// 用途: 可选依赖未装配状态后缀
                /// 参数: {0} - 被依赖能力 Id
                /// </remarks>
                public const string AssemblyOptionalStateNotAssembled = "Capability.Hosting.Assembly.OptionalStateNotAssembled";

                /// <summary>
                /// {0} 可选依赖 {1} 未装配（{2}）：缺失功能「{3}」，不影响 {0} 其余功能运行
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.OptionalDependencyMissing
                /// 用途: 可选依赖缺失只在装配期打一次 Info；缺失功能文案由能力包提供，原因与"不影响运行"由机制统一拼接
                /// 参数: {0} - 依赖方能力 Id；{1} - 被依赖能力 Id；{2} - 缺失原因；{3} - 缺失功能描述
                /// </remarks>
                public const string AssemblyOptionalDependencyMissing = "Capability.Hosting.Assembly.OptionalDependencyMissing";

                /// <summary>
                /// 能力装配完成：{0} 个能力，{1} 项功能因可选依赖缺失未装配，均不影响能力运行
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.Completed
                /// 用途: 装配日志末行（Info）
                /// 参数: {0} - 装配能力数；{1} - 因可选依赖缺失未装配的功能数
                /// </remarks>
                public const string AssemblyCompleted = "Capability.Hosting.Assembly.Completed";

                /// <summary>
                /// 能力装配失败（{0} 项）：
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.Failed
                /// 用途: 汇总失败标题行（Error），后接逐条问题条目
                /// 参数: {0} - 问题条目数
                /// </remarks>
                public const string AssemblyFailed = "Capability.Hosting.Assembly.Failed";

                /// <summary>
                /// {0} 必选依赖 {1} 未装配（{2}）
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.RequiredDependencyMissing
                /// 用途: 必选依赖缺失问题条目（Error）
                /// 参数: {0} - 依赖方能力 Id；{1} - 被依赖能力 Id；{2} - 缺失原因
                /// </remarks>
                public const string AssemblyRequiredDependencyMissing = "Capability.Hosting.Assembly.RequiredDependencyMissing";

                /// <summary>
                /// 依赖成环：{0}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.DependencyCycle
                /// 用途: 环路问题条目（Error），打印完整环路路径
                /// 参数: {0} - 环路路径（如 a → b → c → a）
                /// </remarks>
                public const string AssemblyDependencyCycle = "Capability.Hosting.Assembly.DependencyCycle";

                /// <summary>
                /// 配置排除表
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.ReasonExcludedByDisableList
                /// 用途: 缺失/排除原因片段：排除表命中
                /// </remarks>
                public const string AssemblyReasonExcludedByDisableList = "Capability.Hosting.Assembly.ReasonExcludedByDisableList";

                /// <summary>
                /// 白名单未收录
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.ReasonNotInEnableList
                /// 用途: 缺失/排除原因片段：白名单未收录
                /// </remarks>
                public const string AssemblyReasonNotInEnableList = "Capability.Hosting.Assembly.ReasonNotInEnableList";

                /// <summary>
                /// 部署目录未发现
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.ReasonNotDiscovered
                /// 用途: 缺失/排除原因片段：部署目录未发现该能力包
                /// </remarks>
                public const string AssemblyReasonNotDiscovered = "Capability.Hosting.Assembly.ReasonNotDiscovered";

                /// <summary>
                /// 钩子 {0} 已挂载但没有任何能力声明（对应能力未装配或未声明该钩子）
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.HookNotDeclared
                /// 用途: Hotfix 挂载钩子而对应能力被过滤时的 Info 披露
                /// 参数: {0} - 钩子契约类型名
                /// </remarks>
                public const string AssemblyHookNotDeclared = "Capability.Hosting.Assembly.HookNotDeclared";

                /// <summary>
                /// Settings 配置段出现未发现的能力：{0}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.SettingsUnknownCapability
                /// 用途: Options 配置绑定发现未知能力 Id（fail-fast）
                /// 参数: {0} - 未发现的能力 Id
                /// </remarks>
                public const string AssemblySettingsUnknownCapability = "Capability.Hosting.Assembly.SettingsUnknownCapability";

                /// <summary>
                /// Options 字段不存在或不可配置：{0}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.SettingsUnknownField
                /// 用途: Options 配置绑定发现未知字段或只读/常量字段（fail-fast）
                /// 参数: {0} - 完整字段路径（如 Online.Settings.online-social.Friend.FriendLimt）
                /// </remarks>
                public const string AssemblySettingsUnknownField = "Capability.Hosting.Assembly.SettingsUnknownField";

                /// <summary>
                /// Options 字段类型不符：{0}（期望 {1}，实际 {2}）
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.SettingsFieldTypeMismatch
                /// 用途: Options 配置绑定类型不匹配（fail-fast）
                /// 参数: {0} - 完整字段路径；{1} - 期望类型；{2} - 实际类型
                /// </remarks>
                public const string AssemblySettingsFieldTypeMismatch = "Capability.Hosting.Assembly.SettingsFieldTypeMismatch";

                /// <summary>
                /// 能力 {0} Options 校验失败：{1}
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Hosting.Assembly.OptionsValidationFailed
                /// 用途: 合并后 Options 最终值未通过 Validate（fail-fast）
                /// 参数: {0} - 能力 Id；{1} - 校验问题描述
                /// </remarks>
                public const string AssemblyOptionsValidationFailed = "Capability.Hosting.Assembly.OptionsValidationFailed";
            }

            /// <summary>
            /// 能力服务重复注册：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.ServiceRegistrationDuplicate
            /// 用途: 同类型键+命名键重复注册默认实现（fail-fast）
            /// 参数: {0} - 类型键+命名键描述
            /// </remarks>
            public const string ServiceRegistrationDuplicate = "Capability.Hosting.ServiceRegistrationDuplicate";

            /// <summary>
            /// 能力服务覆盖重复：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.ServiceOverrideDuplicate
            /// 用途: 同键第二个覆盖声明（fail-fast；一个默认 + 至多一个覆盖）
            /// 参数: {0} - 类型键+命名键描述
            /// </remarks>
            public const string ServiceOverrideDuplicate = "Capability.Hosting.ServiceOverrideDuplicate";

            /// <summary>
            /// 能力服务未注册：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.ServiceNotRegistered
            /// 用途: GetService 解析不到已声明的服务（fail-fast；可选缺席走 GetOptionalService 返回 null）
            /// 参数: {0} - 服务类型描述
            /// </remarks>
            public const string ServiceNotRegistered = "Capability.Hosting.ServiceNotRegistered";

            /// <summary>
            /// 能力 {0} 按可选语义解析服务 {1} 未命中，但其声明的可选依赖均已装配（或未声明任何可选依赖）：能力在场却未注册该服务
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.OptionalServiceNotRegistered
            /// 用途: 模块作用域 GetOptionalService 的 fail-fast（设计 §4.9.1：对方装了却没注册该服务），防止功能被静默丢弃
            /// 参数: {0} - 解析方能力 Id；{1} - 服务类型描述
            /// </remarks>
            public const string OptionalServiceNotRegistered = "Capability.Hosting.OptionalServiceNotRegistered";

            /// <summary>
            /// 能力服务懒工厂循环解析：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.ServiceResolutionCycle
            /// 用途: 懒工厂解析链中同键重入（A→B→A），fail-fast 代替无限递归栈溢出
            /// 参数: {0} - 解析链（键描述以 " -> " 连接，末项为重入键）
            /// </remarks>
            public const string ServiceResolutionCycle = "Capability.Hosting.ServiceResolutionCycle";

            /// <summary>
            /// 能力 {0} 解析了未声明来源的服务 {1}（来源：{2}）；Register/懒工厂期只允许解析 自己/族基建/宿主覆盖/直接声明的必选与在场可选依赖
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.ServiceResolutionForbidden
            /// 用途: Register 期解析来源检查（fail-fast），拦截靠装配顺序碰巧可用的未声明依赖
            /// 参数: {0} - 解析方能力 Id；{1} - 服务类型；{2} - 实际来源能力 Id
            /// </remarks>
            public const string ServiceResolutionForbidden = "Capability.Hosting.ServiceResolutionForbidden";

            /// <summary>
            /// GetServices 只能在 Activate 期调用：{0} / {1}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.ServicesEnumerationForbidden
            /// 用途: Register 期调用 GetServices 直接抛出（fail-fast），枚举结果不得随装配顺序变化
            /// 参数: {0} - 能力 Id；{1} - 服务类型
            /// </remarks>
            public const string ServicesEnumerationForbidden = "Capability.Hosting.ServicesEnumerationForbidden";

            /// <summary>
            /// 能力 {0} 声明的宿主服务未提供（需宿主 Override）：{1}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.HostServiceMissing
            /// 用途: RequireHostService 声明的扩展点宿主未提供（fail-fast）
            /// 参数: {0} - 声明方能力 Id；{1} - 宿主服务类型
            /// </remarks>
            public const string HostServiceMissing = "Capability.Hosting.HostServiceMissing";

            /// <summary>
            /// 能力桥契约重复发布：{0}（{1} → {2}）
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.BridgeDuplicatePublish
            /// 用途: 同一契约第二个发布者（fail-fast；一个契约只有一个发布者）
            /// 参数: {0} - 契约类型；{1} - 原发布者能力 Id；{2} - 新发布者能力 Id
            /// </remarks>
            public const string BridgeDuplicatePublish = "Capability.Hosting.BridgeDuplicatePublish";

            /// <summary>
            /// 能力桥契约已撤销或被替换：{0}（消费方按 8002 处理）
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.BridgeUnavailable
            /// 用途: CapabilityBridgeUnavailableException 文案；消费方族层映射为 8002
            /// 参数: {0} - 契约类型
            /// </remarks>
            public const string BridgeUnavailable = "Capability.Hosting.BridgeUnavailable";

            /// <summary>
            /// Activate 开始前拒绝发布事件：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.EventPublishRejected
            /// 用途: Register 期发布事件直接拒绝（fail-fast），避免后注册的订阅者漏收
            /// 参数: {0} - 事件 topic
            /// </remarks>
            public const string EventPublishRejected = "Capability.Hosting.EventPublishRejected";

            /// <summary>
            /// 能力事件订阅处理失败：{0}（{1}）
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.EventBus.SubscriberFailed
            /// 用途: 单个订阅体异常记日志后继续分发（不中断总线）
            /// 参数: {0} - 事件 topic；{1} - 异常描述
            /// </remarks>
            public const string EventBusSubscriberFailed = "Capability.Hosting.EventBus.SubscriberFailed";

            /// <summary>
            /// 调度器停止后不可重新启动。
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.Scheduler.RestartAfterStop
            /// 用途: Stop 之后再 Start 抛 InvalidOperationException（一次性生命周期显式化）
            /// </remarks>
            public const string SchedulerRestartAfterStop = "Capability.Hosting.Scheduler.RestartAfterStop";

            /// <summary>
            /// 能力调度逐滴答 job 失败：{0}（{1}）
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.Scheduler.TickJobFailed
            /// 用途: per-job 隔离：单 job 异常记结构化日志后继续当轮剩余 job
            /// 参数: {0} - job 全名（能力Id/jobId）；{1} - 异常描述
            /// </remarks>
            public const string SchedulerTickJobFailed = "Capability.Hosting.Scheduler.TickJobFailed";

            /// <summary>
            /// 能力调度间隔 job 失败：{0}（{1}）
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.Scheduler.IntervalJobFailed
            /// 用途: per-job 隔离：interval job 异常记结构化日志后继续
            /// 参数: {0} - job 全名（能力Id/jobId）；{1} - 异常描述
            /// </remarks>
            public const string SchedulerIntervalJobFailed = "Capability.Hosting.Scheduler.IntervalJobFailed";

            /// <summary>
            /// 能力 {0} 激活失败，已逆序回滚 {1} 个已激活能力
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.ActivationFailed
            /// 用途: ActivateAsync 中途失败时对已激活模块逆序 Deactivate 后抛出
            /// 参数: {0} - 失败能力 Id；{1} - 已回滚的已激活能力数
            /// </remarks>
            public const string ActivationFailed = "Capability.Hosting.ActivationFailed";

            /// <summary>
            /// 能力调度器启动失败，已逆序回滚 {0} 个已激活能力
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.Scheduler.StartFailed
            /// 用途: 全部模块激活成功后调度器启动失败（如已被停止），对已激活模块逆序 Deactivate、撤桥后抛出
            /// 参数: {0} - 已回滚的已激活能力数
            /// </remarks>
            public const string SchedulerStartFailed = "Capability.Hosting.Scheduler.StartFailed";

            /// <summary>
            /// 能力 {0} 停止失败（不阻断其余能力停止）：{1}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.DeactivationFailed
            /// 用途: DeactivateAsync 失败记日志，不阻断其余模块停止
            /// 参数: {0} - 能力 Id；{1} - 异常描述
            /// </remarks>
            public const string DeactivationFailed = "Capability.Hosting.DeactivationFailed";

            /// <summary>
            /// 能力 {0} 声明的 Options 根类型与请求类型不符：{1} ≠ {2}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.OptionsTypeMismatch
            /// 用途: GetOptions 请求类型与 manifest 声明的根类型不一致（fail-fast）
            /// 参数: {0} - 能力 Id；{1} - 声明的根类型；{2} - 请求类型
            /// </remarks>
            public const string OptionsTypeMismatch = "Capability.Hosting.OptionsTypeMismatch";

            /// <summary>
            /// 代码 Configure 引用的 Options 类型未由任何已发现能力声明：{0}
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.ConfigureTargetMissing
            /// 用途: CapabilityCompositionOptions.Configure&lt;T&gt; 的 T 无声明来源（fail-fast）
            /// 参数: {0} - Options 类型名
            /// </remarks>
            public const string ConfigureTargetMissing = "Capability.Hosting.ConfigureTargetMissing";

            /// <summary>
            /// 能力钩子重复声明：{0}（{1} → {2}）
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.HookDuplicateDeclaration
            /// 用途: 同一钩子契约第二个声明者（fail-fast）
            /// 参数: {0} - 钩子契约类型；{1} - 原声明者能力 Id；{2} - 新声明者能力 Id
            /// </remarks>
            public const string HookDuplicateDeclaration = "Capability.Hosting.HookDuplicateDeclaration";

            /// <summary>
            /// 启用管理面需引用 GameFrameX.Capability.Hosting.AdminApi 并调用 UseAdminApi。
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Hosting.AdminApiPlaneFactoryMissing
            /// 用途: EnableAdminApi 开启但管理面工厂程序集未被引用/注册（fail-fast）
            /// </remarks>
            public const string AdminApiPlaneFactoryMissing = "Capability.Hosting.AdminApiPlaneFactoryMissing";
        }

        /// <summary>
        /// Online 能力族相关消息
        /// </summary>
        public static class OnlineModule
        {

            /// <summary>
            /// Online 存储模式未声明告警
            /// </summary>
            /// <remarks>
            /// 键名: Capability.Online.UseGameDbStoresUndeclared
            /// 用途: 启用 Online 能力族（双闸任一开）但未显式声明 UseGameDbStores 时组合根发出的启动期告警（C302 无声降级防护）
            /// 参数: {0} - 组合 Role 的 ServerType
            /// </remarks>
            public const string UseGameDbStoresUndeclared = "Capability.Online.UseGameDbStoresUndeclared";

            /// <summary>
            /// online-match（对局）能力相关消息
            /// </summary>
            public static class Match
            {
                /// <summary>
                /// 对局结算发奖
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Online.Match.OptionalAssetsAbsent
                /// 用途: online-match 可选依赖 online-assets 未装配时披露的缺失功能名（结算结果照常记录）
                /// </remarks>
                public const string OptionalAssetsAbsent = "Capability.Online.Match.OptionalAssetsAbsent";

                /// <summary>
                /// 匹配域（420 入队 / 取消 / 状态查询）
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Online.Match.OptionalMatchmakingAbsent
                /// 用途: online-match 可选依赖 online-matchmaking 未装配时披露的缺失功能名（房间直开局不受影响）
                /// </remarks>
                public const string OptionalMatchmakingAbsent = "Capability.Online.Match.OptionalMatchmakingAbsent";
            }

            /// <summary>
            /// online-competition（竞赛）能力相关消息
            /// </summary>
            public static class Competition
            {
                /// <summary>
                /// 赛季、锦标赛
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Online.Competition.OptionalAssetsAbsent
                /// 用途: online-competition 可选依赖 online-assets 未装配时披露的缺失功能名
                /// </remarks>
                public const string OptionalAssetsAbsent = "Capability.Online.Competition.OptionalAssetsAbsent";

                /// <summary>
                /// 对局结算写榜投影
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Online.Competition.OptionalMatchAbsent
                /// 用途: online-competition 可选依赖 online-match 未装配时披露的缺失功能名
                /// </remarks>
                public const string OptionalMatchAbsent = "Capability.Online.Competition.OptionalMatchAbsent";
            }

            /// <summary>
            /// online-operations（运营）能力相关消息
            /// </summary>
            public static class Operations
            {
                /// <summary>
                /// 运营总览资产分项
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Online.Operations.OptionalAssetsAbsent
                /// 用途: online-operations 可选依赖 online-assets 未装配时披露的缺失功能名
                /// </remarks>
                public const string OptionalAssetsAbsent = "Capability.Online.Operations.OptionalAssetsAbsent";

                /// <summary>
                /// 运营总览社交分项
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Online.Operations.OptionalSocialAbsent
                /// 用途: online-operations 可选依赖 online-social 未装配时披露的缺失功能名
                /// </remarks>
                public const string OptionalSocialAbsent = "Capability.Online.Operations.OptionalSocialAbsent";

                /// <summary>
                /// 运营总览对局分项
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Online.Operations.OptionalMatchAbsent
                /// 用途: online-operations 可选依赖 online-match 未装配时披露的缺失功能名
                /// </remarks>
                public const string OptionalMatchAbsent = "Capability.Online.Operations.OptionalMatchAbsent";

                /// <summary>
                /// 运营总览匹配分项
                /// </summary>
                /// <remarks>
                /// 键名: Capability.Online.Operations.OptionalMatchmakingAbsent
                /// 用途: online-operations 可选依赖 online-matchmaking 未装配时披露的缺失功能名
                /// </remarks>
                public const string OptionalMatchmakingAbsent = "Capability.Online.Operations.OptionalMatchmakingAbsent";
            }
        }
    }
}
