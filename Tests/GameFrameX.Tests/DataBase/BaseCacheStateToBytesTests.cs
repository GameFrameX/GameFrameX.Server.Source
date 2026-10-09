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
//   please refer to the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using System.Collections.Generic;
using System.Text;
using GameFrameX.DataBase;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// C171：BaseCacheState.ToBytes 默认 JSON/UTF8 实现的脏检查稳定性测试（无数据库依赖）。
/// </summary>
/// <remarks>
/// C171: dirty-check stability tests for the default JSON/UTF8 implementation of
/// BaseCacheState.ToBytes (no database dependency).
/// </remarks>
public sealed class BaseCacheStateToBytesTests
{
    /// <summary>
    /// 同一对象两次调用 ToBytes，字节必须一致，否则 StateHash 脏检查会误报变更。
    /// </summary>
    [Fact]
    public void ToBytes_SameInstanceCalledTwice_ShouldReturnIdenticalBytes()
    {
        var state = new SampleToBytesState
        {
            Id = 42,
            Name = "dirty-check",
        };
        state.Flags["alpha"] = 1;
        state.Flags["beta"] = 2;
        state.Items.Add(1);
        state.Items.Add(2);
        state.Items.Add(3);

        var first = state.ToBytes();
        var second = state.ToBytes();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// ToBytes 输出必须是可解析的 UTF8 JSON。
    /// </summary>
    [Fact]
    public void ToBytes_Default_ShouldBeUtf8Json()
    {
        var state = new SampleToBytesState
        {
            Id = 7,
            Name = "json-probe",
        };

        var json = Encoding.UTF8.GetString(state.ToBytes());

        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("json-probe", document.RootElement.GetProperty("Name").GetString());
    }

    /// <summary>
    /// ToBytes 测试实体。
    /// </summary>
    private sealed class SampleToBytesState : BaseCacheState
    {
        /// <summary>
        /// 名称字段。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 字典字段。
        /// </summary>
        public Dictionary<string, int> Flags { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 列表字段。
        /// </summary>
        public List<long> Items { get; set; } = new List<long>();
    }
}
