// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国和相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
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
/// 本地化键常量定义 - Config 分部类
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// 配置表相关消息资源键
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// 配置表模块消息资源键
        /// </summary>
        public static class Table
        {
            /// <summary>
            /// 配置表未加载！
            /// </summary>
            /// <remarks>
            /// 键名: Config.Table.NotLoaded
            /// 用途: 配置表未加载完成时调用 SetTranslateText 抛出
            /// </remarks>
            public const string NotLoaded = "Config.Table.NotLoaded";

            /// <summary>
            /// 开始加载配置表...
            /// </summary>
            /// <remarks>
            /// 键名: Config.Table.LoadConfigStart
            /// 用途: 加载配置表流程开始时输出调试日志
            /// </remarks>
            public const string LoadConfigStart = "Config.Table.LoadConfigStart";

            /// <summary>
            /// 配置表加载完成...
            /// </summary>
            /// <remarks>
            /// 键名: Config.Table.LoadConfigEnd
            /// 用途: 加载配置表流程结束时输出调试日志
            /// </remarks>
            public const string LoadConfigEnd = "Config.Table.LoadConfigEnd";
        }
    }
}
