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
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Buffers;
using System.IO.Pipelines;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// KCP 会话的双工流适配：读侧供给 KCP 重组后的有序字节流（长度前缀帧语义与 TCP 等价），
/// 写侧执行单帧上限守卫（超限显式 IOException，见 <see cref="WriteAsync"/>）。
/// </summary>
/// <remarks>
/// Duplex stream adaptation of a KCP session: the read side serves the ordered byte stream
/// reassembled by KCP (length-prefixed framing equivalent to the TCP path), and the write side
/// enforces the per-frame bound (an explicit IOException beyond it, see <see cref="WriteAsync"/>).
/// <para>契约对齐 <see cref="System.IO.Stream"/> 消费方（RemoteMessageClient 接收泵与写门控）：
/// 一次 <see cref="WriteAsync"/> = 一条完整 RemoteMessage 帧（上层写门控保证帧间不交错），
/// 读到 0 字节 = 会话关闭。</para>
/// </remarks>
internal sealed class KcpStream : Stream
{
    private readonly KcpClientSession _session;
    private readonly System.IO.Pipelines.PipeReader _reader;

    /// <summary>
    /// 初始化流适配。
    /// </summary>
    /// <remarks>
    /// Initializes the stream adaptation.
    /// </remarks>
    /// <param name="session">承载会话 / The owning session</param>
    internal KcpStream(KcpClientSession session)
    {
        _session = session;
        _reader = session.Reader;
    }

    /// <summary>
    /// 获取本流支持读取。
    /// </summary>
    public override bool CanRead
    {
        get { return true; }
    }

    /// <summary>
    /// 获取本流支持写入。
    /// </summary>
    public override bool CanWrite
    {
        get { return true; }
    }

    /// <summary>
    /// 获取本流不支持定位。
    /// </summary>
    public override bool CanSeek
    {
        get { return false; }
    }

    /// <summary>
    /// 不支持定位，抛出 <see cref="NotSupportedException"/>。
    /// </summary>
    public override long Length
    {
        get { throw new NotSupportedException(); }
    }

    /// <summary>
    /// 不支持定位，抛出 <see cref="NotSupportedException"/>。
    /// </summary>
    public override long Position
    {
        get { throw new NotSupportedException(); }
        set { throw new NotSupportedException(); }
    }

    /// <summary>
    /// 从 KCP 重组字节流中顺序读取：拷贝当前管道内可用字节；管道已完结时返回 0（会话关闭语义）。
    /// </summary>
    /// <remarks>
    /// Reads sequentially from the KCP-reassembled byte stream: copies whatever bytes are
    /// currently available in the pipe; returns 0 once the pipe is completed (session-closed semantics).
    /// <para>「空结果且未完结」分支（上游管道无数据返回空缓冲）必须 <see cref="Task.Yield"/> 让出后再等：
    /// 同步重入 <see cref="System.IO.Pipelines.PipeReader.ReadAsync"/> 会形成紧环忙等；
    /// 循环顶的取消检查覆盖管道实现不消费取消令牌的形态，保证读侧可被取消终止。</para>
    /// </remarks>
    /// <param name="buffer">目标缓冲 / The target buffer</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>读取的字节数；0 表示流结束 / Bytes read; 0 means end of stream</returns>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            // 循环顶检查取消：下游管道读不保证消费令牌时，读侧仍可被取消终止（防「空+未完结」环空转）
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _reader.ReadAsync(cancellationToken);
            var sequence = result.Buffer;
            var bytesToCopy = (int)Math.Min(buffer.Length, sequence.Length);
            if (bytesToCopy > 0)
            {
                sequence.Slice(0, bytesToCopy).CopyTo(buffer.Span);
                // 仅推进 consumed 到已拷贝位置（examined 缺省 = consumed）：剩余字节保持「未检视」，
                // 下次 ReadAsync 立即返回；若把 examined 推到 End，管道会等待新写入而挂起
                _reader.AdvanceTo(sequence.GetPosition(bytesToCopy));
                return bytesToCopy;
            }

            _reader.AdvanceTo(sequence.Start);
            if (result.IsCompleted || buffer.Length == 0)
            {
                return 0;
            }

            // 空结果且未完结（防御分支）：先让出时间片再等待后续字节，避免同步重入忙等
            await Task.Yield();
        }
    }

    /// <summary>
    /// 写入一段载荷（一条完整帧）：单帧超过 <see cref="KcpClientSession.MaxFrameBytes"/> 时显式抛出
    /// <see cref="IOException"/>（内核多段吞吐病态边界——超限帧分钟级爬行，宁可快速失败，
    /// 由 RPC 层按连接级失败处理；更大帧应声明 tcp 端点）。
    /// </summary>
    /// <remarks>
    /// Writes a payload (one complete frame): frames larger than
    /// <see cref="KcpClientSession.MaxFrameBytes"/> throw <see cref="IOException"/> explicitly —
    /// beyond the bound the kernel's multi-segment throughput pathology (minutes-scale crawling,
    /// measured in notes.md) makes a fast failure strictly better than a silent crawl; the RPC layer
    /// treats it as a connection-level failure. Larger frames should declare a tcp endpoint instead.
    /// </remarks>
    /// <param name="buffer">载荷（一条完整帧）/ The payload (one complete frame)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <exception cref="IOException">帧超过 KCP 单帧上限时抛出 / Thrown when the frame exceeds the KCP per-frame bound</exception>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length > KcpClientSession.MaxFrameBytes)
        {
            // Localization: RemoteMessaging.Transport.KcpFrameSizeExceeded - KCP 传输单帧上限 {1} 字节，本次帧 {0} 字节；更大载荷请使用 tcp 端点
            throw new IOException(LocalizationService.GetString(Localization.Keys.RemoteMessaging.Transport.KcpFrameSizeExceeded, buffer.Length, KcpClientSession.MaxFrameBytes));
        }

        return _session.SendChunkAsync(buffer, cancellationToken);
    }

    /// <summary>
    /// KCP 发送即时入队，刷新为空操作。
    /// </summary>
    public override void Flush()
    {
    }

    /// <summary>
    /// KCP 发送即时入队，刷新为空操作。
    /// </summary>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 同步读：委托异步实现（接收泵走异步面，本重载仅为基类契约完备）。
    /// </summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 同步写：委托异步实现（写门控走异步面，本重载仅为基类契约完备）。
    /// </summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 不支持定位，抛出 <see cref="NotSupportedException"/>。
    /// </summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 不支持设置长度，抛出 <see cref="NotSupportedException"/>。
    /// </summary>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 释放流：关闭承载会话（连接与独占 Socket 随之释放）。
    /// </summary>
    /// <remarks>
    /// Disposes the stream: closes the owning session (the connection and its dedicated socket
    /// are released with it).
    /// </remarks>
    /// <param name="disposing">是否显式释放 / Whether this is an explicit dispose</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.Dispose();
        }

        base.Dispose(disposing);
    }
}
