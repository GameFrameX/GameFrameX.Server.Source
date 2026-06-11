// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//   This project is dual-licensed under the MIT License and Apache License 2.0,
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

namespace GameFrameX.Hotfix.WrapperGenerator.Utils;

/// <summary>
/// 提供字符串处理的工具方法。
/// </summary>
/// <remarks>
/// Provides utility methods for string processing.
/// </remarks>
public static class Tools
{
    /// <summary>
    /// 从类型的完全限定名中提取命名空间部分。
    /// </summary>
    /// <remarks>
    /// Extracts the namespace portion from a fully qualified type name.
    /// </remarks>
    /// <param name="fullName">类型的完全限定名 / Fully qualified type name</param>
    /// <returns>命名空间字符串；如果输入不包含点号或为空，则返回原始字符串 / Namespace string; returns the original string if input contains no dots or is empty</returns>
    public static string GetNameSpace(string fullName)
    {
        if (string.IsNullOrEmpty(fullName))
        {
            return fullName;
        }

        var i = fullName.LastIndexOf('.');
        if (i < 0)
        {
            return fullName;
        }

        return fullName.Substring(0, i);
    }

    /// <summary>
    /// 移除字符串中的所有空白字符。
    /// </summary>
    /// <remarks>
    /// Removes all whitespace characters from the string.
    /// </remarks>
    /// <param name="str">要处理的字符串 / String to process</param>
    /// <returns>移除空白后的字符串 / String with all whitespace removed</returns>
    public static string RemoveWhitespace(this string str)
    {
        return string.Join("", str.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
    }
}