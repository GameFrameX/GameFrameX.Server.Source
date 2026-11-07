using Xunit;

namespace GameFrameX.Tests.Core.Session;

/// <summary>真实通道写入测试会暂时替换静态消息编码器，必须与其他测试隔离。</summary>
[CollectionDefinition(nameof(LocalPlayerMessageSenderCollection), DisableParallelization = true)]
public sealed class LocalPlayerMessageSenderCollection
{
}
