// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整 license 文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using System.Linq;

namespace GameFrameX.Client.Bot;

/// <summary>
/// 机器人传输类型常量与校验工具。
/// </summary>
/// <remarks>
/// 命令行 <c>--transport</c> 参数与 <see cref="BotRunOptions.Transport"/> 字段共享同一字符串集合。
/// 新增传输类型时同步追加常量与 <see cref="SupportedValues"/>，<see cref="BotClient"/> 构造期基于此分派客户端实现。
/// </remarks>
public static class BotTransport
{
    /// <summary>
    /// TCP 传输标识（默认值）。
    /// </summary>
    public const string Tcp = "tcp";

    /// <summary>
    /// KCP 传输标识。
    /// </summary>
    public const string Kcp = "kcp";

    /// <summary>
    /// 支持的传输类型集合（按顺序用于错误信息展示）。
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedValues = new[] { Tcp, Kcp };

    /// <summary>
    /// 校验传输字符串是否在 <see cref="SupportedValues"/> 之中（大小写不敏感）。
    /// </summary>
    /// <param name="value">待校验值</param>
    /// <returns>命中返回 true</returns>
    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return SupportedValues.Any(supported => string.Equals(supported, value, StringComparison.OrdinalIgnoreCase));
    }
}