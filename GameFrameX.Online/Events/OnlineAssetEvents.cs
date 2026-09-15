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


namespace GameFrameX.Online.Events;

using System.Text.Json;
using GameFrameX.Online.Assets;

/// <summary>
/// 资产域事件工厂（vault:C4 S3.4：统一入口成功落账后发布资产变更事实，供下游与 Admin 消费）。
/// <para>
/// 维护约束：事件代表已落账的事实（只发布成功交易），消费端不得回写资产状态；
/// 载荷携带交易标识与逐资产前后值（审计视图投影），金额输出经 <c>OnlineEventSanitizer</c>
/// 约定脱敏由宿主装配执行；事件是增量不是快照（快照查询走 <c>OnlineAssetQueryService</c>）。
/// </para>
/// </summary>
public static class OnlineAssetEvents
{
    /// <summary>资产发放事实（Grant/Reissue 操作）。</summary>
    public const string AssetGranted = "Online.Asset.Granted";

    /// <summary>资产扣除事实（Deduct/Revoke 操作）。</summary>
    public const string AssetDeducted = "Online.Asset.Deducted";

    /// <summary>资产人工调整事实（Adjust 操作，含系统补偿反转）。</summary>
    public const string AssetAdjusted = "Online.Asset.Adjusted";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-assets";

    /// <summary>
    /// 构造资产变更事件（按操作类型选择事件类型；混合批次以操作类型为准，逐资产前后值在载荷内）。
    /// </summary>
    /// <param name="transaction">资产交易（成功终态）。</param>
    /// <param name="entries">落账条目。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent Create(OnlineAssetTransaction transaction, IReadOnlyList<OnlineLedgerEntry> entries)
    {
        string eventType;
        switch (transaction.Operation)
        {
            case OnlineGrantOperation.Grant:
            case OnlineGrantOperation.Reissue:
                eventType = AssetGranted;
                break;
            case OnlineGrantOperation.Deduct:
            case OnlineGrantOperation.Revoke:
                eventType = AssetDeducted;
                break;
            default:
                eventType = AssetAdjusted;
                break;
        }
        var payload = new AssetEventPayload
        {
            TransactionId = transaction.TransactionId,
            Operation = transaction.Operation.ToString(),
            Source = transaction.Source.ToString(),
            BusinessOrderId = transaction.BusinessOrderId ?? string.Empty,
            Lines = entries.Select(entry => new AssetEventLine { AssetKind = entry.AssetKind.ToString(), AssetId = entry.AssetId, Delta = entry.Delta, AmountBefore = entry.AmountBefore, AmountAfter = entry.AmountAfter }).ToList(),
        };
        var auditFields = new Dictionary<string, string>
        {
            { "TransactionId", transaction.TransactionId },
            { "PlayerId", transaction.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Operation", transaction.Operation.ToString() },
            { "ChangeSource", transaction.Source.ToString() },
            { "BusinessOrderId", transaction.BusinessOrderId ?? string.Empty },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = eventType,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = transaction.TenantId,
            AppId = transaction.AppId,
            ServerId = transaction.InitiatingServerId,
            PlayerId = transaction.PlayerId,
            Source = Source,
            CorrelationId = null,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 资产事件载荷（序列化契约；结构演进时递增 SchemaVersion）。
    /// </summary>
    private sealed class AssetEventPayload
    {
        /// <summary>交易标识。</summary>
        public string TransactionId
        {
            get;
            set;
        }

        /// <summary>操作类型名。</summary>
        public string Operation
        {
            get;
            set;
        }

        /// <summary>变更来源名。</summary>
        public string Source
        {
            get;
            set;
        }

        /// <summary>业务单号。</summary>
        public string BusinessOrderId
        {
            get;
            set;
        }

        /// <summary>逐资产变更行。</summary>
        public List<AssetEventLine> Lines
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 资产事件变更行（前后值审计投影）。
    /// </summary>
    private sealed class AssetEventLine
    {
        /// <summary>资产类别名。</summary>
        public string AssetKind
        {
            get;
            set;
        }

        /// <summary>资产标识。</summary>
        public string AssetId
        {
            get;
            set;
        }

        /// <summary>带符号数额。</summary>
        public long Delta
        {
            get;
            set;
        }

        /// <summary>变更前数量。</summary>
        public long AmountBefore
        {
            get;
            set;
        }

        /// <summary>变更后数量。</summary>
        public long AmountAfter
        {
            get;
            set;
        }
    }
}
