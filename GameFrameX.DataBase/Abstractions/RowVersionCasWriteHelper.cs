// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
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
using System.Threading.Tasks;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Orm.Entity;
using GameFrameX.Localization;

namespace GameFrameX.DataBase.Abstractions;

/// <summary>
/// CAS 条件写（<see cref="IDatabaseService.UpdateIfRowVersionAsync{TState}(long, long, TState)"/>）
/// 各 Provider 共享的内存侧管线助手。
/// </summary>
/// <remarks>
/// Shared memory-side pipeline for the CAS conditional-write primitive across providers:
/// id consistency check, version preparation (align to the expected version + checked
/// increment), pre-write byte snapshot, failure rollback and exact success commit. Provider
/// implementations keep only their store-specific write operation (predicate, statement,
/// retry shell), supplied as the write delegate.
/// </remarks>
public static class RowVersionCasWriteHelper
{
    /// <summary>
    /// 执行 CAS 条件写的共享内存管线：Id 一致性校验 → 版本准备（对齐期望版本 + checked 递增）→
    /// 写库前字节快照 → Provider 写库操作 → 失败回滚内存版本 / 成功按快照精确提交基线。
    /// </summary>
    /// <remarks>
    /// Runs the shared memory-side pipeline for the CAS conditional write: id consistency
    /// check, version preparation (align to the expected version + checked increment), pre-write
    /// byte snapshot, the provider write operation, then failure rollback / exact success commit.
    /// The write delegate observes the state with its row version already bumped, and the byte
    /// snapshot commit contract matches the provider-owned write paths.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型，必须继承自 BaseCacheState / The cache state type, must inherit from BaseCacheState</typeparam>
    /// <param name="id">数据 ID（须与 <paramref name="state"/> 的 Id 一致）/ The document id (must equal <paramref name="state"/>'s Id)</param>
    /// <param name="expectedRowVersion">期望的存量行版本号 / The expected stored row version</param>
    /// <param name="state">要写入的数据对象 / The data object to write</param>
    /// <param name="writeOperation">Provider 写库操作，写入成功返回 true / The provider write operation; true when written</param>
    /// <returns>成功写入返回 true；版本不符或行缺失返回 false / true when written; false on version mismatch or missing row</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出（此时版本未变异）。
    /// Thrown when <paramref name="state"/>'s Id differs from <paramref name="id"/> (the version is untouched).
    /// </exception>
    /// <exception cref="OverflowException">
    /// <paramref name="expectedRowVersion"/> 为 long.MaxValue 递增溢出时抛出（此情况不写库）。
    /// Thrown when incrementing <paramref name="expectedRowVersion"/> == long.MaxValue overflows (nothing is written).
    /// </exception>
    public static async Task<bool> WriteAsync<TState>(long id, long expectedRowVersion, TState state, Func<Task<bool>> writeOperation) where TState : BaseCacheState
    {
        if (state.Id != id)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Database.ConditionalWriteStateIdMismatch, state.Id, id), nameof(state));
        }

        // 版本准备：先对齐到期望版本，再吃 checked 递增语义（long.MaxValue 在写库前抛出）。失败（false）路径
        // 回滚内存版本——写前递增若不回滚会污染调用方 state.RowVersion，后续 CAS 持续 Stale；异常路径不回滚
        // （写结果未知，回滚可能反向脱节）。
        // Version preparation: align to the expected version first, then take the checked increment (long.MaxValue
        // overflows before any write). The pre-write increment is rolled back on the false path — leaving it in
        // place would pollute the caller's state.RowVersion and starve every later CAS; it is NOT rolled back on
        // exceptions (the write outcome is unknown, and rolling back could desync the other way).
        var originalRowVersion = state.RowVersion;
        state.RowVersion = expectedRowVersion;
        state.IncrementRowVersion();

        // 写库前捕获字节快照（行版本递增后），成功后按该快照精确提交基线。
        // Capture the byte snapshot before the write (after the version bump); on success the
        // baseline is committed exactly against this snapshot.
        var persistedData = state.ToBytes();

        var written = await writeOperation().ConfigureAwait(false);
        if (!written)
        {
            // 版本不符或行缺失：未写库，回滚写前递增的内存版本。
            // Version mismatch or missing row: nothing was written — roll back the pre-write in-memory increment.
            state.RowVersion = originalRowVersion;
            return false;
        }

        state.SaveToDbPostHandler(persistedData);
        return true;
    }
}
