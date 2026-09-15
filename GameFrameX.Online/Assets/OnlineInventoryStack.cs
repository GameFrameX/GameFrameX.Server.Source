// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others that are prohibited by laws and regulations!
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Assets;

/// <summary>
/// 玩家道具库存堆栈快照（vault:C4 S3.2：Inventory = 玩家道具和数量；数量可由账本重算校验）。
/// <para>
/// 维护约束：数量快照只经统一入口在玩家分片事务内与账本条目原子同步（X5）；
/// 键 = (TenantId, AppId, PlayerId, ItemId)，区服不参与隔离（R3 同构）；
/// 数量下限 0；堆叠上限等业务约束归调用方前置校验，本层只守护一致性红线。
/// </para>
/// </summary>
public sealed class OnlineInventoryStack
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
    /// 获取或设置 App 标识。
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
    /// 获取或设置道具标识。
    /// </summary>
    public string ItemId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置数量快照（下限 0；应用后值，与账本累加一致性由对账守护）。
    /// </summary>
    public long Quantity
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置快照版本（每次资产变更递增；乐观锁与审计用）。
    /// </summary>
    public long Version
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一次更新时刻（UTC 毫秒）。
    /// </summary>
    public long UpdatedTime
    {
        get;
        set;
    }
}
