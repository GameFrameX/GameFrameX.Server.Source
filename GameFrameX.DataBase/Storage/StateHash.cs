// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using GameFrameX.Foundation.Hash;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using Standart.Hash.xxHash;

namespace GameFrameX.DataBase.Storage;

/// <summary>
/// 数据状态Hash计算处理器。
/// </summary>
/// <remarks>
/// Data state hash calculation processor.
/// </remarks>
internal sealed class StateHash
{
    /// <summary>
    /// 初始化 <see cref="StateHash"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// Initialize a new instance of the <see cref="StateHash"/> class.
    /// </remarks>
    /// <param name="state">缓存状态对象 / Cache state object</param>
    /// <param name="isNew">是否是新创建的实例 / Whether it is a newly created instance</param>
    public StateHash(BaseCacheState state, bool isNew = false)
    {
        State = state;
        if (!isNew)
        {
            CacheHash = GetHashAndData(state).md5;
        }
    }

    /// <summary>
    /// 缓存状态对象。
    /// </summary>
    /// <remarks>
    /// Cache state object.
    /// </remarks>
    private BaseCacheState State { get; }

    /// <summary>
    /// 缓存的Hash值（最近一次确认已落库的字节快照的Hash）。
    /// </summary>
    /// <remarks>
    /// Cached hash value (the hash of the byte snapshot most recently confirmed as persisted).
    /// 只能通过 <see cref="SaveToDbPostHandler(byte[])"/>（实际送库快照）或参数less 提交推进；
    /// 推进到「从未持久化的字节」的 hash 会让后续变更被误判为未修改（静默丢数据，C263）。
    /// It may only advance via <see cref="SaveToDbPostHandler(byte[])"/> (the actually persisted snapshot)
    /// or the parameterless commit; advancing it to the hash of never-persisted bytes makes later
    /// mutations read as unmodified (silent data loss, C263).
    /// </remarks>
    private uint128 CacheHash { get; set; }

    /// <summary>
    /// 待保存的Hash值（最近一次 IsChanged 序列化快照的Hash）。
    /// </summary>
    /// <remarks>
    /// Hash value to save (the hash of the snapshot serialized by the most recent IsChanged call).
    /// </remarks>
    private uint128 ToSaveHash { get; set; }

    /// <summary>
    /// 守护 CacheHash/ToSaveHash 迁移的锁：脏检查与回存后置处理可能来自不同线程
    /// （Actor 线程 IsModify / saver 线程批量回存），uint128 为两个 64 位字段，
    /// 无锁时存在撕裂读与重排序；序列化等长耗时操作不进入该锁。
    /// </summary>
    /// <remarks>
    /// Guard for CacheHash/ToSaveHash transitions: dirty checks and save post-handlers may run on
    /// different threads (actor-thread IsModify / saver-thread bulk save); uint128 is two 64-bit
    /// fields, so unsynchronized access risks torn reads and reordering. Long operations such as
    /// serialization stay outside this lock.
    /// </remarks>
    private readonly object _hashSync = new();

    /// <summary>
    /// 判断是否需要保存。
    /// </summary>
    /// <remarks>
    /// Determine if save is needed.
    /// </remarks>
    /// <returns>包含是否需要保存和字节数据的元组 / Tuple containing whether save is needed and byte data</returns>
    public (bool, byte[]) IsChanged()
    {
        var (toSaveHash, data) = GetHashAndData(State);
        lock (_hashSync)
        {
            ToSaveHash = toSaveHash;
            return (XxHashHelper.IsDefault(CacheHash) || !(toSaveHash.high64 == CacheHash.high64 && toSaveHash.low64 == CacheHash.low64), data);
        }
    }

    /// <summary>
    /// 保存到数据库之后的操作（精确提交：以实际送库的字节快照为准推进基线）。
    /// </summary>
    /// <remarks>
    /// Post-save operation (exact commit: advances the baseline with the byte snapshot actually
    /// handed to the database). 检查（IsChanged）与写库之间若发生并发 IsChanged（如 Actor 反激活检查、
    /// 强制全量保存），ToSaveHash 已被推进到未落库快照；本重载不读 ToSaveHash，只提交调用方
    /// 送库快照的 hash，保证 CacheHash 永远指向真实持久化过的字节（C263 竞态修复）。
    /// If a concurrent IsChanged ran between the check and the database write (e.g. an actor
    /// deactivation check or a forced full save), ToSaveHash already advanced to an unpersisted
    /// snapshot; this overload ignores ToSaveHash and commits the hash of the snapshot the caller
    /// persisted, so CacheHash always points at bytes that were truly written (C263 race fix).
    /// </remarks>
    /// <param name="persistedData">实际送库的字节快照（来自 IsChanged/IsChangedWithId 返回值或写库前捕获的 ToBytes）/ The byte snapshot actually persisted (from IsChanged/IsChangedWithId or a ToBytes capture taken right before the write)</param>
    public void SaveToDbPostHandler(byte[] persistedData)
    {
        var persistedHash = XxHashHelper.Hash128(persistedData);
        lock (_hashSync)
        {
            if (persistedHash.high64 == CacheHash.high64 && persistedHash.low64 == CacheHash.low64)
            {
                return;
            }

            CacheHash = persistedHash;
        }
    }

    /// <summary>
    /// 保存到数据库之后的操作（兼容提交：以最近一次检查快照 ToSaveHash 为准）。
    /// </summary>
    /// <remarks>
    /// Post-save operation (compat commit: advances with ToSaveHash, the latest check snapshot).
    /// 供未持有送库快照的外部自定义保存流程使用；框架内写路径一律使用
    /// <see cref="SaveToDbPostHandler(byte[])"/> 精确提交，避免把 CacheHash 推进到
    /// 并发检查产生的未落库快照。
    /// For external custom save flows that do not hold the persisted snapshot; framework write
    /// paths always use the exact <see cref="SaveToDbPostHandler(byte[])"/> overload so CacheHash
    /// never advances to an unpersisted snapshot produced by a concurrent check.
    /// </remarks>
    public void SaveToDbPostHandler()
    {
        lock (_hashSync)
        {
            if (CacheHash.high64 == ToSaveHash.high64 && CacheHash.low64 == ToSaveHash.low64)
            {
                // Localization: Storage.CacheHashEquals - 调用AfterSaveToDB前CacheHash已经等于ToSaveHash {0}
                // LogHelper.Warning<string>("StateHash.SaveToDbPostHandler {cacheHashEquals}", LocalizationService.GetString(Localization.Keys.Storage.CacheHashEquals, State.GetType().FullName));
                return;
            }

            CacheHash = ToSaveHash;
        }
    }

    /// <summary>
    /// 获取状态对象的Hash值和字节数据。
    /// </summary>
    /// <remarks>
    /// Get the hash value and byte data of the state object.
    /// </remarks>
    /// <param name="state">缓存状态对象 / Cache state object</param>
    /// <returns>包含Hash值和字节数据的元组 / Tuple containing hash value and byte data</returns>
    private static (uint128 md5, byte[] data) GetHashAndData(BaseCacheState state)
    {
        var data = state.ToBytes();
        var uint128 = XxHashHelper.Hash128(data);
        return (uint128, data);
    }
}