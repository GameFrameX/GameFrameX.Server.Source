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

using System;
using GameFrameX.Core.Abstractions.Attribute;
using Xunit;

namespace GameFrameX.Tests.Core;

/// <summary>
/// TimeOutAttribute(TimeSpan) 总毫秒语义回归单测（GFX-1178 / CR-L04）：
/// 旧实现取 <c>TimeSpan.Milliseconds</c>（0-999 分量），30 秒被截断为 0、1 分 30 秒错成 30000。
/// </summary>
public class TimeOutAttributeTests
{
    [Fact]
    public void TimeSpan30Seconds_MapsTo30000Milliseconds()
    {
        var attribute = new TimeOutAttribute(TimeSpan.FromSeconds(30));

        Assert.Equal(30000, attribute.TimeOut);
    }

    [Fact]
    public void TimeSpan90Seconds_MapsTo90000Milliseconds()
    {
        var attribute = new TimeOutAttribute(new TimeSpan(0, 1, 30));

        Assert.Equal(90000, attribute.TimeOut);
    }

    [Fact]
    public void IntOverload_Unchanged()
    {
        var attribute = new TimeOutAttribute(12000);

        Assert.Equal(12000, attribute.TimeOut);
    }
}
