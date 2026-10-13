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
                /// 用途: AddOnlineCapabilities 组合回调后 EnableRuntime 与 EnableAdminApi 均为 false
                /// </remarks>
                public const string NoPlaneEnabled = "Capability.Hosting.Composition.NoPlaneEnabled";
            }
        }
    }
}
