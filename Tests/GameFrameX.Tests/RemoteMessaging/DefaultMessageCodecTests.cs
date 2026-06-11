// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   均受中华人民共和国及相关国际法律法规保护。
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   本项目采用 Apache License 2.0 单协议分发，
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   侵犯他人合法权益等法律法规所禁止的行为！
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Buffers.Binary;
using System.IO;
using GameFrameX.NetWork.RemoteMessaging.Transport;

namespace GameFrameX.Tests.RemoteMessaging;

public class DefaultMessageCodecTests
{
    [Fact]
    public async Task DecodeAsync_ShouldThrowInvalidDataException_WhenPacketLengthIsSmallerThanHeader()
    {
        var codec = CreateCodec(maxPacketSize: 64, maxDecompressedSize: 64);
        await using var stream = CreateLengthOnlyStream(13);

        await Assert.ThrowsAsync<InvalidDataException>(() => codec.DecodeAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_ShouldThrowInvalidDataException_BeforeReadingBody_WhenPacketLengthExceedsLimit()
    {
        var codec = CreateCodec(maxPacketSize: 64, maxDecompressedSize: 64);
        await using var stream = CreateLengthOnlyStream(65);

        await Assert.ThrowsAsync<InvalidDataException>(() => codec.DecodeAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_ShouldThrowInvalidDataException_WhenDecompressedPayloadExceedsLimit()
    {
        const byte algorithmId = 42;
        var registry = new DefaultMessageCompressionRegistry();
        registry.Register(new ExpandingCompressionAlgorithm(algorithmId, new byte[9]));
        var codec = CreateCodec(registry, maxPacketSize: 64, maxDecompressedSize: 8);
        await using var stream = CreatePacketStream(algorithmId, new byte[] { 1 });

        await Assert.ThrowsAsync<InvalidDataException>(() => codec.DecodeAsync(stream, CancellationToken.None));
    }

    private static IMessageCodec CreateCodec(int maxPacketSize, int maxDecompressedSize)
    {
        return CreateCodec(new DefaultMessageCompressionRegistry(), maxPacketSize, maxDecompressedSize);
    }

    private static IMessageCodec CreateCodec(
        IMessageCompressionRegistry registry,
        int maxPacketSize,
        int maxDecompressedSize)
    {
        var codecType = typeof(IMessageCodec).Assembly.GetType("GameFrameX.NetWork.RemoteMessaging.Transport.DefaultMessageCodec", throwOnError: true);
        return (IMessageCodec)Activator.CreateInstance(
            codecType!,
            registry,
            (byte)0,
            512,
            maxPacketSize,
            maxDecompressedSize)!;
    }

    private static MemoryStream CreateLengthOnlyStream(int totalLength)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, totalLength);
        return new MemoryStream(buffer);
    }

    private static MemoryStream CreatePacketStream(byte algorithmId, byte[] payload)
    {
        const int headerLength = 14;
        var totalLength = headerLength + payload.Length;
        var buffer = new byte[totalLength];
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), totalLength);
        buffer[4] = 0;
        buffer[5] = algorithmId;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(6, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(10, 4), 1);
        Buffer.BlockCopy(payload, 0, buffer, headerLength, payload.Length);
        return new MemoryStream(buffer);
    }

    private sealed class ExpandingCompressionAlgorithm : IMessageCompressionAlgorithm
    {
        private readonly byte[] _decompressedBytes;

        public ExpandingCompressionAlgorithm(byte algorithmId, byte[] decompressedBytes)
        {
            AlgorithmId = algorithmId;
            _decompressedBytes = decompressedBytes;
        }

        public byte AlgorithmId { get; }

        public byte[] Compress(byte[] input)
        {
            return input;
        }

        public byte[] Decompress(byte[] input)
        {
            return _decompressedBytes;
        }
    }
}
