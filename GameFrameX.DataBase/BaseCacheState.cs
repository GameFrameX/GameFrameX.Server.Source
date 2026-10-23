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

using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Storage;
using GameFrameX.Foundation.Json;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Orm.Entity;

namespace GameFrameX.DataBase;

/// <summary>
/// 缓存数据对象。
/// </summary>
/// <remarks>
/// Cache data object base class.
/// </remarks>
public abstract class BaseCacheState : EntityBase<long>, ICacheState
{
    /// <summary>
    /// 获取数据对象是否修改。
    /// </summary>
    /// <remarks>
    /// Get whether the data object has been modified.
    /// </remarks>
    /// <returns>如果已修改则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if modified; otherwise <c>false</c></returns>
    public virtual bool IsModify()
    {
        return IsChanged().isChanged;
    }

    /// <summary>
    /// 转换为字符串, 包含所有属性。
    /// </summary>
    /// <remarks>
    /// Convert to string, including all properties.
    /// </remarks>
    /// <returns>对象的JSON字符串表示 / JSON string representation of the object</returns>
    public override string ToString()
    {
        return JsonHelper.Serialize(this);
    }

    #region hash

    private StateHash _stateHash;


    /// <summary>
    /// 用于在对象从数据库加载后进行一些特定的处理，如初始化数据或设置状态。
    /// </summary>
    /// <remarks>
    /// Used to perform specific processing after the object is loaded from the database, such as initializing data or setting state.
    /// </remarks>
    /// <param name="isNew">是否是新创建的实例，true表示是新创建的实例，false表示不是 / Whether it is a newly created instance, true means it is new, false means it is not</param>
    public virtual void LoadFromDbPostHandler(bool isNew = false)
    {
        _stateHash = new StateHash(this, isNew);
    }

    /// <summary>
    /// 是否修改。
    /// </summary>
    /// <remarks>
    /// Check if the object has been modified.
    /// </remarks>
    /// <returns>包含是否修改和字节数据的元组 / Tuple containing whether modified and byte data</returns>
    public virtual (bool isChanged, byte[] data) IsChanged()
    {
        CheckStateHash();
        return _stateHash.IsChanged();
    }

    /// <summary>
    /// 是否由ID引起的变化。
    /// </summary>
    /// <remarks>
    /// Check if the change was caused by ID.
    /// </remarks>
    /// <returns>包含是否修改、状态ID和字节数据的元组 / Tuple containing whether modified, state ID, and byte data</returns>
    public virtual (bool isChanged, long stateId, byte[] data) IsChangedWithId()
    {
        CheckStateHash();
        var res = _stateHash.IsChanged();
        return (res.Item1, Id, res.Item2);
    }

    /// <summary>
    /// 在对象保存到数据库后调用的方法（精确提交：以实际送库的字节快照为准推进脏检查基线）。
    /// </summary>
    /// <remarks>
    /// Method called after the object is saved to the database (exact commit: advances the
    /// dirty-check baseline with the byte snapshot actually handed to the database).
    /// 检查与写库之间可能发生并发脏检查（如 Actor 反激活的 IsModify、强制全量保存），
    /// 它们会把 ToSaveHash 推进到尚未落库的快照；参数less 重载在竞态窗口内会误把基线推进到
    /// 该未落库快照，导致后续变异被判定为未修改（静默丢数据）。本重载只提交
    /// <paramref name="persistedData"/> 的 hash，保证基线永远指向真实持久化过的字节。
    /// A concurrent dirty check between the check and the write (e.g. an actor-deactivation
    /// IsModify or a forced full save) advances ToSaveHash to a snapshot that was not persisted;
    /// within that race window the parameterless overload would wrongly baseline that unpersisted
    /// snapshot, making later mutations read as unmodified (silent data loss). This overload
    /// commits only the hash of <paramref name="persistedData"/>, so the baseline always points at
    /// bytes that were truly written.
    /// </remarks>
    /// <param name="persistedData">实际送库的字节快照（IsChanged/IsChangedWithId 的返回值，或写库前捕获的 ToBytes）/ The byte snapshot actually persisted (the value returned by IsChanged/IsChangedWithId, or a ToBytes capture taken right before the write)</param>
    public void SaveToDbPostHandler(byte[] persistedData)
    {
        CheckStateHash();
        _stateHash.SaveToDbPostHandler(persistedData);
    }

    /// <summary>
    /// 在对象保存到数据库后调用的方法，可以进行一些后续处理（兼容提交：以最近一次检查快照为准）。
    /// </summary>
    /// <remarks>
    /// Method called after the object is saved to the database, can be used for post-processing
    /// (compat commit: advances with the latest check snapshot).
    /// 框架内写路径一律使用 <see cref="SaveToDbPostHandler(byte[])"/> 精确提交；本重载仅供
    /// 未持有送库快照的外部自定义保存流程使用。
    /// Framework write paths always use the exact <see cref="SaveToDbPostHandler(byte[])"/> overload;
    /// this overload is kept for external custom save flows that do not hold the persisted snapshot.
    /// </remarks>
    public void SaveToDbPostHandler()
    {
        CheckStateHash();
        _stateHash.SaveToDbPostHandler();
    }

    /// <summary>
    /// 检查StateHash对象是否存在。
    /// </summary>
    /// <remarks>
    /// Check if the StateHash object exists.
    /// </remarks>
    private void CheckStateHash()
    {
        if (_stateHash.IsNotNull())
        {
            return;
        }

        // 懒基线按「未持久化」处理（isNew=true）：绕过 Provider 查询路径（未调 LoadFromDbPostHandler）
        // 的对象在首次检查前可能已被业务修改，若以当前序列化固化基线（isNew=false），这些变异会被
        // 静默吞掉（判定未修改 → 永不回存）。按新对象处理使首次检查必报已修改，
        // 强制一次幂等整档回存收敛基线——宁可多写一次，不吞变异。
        // The lazy baseline treats the state as unpersisted (isNew=true): an object that bypassed the
        // provider query paths (LoadFromDbPostHandler never called) may already carry business mutations
        // at its first check; baselining the current serialization (isNew=false) would silently swallow
        // them (reported unmodified → never saved). Treating it as new forces the first check to
        // report modified and one idempotent whole-document save to settle the baseline — one redundant
        // write is preferred over swallowing mutations.
        LoadFromDbPostHandler(isNew: true);
    }

    /// <summary>
    /// 将对象序列化转换为字节数组（默认 JSON / UTF8 实现，供 StateHash 脏检查使用）。
    /// </summary>
    /// <remarks>
    /// Serialize the object to a byte array (default JSON/UTF8 implementation, used by StateHash change detection).
    /// 仅服务于进程内 StateHash 脏检查，与持久化格式无关（Mongo 持久化为 BSON 文档，PostgreSQL 持久化为 jsonb 列，各自独立序列化）。
    /// Only serves in-process StateHash change detection and is independent of the persistence format
    /// (Mongo persists BSON documents and PostgreSQL persists jsonb columns, each with its own serialization).
    /// </remarks>
    /// <returns>序列化后的字节数组 / Serialized byte array</returns>
    public virtual byte[] ToBytes()
    {
        return System.Text.Encoding.UTF8.GetBytes(JsonHelper.Serialize(this));
    }

    #endregion
}