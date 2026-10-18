// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB 仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.IO;
using GameFrameX.ProtoBuf.Net;
using ProtoBuf;
using Xunit;

namespace GameFrameX.Tests.ProtoBuff;

/// <summary>
/// ProtoReader 读侧递归深度上限单元测试（C272）/ ProtoReader read-side recursion depth limit unit tests (C272)
/// </summary>
/// <remarks>
/// 载荷手工构造（tag 0x0A = field 1 / wire type 2 逐层包裹空子消息），不依赖写侧序列化。
/// 边界语义：<see cref="ProtoReader.StartSubItem"/> 在 depth &gt;= <see cref="ProtoReader.MaxDepth"/> 时抛
/// <see cref="ProtoException"/>——第 N 层子消息进入时 depth 为 N-1，故恰 MaxDepth 层嵌套合法、
/// MaxDepth+1 层拒绝。全程使用缺省上限（100），不改进程级静态值（xUnit 并行下避免跨类污染）。
/// </remarks>
public class ProtoReaderDepthLimitTests
{
    /// <summary>
    /// 递归嵌套契约类型（field 1 自引用）。
    /// </summary>
    [ProtoContract]
    private sealed class RecursivePayload
    {
        [ProtoMember(1)]
        public RecursivePayload Inner { get; set; }
    }

    /// <summary>
    /// 构造 depth 层嵌套的空子消息载荷（自内向外包裹：每层 tag(0x0A) + varint(长度) + 内层）。
    /// </summary>
    private static byte[] BuildNestedPayload(int depth)
    {
        var payload = Array.Empty<byte>();
        for (var i = 0; i < depth; i++)
        {
            var length = payload.Length;
            var varintSize = 1;
            var remaining = length;
            while (remaining >= 0x80)
            {
                varintSize++;
                remaining >>= 7;
            }

            var buffer = new byte[1 + varintSize + length];
            buffer[0] = 0x0A; // field 1, wire type 2 (length-delimited)
            var index = 1;
            remaining = length;
            while (remaining >= 0x80)
            {
                buffer[index++] = (byte)(remaining | 0x80);
                remaining >>= 7;
            }

            buffer[index] = (byte)remaining;
            Buffer.BlockCopy(payload, 0, buffer, 1 + varintSize, length);
            payload = buffer;
        }

        return payload;
    }

    /// <summary>
    /// 深度远超 MaxDepth 的恶意载荷应抛 ProtoException（读侧深度上限快速失败，防 StackOverflow 打崩进程）。
    /// </summary>
    [Fact]
    public void Deserialize_BeyondMaxDepth_ShouldThrowProtoException()
    {
        var payload = BuildNestedPayload(ProtoReader.MaxDepth + 50);

        var exception = Assert.Throws<ProtoException>(() => Serializer.Deserialize<RecursivePayload>(new MemoryStream(payload)));
        Assert.Contains("depth", exception.Message);
    }

    /// <summary>
    /// 恰好 MaxDepth 层嵌套应正常解包（上限边界含端点，合法消息零误伤）。
    /// </summary>
    [Fact]
    public void Deserialize_ExactlyMaxDepth_ShouldSucceed()
    {
        var payload = BuildNestedPayload(ProtoReader.MaxDepth);

        var result = Serializer.Deserialize<RecursivePayload>(new MemoryStream(payload));

        Assert.NotNull(result);
        var hops = 0;
        var cursor = result;
        while (cursor.Inner != null)
        {
            cursor = cursor.Inner;
            hops++;
        }

        Assert.Equal(ProtoReader.MaxDepth, hops);
    }

    /// <summary>
    /// MaxDepth+1 层嵌套即被拒绝（边界外一点快速失败）。
    /// </summary>
    [Fact]
    public void Deserialize_OneBeyondMaxDepth_ShouldThrow()
    {
        var payload = BuildNestedPayload(ProtoReader.MaxDepth + 1);

        Assert.Throws<ProtoException>(() => Serializer.Deserialize<RecursivePayload>(new MemoryStream(payload)));
    }

    /// <summary>
    /// 浅嵌套正常解包（常规消息不受上限影响；10 层 → 根对象下钻 10 次到空）。
    /// </summary>
    [Fact]
    public void Deserialize_ShallowNesting_ShouldSucceed()
    {
        var payload = BuildNestedPayload(10);

        var result = Serializer.Deserialize<RecursivePayload>(new MemoryStream(payload));

        Assert.NotNull(result);
        var hops = 0;
        var cursor = result;
        while (cursor.Inner != null)
        {
            cursor = cursor.Inner;
            hops++;
        }

        Assert.Equal(10, hops);
    }
}
