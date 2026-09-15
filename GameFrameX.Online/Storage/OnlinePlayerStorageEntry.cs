//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Storage;

/// <summary>
/// 玩家云存储条目（vault:C3 S2.6：玩家 + App 作用域的 KV 实体；区服不参与隔离——换服数据随身）。
/// <para>
/// 维护约束：键 = (TenantId, AppId, PlayerId, Collection, Key)，跨作用域读写结构性不可达（VC-2.7/2.8/2.9）；
/// <see cref="Version"/> 为乐观锁版本（创建为 1，每次写递增，CAS 失败映射 6xxx，VC-2.10）；
/// 删除为软删（<see cref="DeletedAtTime"/>，读取与列举即不可见）；<see cref="ExpiresAtTime"/> 到期后由清理任务软删。
/// </para>
/// </summary>
public sealed class OnlinePlayerStorageEntry
{
    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置应用标识。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置集合名（同一玩家的数据分组，如 "settings"/"archive"）。
    /// </summary>
    public string Collection
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置条目键（集合内唯一）。
    /// </summary>
    public string Key
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置负载数据（原始字节；格式由调用方约定）。
    /// </summary>
    public byte[] Payload
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置版本（乐观锁；创建为 1，每次写递增）。
    /// </summary>
    public long Version
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时间（Unix 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建者（会话标识或操作者标识）。
    /// </summary>
    public string CreatedBy
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近更新时间（Unix 毫秒）。
    /// </summary>
    public long UpdatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近更新者（会话标识或操作者标识）。
    /// </summary>
    public string UpdatedBy
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置过期时刻（Unix 毫秒；0 = 永不过期）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置软删时刻（Unix 毫秒；0 = 未删除）。
    /// </summary>
    public long DeletedAtTime
    {
        get;
        set;
    }
}
