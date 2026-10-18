// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯任何单位或个人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of any unit or individual, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer and the contributor.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Messages;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Network
{
    /// <summary>
    /// GFX-909 网络栈资源泄漏与消息池数据残留回归测试：
    /// ① 池归还后身份字段（MessageId / UniqueId）必须重置，消除复用串台；
    /// ② 通道 Close() 必须释放通道级 CTS 且幂等。
    /// </summary>
    public class NetworkResourceLeakRegressionTests
    {
        private sealed class LeakTestMessage : MessageObject
        {
            public int Value { get; set; }

            public override void Clear()
            {
                Value = default;
            }
        }

        private sealed class StubGameAppSession : IGameAppSession
        {
            public string SessionId { get; } = "leak-test-session";

            public bool IsConnected { get; } = true;

            public ValueTask SendAsync(byte[] messageData, CancellationToken cancellationToken)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask SendAsync(ReadOnlyMemory<byte> messageData, CancellationToken cancellationToken)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask SendAsync(System.Buffers.ReadOnlySequence<byte> messageData, CancellationToken cancellationToken)
            {
                return ValueTask.CompletedTask;
            }
        }

        private sealed class StubNetworkSender : INetworkSender
        {
            public ValueTask SendAsync(byte[] messageData, CancellationToken cancellationToken)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask SendAsync(ReadOnlyMemory<byte> messageData, CancellationToken cancellationToken)
            {
                return ValueTask.CompletedTask;
            }
        }

        private sealed class TestNetworkChannel : BaseNetworkChannel
        {
            public TestNetworkChannel()
                : base(new StubGameAppSession(), new AppSetting(), new StubNetworkSender())
            {
            }
        }

        /// <summary>
        /// 池归还后再次获取，MessageId 必须归零、UniqueId 必须重生成（≠ 归还前值），与 new T() 语义等价。
        /// </summary>
        [Fact]
        public void MessageObjectPool_ReturnResetsIdentityFields()
        {
            var message = MessageObjectPoolHelper.Get<LeakTestMessage>();
            message.Value = 42;
            message.SetMessageId(123456);
            var uniqueIdBeforeReturn = message.UniqueId;

            MessageObjectPoolHelper.Return(message);

            var reused = MessageObjectPoolHelper.Get<LeakTestMessage>();
            Assert.Same(message, reused);
            Assert.Equal(0, reused.MessageId);
            Assert.NotEqual(uniqueIdBeforeReturn, reused.UniqueId);
            Assert.Equal(0, reused.Value);
        }

        /// <summary>
        /// Close() 必须释放通道级 CTS（Cancel 后 Dispose）：对已释放 CTS 再 Cancel 抛 ObjectDisposedException；
        /// Close() 幂等，二次调用不抛且 IsClosed() 恒为 true。
        /// </summary>
        [Fact]
        public void BaseNetworkChannel_CloseDisposesCancellationTokenSourceAndIsIdempotent()
        {
            var channel = new TestNetworkChannel();
            var ctsField = typeof(BaseNetworkChannel).GetField("CancellationTokenSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(ctsField);
            var cts = (CancellationTokenSource)ctsField.GetValue(channel);

            channel.Close();

            Assert.True(channel.IsClosed());
            Assert.True(cts.IsCancellationRequested);
            Assert.Throws<ObjectDisposedException>(() => cts.Cancel());

            // 二次 Close 幂等：不得触碰已释放的 CTS
            channel.Close();
            Assert.True(channel.IsClosed());
        }
    }
}
