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

using GameFrameX.Core.Config;
using Xunit;

namespace GameFrameX.Tests.Config;

/// <summary>
/// ReadSegment 损坏段长回归单测（GFX-1178 / CR-M08）：
/// 段长来自数据本身，三个多字节头分支必须在索引访问前完成可读范围校验——
/// 声明超大段但数据截断时抛 <see cref="SerializationException"/>，
/// 而非旧实现先以越界 endPos 索引抛 <see cref="IndexOutOfRangeException"/>。
/// </summary>
public class ByteBufReadSegmentTests
{
    [Fact]
    public void ReadSegment_TruncatedTwoByteHeader_ThrowsSerializationException()
    {
        // h = 0x81 → 两字节头分支；声明段长 (1 &lt;&lt; 8) | 0xFF = 511，实际可读仅 2 字节
        var buffer = new ByteBuf(new byte[] { 0x81, 0xFF });

        var exception = Assert.Throws<SerializationException>(() => buffer.ReadSegment(out _, out _));
        Assert.NotNull(exception.Message);
    }

    [Fact]
    public void ReadSegment_TruncatedThreeByteHeader_ThrowsSerializationException()
    {
        // h = 0xC1 → 三字节头分支；声明段长 (1 &lt;&lt; 16) | 0xFFFF = 131071，实际可读仅 3 字节
        var buffer = new ByteBuf(new byte[] { 0xC1, 0xFF, 0xFF });

        Assert.Throws<SerializationException>(() => buffer.ReadSegment(out _, out _));
    }

    [Fact]
    public void ReadSegment_TruncatedFourByteHeader_ThrowsSerializationException()
    {
        // h = 0xE1 → 四字节头分支；声明段长 (1 &lt;&lt; 24) | 0xFFFFFF，实际可读仅 4 字节
        var buffer = new ByteBuf(new byte[] { 0xE1, 0xFF, 0xFF, 0xFF });

        Assert.Throws<SerializationException>(() => buffer.ReadSegment(out _, out _));
    }

    [Fact]
    public void ReadSegment_TruncatedSingleByteSegment_ThrowsSerializationException()
    {
        // h = 0x05 → 单字节段分支（无额外索引访问），依赖尾部 ReaderIndex 断言
        var buffer = new ByteBuf(new byte[] { 0x05 });

        Assert.Throws<SerializationException>(() => buffer.ReadSegment(out _, out _));
    }

    [Fact]
    public void ReadSegment_ExactFit_DoesNotThrow()
    {
        // h = 0x80, 段长 1：头(1) + 段数据(1) + 尾部搬运字节(1) 恰好填满 3 字节可读区
        var buffer = new ByteBuf(new byte[] { 0x80, 0x01, 0x2A });

        buffer.ReadSegment(out var startIndex, out var segmentSize);

        Assert.Equal(1, segmentSize);
        Assert.Equal(1, startIndex);
    }
}
