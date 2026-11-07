using System.Buffers;
using System.Reflection;
using GameFrameX.Core.Session;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Messages;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Core.Session;

[Collection(nameof(LocalPlayerMessageSenderCollection))]
public sealed class LocalPlayerMessageSenderTests : IDisposable
{
    private const long PlayerIdentifier = 940001;
    private readonly IMessageEncoderHandler _previousEncoder = MessageHelper.EncoderHandler;
    private readonly RecordingMessageEncoder _encoder = new();

    public LocalPlayerMessageSenderTests()
    {
        MessageHelper.SetMessageEncoderHandler(_encoder, null);
    }

    public void Dispose()
    {
        // 恢复包括未初始化状态在内的静态值，不改变其他网络测试的编码器配置。
        typeof(MessageHelper).GetProperty(nameof(MessageHelper.EncoderHandler), BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, _previousEncoder);
    }

    [Fact]
    public async Task SendAsync_ConnectedPlayer_ShouldWriteActualChannelAndReturnDelivered()
    {
        var networkSender = new RecordingNetworkSender();
        using (var channel = new LocalNetworkChannel(new ConnectedSession(), networkSender))
        {
            var manager = CreateManager(channel);
            var message = new PlayerNotification();
            var result = await new LocalPlayerMessageSender(manager).SendAsync(PlayerIdentifier, message);

            Assert.Equal(PlayerSendStatus.LocalDelivered, result.Status);
            Assert.True(result.IsSuccess);
            Assert.Equal(PlayerIdentifier, result.PlayerId);
            Assert.Null(result.ErrorMessage);
            Assert.Same(message, _encoder.LastMessage);
            Assert.Equal(_encoder.Payload, networkSender.LastPayload);
            Assert.Equal(1, networkSender.WriteCount);
            Assert.Equal(1UL, channel.SendPacketLength);
            Assert.Equal((ulong)_encoder.Payload.Length, channel.SendBytesLength);
            Assert.Throws<ObjectDisposedException>(() => networkSender.LastCancellationToken.WaitHandle);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BaseNetworkChannel_WriteAsync_UnavailableConnection_ShouldThrowWithoutSending(bool closeChannel)
    {
        var networkSender = new RecordingNetworkSender();
        var connection = new ConnectedSession { IsConnected = closeChannel };
        using (var channel = new LocalNetworkChannel(connection, networkSender))
        {
            if (closeChannel)
            {
                channel.Close();
            }

            var exception = await Assert.ThrowsAsync<System.Net.Sockets.SocketException>(() => channel.WriteAsync(new PlayerNotification()));

            Assert.Equal(System.Net.Sockets.SocketError.NotConnected, exception.SocketErrorCode);
            Assert.Equal(0, networkSender.WriteCount);
            Assert.Equal(0UL, channel.SendPacketLength);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BaseNetworkChannel_WriteAsync_TransportFailure_ShouldPreserveExceptionAndDisposeTimeoutSource(bool cancellation)
    {
        Exception failure = cancellation
            ? new OperationCanceledException("transport cancelled")
            : new InvalidOperationException("transport unavailable");
        var networkSender = new RecordingNetworkSender
        {
            WriteOperation = () => Task.FromException(failure),
        };
        using (var channel = new LocalNetworkChannel(new ConnectedSession(), networkSender))
        {
            var observed = await Record.ExceptionAsync(() => channel.WriteAsync(new PlayerNotification()));

            Assert.Same(failure, observed);
            Assert.Equal(1, networkSender.WriteCount);
            Assert.Throws<ObjectDisposedException>(() => networkSender.LastCancellationToken.WaitHandle);
            Assert.False(channel.IsClosed());
        }
    }

    [Fact]
    public async Task SendAsync_MissingPlayer_ShouldReturnOfflineWithoutWriting()
    {
        var result = await new LocalPlayerMessageSender(new PlayerSessionManager())
            .SendAsync(PlayerIdentifier, new PlayerNotification());

        Assert.Equal(PlayerSendStatus.Offline, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(PlayerIdentifier, result.PlayerId);
        Assert.Null(_encoder.LastMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendAsync_ClosedOrDisconnectedPlayer_ShouldReturnOfflineWithoutWriting(bool closeChannel)
    {
        var networkSender = new RecordingNetworkSender();
        var connection = new ConnectedSession { IsConnected = closeChannel };
        using (var channel = new LocalNetworkChannel(connection, networkSender))
        {
            var manager = CreateManager(channel);
            if (closeChannel)
            {
                channel.Close();
            }

            var result = await new LocalPlayerMessageSender(manager).SendAsync(PlayerIdentifier, new PlayerNotification());

            Assert.Equal(PlayerSendStatus.Offline, result.Status);
            Assert.False(result.IsSuccess);
            Assert.Equal(0, networkSender.WriteCount);
        }
    }

    [Fact]
    public async Task SendAsync_ActualChannelWriteFails_ShouldReturnFailed()
    {
        var networkSender = new RecordingNetworkSender
        {
            WriteOperation = () => Task.FromException(new InvalidOperationException("transport unavailable")),
        };
        using (var channel = new LocalNetworkChannel(new ConnectedSession(), networkSender))
        {
            var result = await new LocalPlayerMessageSender(CreateManager(channel))
                .SendAsync(PlayerIdentifier, new PlayerNotification());

            Assert.Equal(PlayerSendStatus.Failed, result.Status);
            Assert.False(result.IsSuccess);
            Assert.Equal("transport unavailable", result.ErrorMessage);
            Assert.Equal(1, networkSender.WriteCount);
        }
    }

    [Fact]
    public async Task SendAsync_ActualChannelWriteIsCancelled_ShouldPropagateCancellation()
    {
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var networkSender = new RecordingNetworkSender
            {
                WriteOperation = () => Task.FromCanceled(cancellation.Token),
            };
            using (var channel = new LocalNetworkChannel(new ConnectedSession(), networkSender))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    new LocalPlayerMessageSender(CreateManager(channel)).SendAsync(PlayerIdentifier, new PlayerNotification()));
                Assert.Equal(1, networkSender.WriteCount);
            }
        }
    }

    [Fact]
    public async Task SendAsync_CancelledBeforeSending_ShouldNotWrite()
    {
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var networkSender = new RecordingNetworkSender();
            using (var channel = new LocalNetworkChannel(new ConnectedSession(), networkSender))
            {
                var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    new LocalPlayerMessageSender(CreateManager(channel))
                        .SendAsync(PlayerIdentifier, new PlayerNotification(), cancellation.Token));

                Assert.Equal(cancellation.Token, exception.CancellationToken);
                Assert.Equal(0, networkSender.WriteCount);
            }
        }
    }

    [Fact]
    public async Task SendAsync_CancelledDuringWrite_ShouldCancelWaitingWithoutReportingSuccess()
    {
        var writeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var networkSender = new RecordingNetworkSender
        {
            WriteOperation = async () =>
            {
                writeEntered.SetResult();
                await writeRelease.Task;
                writeFinished.SetResult();
            },
        };
        using (var cancellation = new CancellationTokenSource())
        {
            using (var channel = new LocalNetworkChannel(new ConnectedSession(), networkSender))
            {
                var sending = new LocalPlayerMessageSender(CreateManager(channel))
                    .SendAsync(PlayerIdentifier, new PlayerNotification(), cancellation.Token);
                try
                {
                    await writeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.False(sending.IsCompleted);
                    cancellation.Cancel();
                    var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending.WaitAsync(TimeSpan.FromSeconds(10)));
                    Assert.Equal(cancellation.Token, exception.CancellationToken);
                    Assert.Equal(1, networkSender.WriteCount);
                }
                finally
                {
                    // 通道无取消参数：释放已开始的写入，验证测试不会遗留后台任务。
                    writeRelease.TrySetResult();
                    await writeFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
        }
    }

    [Fact]
    public async Task SendAsync_UnsupportedMessage_ShouldFailWithoutWriting()
    {
        var networkSender = new RecordingNetworkSender();
        using (var channel = new LocalNetworkChannel(new ConnectedSession(), networkSender))
        {
            var result = await new LocalPlayerMessageSender(CreateManager(channel))
                .SendAsync(PlayerIdentifier, new NonNetworkMessage());

            Assert.Equal(PlayerSendStatus.Failed, result.Status);
            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.ErrorMessage);
            Assert.Equal(0, networkSender.WriteCount);
        }
    }

    [Fact]
    public async Task SendAsync_NullMessage_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new LocalPlayerMessageSender(new PlayerSessionManager()).SendAsync(PlayerIdentifier, null));
    }

