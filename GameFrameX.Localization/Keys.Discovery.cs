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
/// 本地化字符串键常量类 - Discovery 模块
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// Discovery 模块的本地化字符串键。
    /// </summary>
    public static class Discovery
    {
        /// <summary>
        /// 端点字符串为空，期望统一 scheme://host:port 格式
        /// </summary>
        public static class Endpoint
        {
            /// <summary>
            /// 端点字符串为空；期望统一的 scheme://host:port 格式（如 tcp://game-1.gameframex:7777）
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.EmptyString
            /// 用途: EndpointParser.Parse 在输入去除首尾空白后为空字符串时抛出
            /// </remarks>
            public const string EmptyString = "Discovery.Endpoint.EmptyString";

            /// <summary>
            /// 端点 '{0}' 缺少必需的 scheme://host:port 结构：缺少带非空 scheme 与 authority 的 '://' 分隔符
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.MissingSchemeSeparator
            /// 用途: EndpointParser.Parse 在找不到合法的 '://' 分隔结构时抛出
            /// 参数: {0} - 原始端点字符串
            /// </remarks>
            public const string MissingSchemeSeparator = "Discovery.Endpoint.MissingSchemeSeparator";

            /// <summary>
            /// 端点 '{0}' 使用了不受支持的 scheme '{1}'。支持的 scheme：{2}。
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.UnsupportedScheme
            /// 用途: EndpointParser.Parse 在 scheme 不在受支持列表（tcp/kcp/ws/wss）时抛出
            /// 参数: {0} - 原始端点字符串；{1} - 实际的 scheme；{2} - 受支持的 scheme 列表
            /// </remarks>
            public const string UnsupportedScheme = "Discovery.Endpoint.UnsupportedScheme";

            /// <summary>
            /// 端点 '{0}' 在 scheme 之后 host 为空
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.EmptyHostAfterScheme
            /// 用途: EndpointParser 解析 authority 段发现其为空时抛出
            /// 参数: {0} - 原始端点字符串
            /// </remarks>
            public const string EmptyHostAfterScheme = "Discovery.Endpoint.EmptyHostAfterScheme";

            /// <summary>
            /// 端点 '{0}' 的方括号 IPv6 host 格式错误：期望 '[IPv6 字面量]:端口'
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.BracketedIpv6Malformed
            /// 用途: EndpointParser 解析方括号 authority 时未找到 ']' 或方括号内为空时抛出
            /// 参数: {0} - 原始端点字符串
            /// </remarks>
            public const string BracketedIpv6Malformed = "Discovery.Endpoint.BracketedIpv6Malformed";

            /// <summary>
            /// 端点 '{0}' 的方括号 host '{1}' 不是合法的 IPv6 字面量
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.BracketedHostNotIpv6
            /// 用途: EndpointParser 解析方括号 host 内容无法解析为 IPv6 地址时抛出
            /// 参数: {0} - 原始端点字符串；{1} - 方括号内的 host 文本
            /// </remarks>
            public const string BracketedHostNotIpv6 = "Discovery.Endpoint.BracketedHostNotIpv6";

            /// <summary>
            /// 端点 '{0}' 在方括号 IPv6 host 之后缺少端口：期望 '[IPv6 字面量]:端口'
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.MissingPortAfterBracketedIpv6
            /// 用途: EndpointParser 在方括号 IPv6 host 后未跟随 ':端口' 时抛出
            /// 参数: {0} - 原始端点字符串
            /// </remarks>
            public const string MissingPortAfterBracketedIpv6 = "Discovery.Endpoint.MissingPortAfterBracketedIpv6";

            /// <summary>
            /// 端点 '{0}' 缺少端口：期望 'scheme://host:port'
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.MissingPort
            /// 用途: EndpointParser 在 authority 中找不到 host 与端口的分隔冒号时抛出
            /// 参数: {0} - 原始端点字符串
            /// </remarks>
            public const string MissingPort = "Discovery.Endpoint.MissingPort";

            /// <summary>
            /// 端点 '{0}' 在端口之前 host 为空
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.EmptyHostBeforePort
            /// 用途: EndpointParser 在最后一个冒号之前的 host 段为空时抛出
            /// 参数: {0} - 原始端点字符串
            /// </remarks>
            public const string EmptyHostBeforePort = "Discovery.Endpoint.EmptyHostBeforePort";

            /// <summary>
            /// 端点 '{0}' 使用了未加方括号的 IPv6 字面量 '{1}'；IPv6 host 必须写成 '[IPv6 字面量]:端口'
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.UnbracketedIpv6
            /// 用途: EndpointParser 发现未加方括号的 IPv6 字面量 host 时抛出
            /// 参数: {0} - 原始端点字符串；{1} - 未加方括号的 IPv6 host 文本
            /// </remarks>
            public const string UnbracketedIpv6 = "Discovery.Endpoint.UnbracketedIpv6";

            /// <summary>
            /// 端点 '{0}' 的 host '{1}' 非法：DNS、容器名或 Service 名中不允许空白以及 URI 分隔符 '/'、'?'、'#'、'@'、':'
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.InvalidHostCharacters
            /// 用途: EndpointParser 校验非 IP host 时发现空白或 URI 分隔符字符时抛出
            /// 参数: {0} - 原始端点字符串；{1} - 非法的 host 文本
            /// </remarks>
            public const string InvalidHostCharacters = "Discovery.Endpoint.InvalidHostCharacters";

            /// <summary>
            /// 端点 '{0}' 的端口 '{1}' 非法：期望 1-65535 之间的整数
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.Endpoint.InvalidPort
            /// 用途: EndpointParser 在端口非整数或超出 1-65535 范围时抛出
            /// 参数: {0} - 原始端点字符串；{1} - 端口文本
            /// </remarks>
            public const string InvalidPort = "Discovery.Endpoint.InvalidPort";
        }

        /// <summary>
        /// 玩家路由同步模块的本地化字符串键。
        /// </summary>
        public static class PlayerRouteSync
        {
            /// <summary>
            /// 实例 Id 不能为空。
            /// </summary>
            /// <remarks>
            /// 键名: Discovery.PlayerRouteSync.InstanceIdEmpty
            /// 用途: PlayerRouteSyncTarget.UpsertAsync 在记录的 InstanceId 为空白时抛出
            /// </remarks>
            public const string InstanceIdEmpty = "Discovery.PlayerRouteSync.InstanceIdEmpty";
        }
    }
}
