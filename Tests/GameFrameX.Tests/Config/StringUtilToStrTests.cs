// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text;
using GameFrameX.Core.Config;
using Xunit;

namespace GameFrameX.Tests.Config;

/// <summary>
/// StringUtil.ToStr 属性守卫回归单测（GFX-1178 / CR-L03）：
/// 无 getter 属性与索引器属性必须被跳过——旧实现对其调用 GetValue 抛 ArgumentException。
/// </summary>
public class StringUtilToStrTests
{
    private sealed class ProbeObject
    {
#pragma warning disable CS0169 // 未读取字段：本探针字段仅用于 ToStr 输出断言
        private readonly int _hiddenField = 7;
#pragma warning restore CS0169

        public int ReadableField = 42;

        public int ReadableProperty => 99;

        public int WriteOnlyProperty
        {
            set { _ = value; }
        }

        public int this[int index] => index;
    }

    [Fact]
    public void ToStr_ObjectWithIndexerAndWriteOnlyProperty_DoesNotThrow()
    {
        var result = StringUtil.ToStr(new ProbeObject(), new StringBuilder());

        Assert.NotNull(result);
    }

    [Fact]
    public void ToStr_IncludesReadableMembers()
    {
        var result = StringUtil.ToStr(new ProbeObject());

        Assert.Contains("ReadableField = 42", result);
        Assert.Contains("ReadableProperty = 99", result);
    }

    [Fact]
    public void ToStr_EmptyObject_ReturnsEmptyContent()
    {
        var result = StringUtil.ToStr(new object());

        Assert.Equal(string.Empty, result);
    }
}