    [Fact]
    public void Constructor_NullManager_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new LocalPlayerMessageSender(null));
    }

    private static PlayerSessionManager CreateManager(INetworkChannel channel)
    {
        var manager = new PlayerSessionManager();
        var session = new PlayerSession("local-player-message-test", channel);
        session.SetPlayerId(PlayerIdentifier);
        manager.Add(session);
        return manager;
    }

    private sealed class PlayerNotification : MessageObject
    {
        public override void Clear()
        {
        }
    }

    private sealed class NonNetworkMessage : IMessage
    {
        public void Clear()
        {
        }

        public string ToFormatMessageString(long actorId = default)
        {
            return nameof(NonNetworkMessage);
        }
    }

    private sealed class RecordingMessageEncoder : IMessageEncoderHandler
    {
        public byte[] Payload { get; } = [17, 23, 42];
        public IMessage LastMessage { get; private set; }
        public uint LimitCompressLength => uint.MaxValue;
        public ushort PackageHeaderLength => 0;

        public byte[] Handler(IMessage message)
        {
            LastMessage = message;
            return Payload;
        }

        public void SetCompressionHandler(IMessageCompressHandler compressHandler = null)
        {
        }
    }

    private sealed class LocalNetworkChannel : BaseNetworkChannel
    {
        public LocalNetworkChannel(IGameAppSession session, INetworkSender sender)
            : base(session, new AppSetting { NetworkSendTimeOutSeconds = 30 }, sender)
        {
        }
    }

    private sealed class RecordingNetworkSender : INetworkSender
    {
        public byte[] LastPayload { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public int WriteCount { get; private set; }
        public Func<Task> WriteOperation { get; init; } = () => Task.CompletedTask;

        public ValueTask SendAsync(byte[] messageData, CancellationToken cancellationToken)
        {
            return SendAsync(messageData.AsMemory(), cancellationToken);
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> messageData, CancellationToken cancellationToken)
        {
            WriteCount++;
            LastPayload = messageData.ToArray();
            LastCancellationToken = cancellationToken;
            return new ValueTask(WriteOperation());
        }
    }

    private sealed class ConnectedSession : IGameAppSession
    {
        public string SessionId => "connected-local-player";
        public bool IsConnected { get; set; } = true;

        public ValueTask SendAsync(byte[] messageData, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> messageData, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask SendAsync(ReadOnlySequence<byte> messageData, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
