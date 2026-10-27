//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Foundation.Session;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 会话写入共享串行门（<see cref="OnlineSessionManager"/> 与 <see cref="OnlineSessionTokenService"/>
/// 的全部会话变更统一排队，防止两个写入方的「读—改—全量写」并发互相覆盖——GFX-1091）。
/// <para>
/// ponytail: 单进程串行门——多进程部署时依赖装配层按会话归属单写者路由，跨进程原子性由持久化存储的 CAS 实现。
/// </para>
/// </summary>
public sealed class OnlineSessionWriteGate
{
    /// <summary>串行信号量。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 获取门（用毕必须 <see cref="Release"/>；门内再入其它会话变更面会死锁，须走门内核心面）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>获取完成。</returns>
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        return _gate.WaitAsync(cancellationToken);
    }

    /// <summary>释放门。</summary>
    public void Release()
    {
        _gate.Release();
    }

    /// <summary>
    /// 在门内执行变更操作（同一时刻仅一个会话变更在途）。
    /// </summary>
    /// <typeparam name="T">操作结果类型。</typeparam>
    /// <param name="operation">变更操作（约定：内部完成读取—变更—回写全周期）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (operation == null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
