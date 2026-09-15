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
/// 资产对账报告（vault:C4 S3.10/VC-3.13：账本累加 == 余额/库存快照的差异输出）。
/// <para>
/// 维护约束：对账以账本累加为事实源——任何差异即告警对象（差异 = 0 红线的守护输出）；
/// 差异记录必须可定位（玩家/类别/资产/两侧数值），供人工或脚本回溯修正；
/// 报告是只读快照，不携带修正动作（修正只能经统一入口追加交易）。
/// </para>
/// </summary>
public sealed class OnlineAssetReconciliationReport
{
    /// <summary>
    /// 获取对账覆盖玩家数。
    /// </summary>
    public int PlayerCount
    {
        get;
    }

    /// <summary>
    /// 获取是否一致（差异数为 0）。
    /// </summary>
    public bool IsConsistent
    {
        get;
    }

    /// <summary>
    /// 获取差异列表（空 = 一致）。
    /// </summary>
    public IReadOnlyList<Difference> Differences
    {
        get;
    }

    /// <summary>
    /// 构造对账报告。
    /// </summary>
    /// <param name="playerCount">覆盖玩家数。</param>
    /// <param name="differences">差异列表。</param>
    public OnlineAssetReconciliationReport(int playerCount, IReadOnlyList<Difference> differences)
    {
        PlayerCount = playerCount;
        Differences = differences ?? Array.Empty<Difference>();
        IsConsistent = Differences.Count == 0;
    }

    /// <summary>
    /// 单资产对账差异（快照侧 vs 账本侧）。
    /// </summary>
    public sealed class Difference
    {
        /// <summary>
        /// 获取玩家标识。
        /// </summary>
        public long PlayerId
        {
            get;
        }

        /// <summary>
        /// 获取资产类别。
        /// </summary>
        public OnlineAssetKind AssetKind
        {
            get;
        }

        /// <summary>
        /// 获取资产标识。
        /// </summary>
        public string AssetId
        {
            get;
        }

        /// <summary>
        /// 获取快照侧数值（余额/库存）。
        /// </summary>
        public long SnapshotAmount
        {
            get;
        }

        /// <summary>
        /// 获取账本侧数值（带符号累加）。
        /// </summary>
        public long LedgerSumAmount
        {
            get;
        }

        /// <summary>
        /// 构造差异记录。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="assetKind">资产类别。</param>
        /// <param name="assetId">资产标识。</param>
        /// <param name="snapshotAmount">快照侧数值。</param>
        /// <param name="ledgerSumAmount">账本侧数值。</param>
        public Difference(long playerId, OnlineAssetKind assetKind, string assetId, long snapshotAmount, long ledgerSumAmount)
        {
            PlayerId = playerId;
            AssetKind = assetKind;
            AssetId = assetId;
            SnapshotAmount = snapshotAmount;
            LedgerSumAmount = ledgerSumAmount;
        }
    }
}
