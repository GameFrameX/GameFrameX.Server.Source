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
/// 不可变资产账本条目（vault:C4 S3.2/S3.3：只追加、不修改、不删除；余额可由账本重算校验）。
/// <para>
/// 维护约束（资产红线）：本类型一经落账即为事实，任何代码路径不得修改或删除历史条目
/// （VC-3.7）；错误发放只能通过追加带符号反转条目（来源 <see cref="OnlineAssetChangeSource.SystemCompensation"/>，
/// <see cref="CompensatesTransactionId"/> 指向原交易）纠正；每条必须携带完整追溯字段——
/// 作用域（Player/App/Server 双侧）、来源、原因、业务单号、操作者、前后值与所属交易
/// （VC-3.14 反查依据）；<see cref="AmountBefore"/> + <see cref="Delta"/> == <see cref="AmountAfter"/>
/// 由存储层在追加时原子校验；<see cref="SequenceNumber"/> 为玩家维度单调递增账本序
/// （对账与游标分页的稳定排序键）。
/// </para>
/// </summary>
public sealed class OnlineLedgerEntry
{
    /// <summary>
    /// 获取账本条目标识（全局唯一，<c>led-</c> 前缀）。
    /// </summary>
    public string EntryId
    {
        get;
    }

    /// <summary>
    /// 获取所属资产交易标识（幂等边界的交易外键）。
    /// </summary>
    public string TransactionId
    {
        get;
    }

    /// <summary>
    /// 获取租户标识。
    /// </summary>
    public long TenantId
    {
        get;
    }

    /// <summary>
    /// 获取 App 标识。
    /// </summary>
    public long AppId
    {
        get;
    }

    /// <summary>
    /// 获取玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
    }

    /// <summary>
    /// 获取归属服标识（资产所有者的家服；跨服发奖的路由归属，VC-3.10）。
    /// </summary>
    public long HomeServerId
    {
        get;
    }

    /// <summary>
    /// 获取发起服标识（执行本次变更的服务器；归属服只表运行位置，不做数据隔离）。
    /// </summary>
    public long InitiatingServerId
    {
        get;
    }

    /// <summary>
    /// 获取变更来源（六类业务来源或系统补偿；未登记来源结构性不可达）。
    /// </summary>
    public OnlineAssetChangeSource Source
    {
        get;
    }

    /// <summary>
    /// 获取操作类型（发放/扣除/撤销/补发/人工调整）。
    /// </summary>
    public OnlineGrantOperation Operation
    {
        get;
    }

    /// <summary>
    /// 获取变更原因（业务语义描述，审计追溯必填）。
    /// </summary>
    public string Reason
    {
        get;
    }

    /// <summary>
    /// 获取业务单号（支付订单号/兑换码流水/邮件附件流水/结算标识等来源侧单号）。
    /// </summary>
    public string BusinessOrderId
    {
        get;
    }

    /// <summary>
    /// 获取操作者（会话或管理员标识；系统来源为空字符串，人工资金动作必填）。
    /// </summary>
    public string OperatorId
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
    /// 获取资产标识（货币代码或道具 ID）。
    /// </summary>
    public string AssetId
    {
        get;
    }

    /// <summary>
    /// 获取变更前数量（快照口径）。
    /// </summary>
    public long AmountBefore
    {
        get;
    }

    /// <summary>
    /// 获取带符号变更数额（AmountAfter - AmountBefore）。
    /// </summary>
    public long Delta
    {
        get;
    }

    /// <summary>
    /// 获取变更后数量（AmountBefore + Delta；下限 0 由存储层保证）。
    /// </summary>
    public long AmountAfter
    {
        get;
    }

    /// <summary>
    /// 获取补偿指向的原交易标识（反转条目非空；普通条目为空字符串）。
    /// </summary>
    public string CompensatesTransactionId
    {
        get;
    }

    /// <summary>
    /// 获取玩家维度账本序（单调递增；追加时由存储层分配）。
    /// </summary>
    public long SequenceNumber
    {
        get;
    }

    /// <summary>
    /// 获取落账时刻（UTC 毫秒）。
    /// </summary>
    public long OccurredTime
    {
        get;
    }

    /// <summary>
    /// 构造账本条目（仅供存储层在玩家分片事务内调用；构造后不得再变更任何字段）。
    /// </summary>
    /// <param name="entryId">条目标识。</param>
    /// <param name="transactionId">所属交易标识。</param>
    /// <param name="header">账本头载荷（作用域 + 来源/操作/原因/单号/操作者，与变更批次共享）。</param>
    /// <param name="assetKind">资产类别。</param>
    /// <param name="assetId">资产标识。</param>
    /// <param name="amountBefore">变更前数量。</param>
    /// <param name="delta">带符号数额。</param>
    /// <param name="amountAfter">变更后数量。</param>
    /// <param name="compensatesTransactionId">补偿指向原交易（非反转条目传空字符串）。</param>
    /// <param name="sequenceNumber">玩家维度账本序。</param>
    /// <param name="occurredTime">落账时刻（UTC 毫秒）。</param>
    public OnlineLedgerEntry(string entryId, string transactionId, OnlineLedgerHeader header, OnlineAssetKind assetKind, string assetId, long amountBefore, long delta, long amountAfter, string compensatesTransactionId, long sequenceNumber, long occurredTime)
    {
        EntryId = entryId;
        TransactionId = transactionId;
        TenantId = header.TenantId;
        AppId = header.AppId;
        PlayerId = header.PlayerId;
        HomeServerId = header.HomeServerId;
        InitiatingServerId = header.InitiatingServerId;
        Source = header.Source;
        Operation = header.Operation;
        Reason = header.Reason;
        BusinessOrderId = header.BusinessOrderId;
        OperatorId = header.OperatorId;
        AssetKind = assetKind;
        AssetId = assetId;
        AmountBefore = amountBefore;
        Delta = delta;
        AmountAfter = amountAfter;
        CompensatesTransactionId = compensatesTransactionId;
        SequenceNumber = sequenceNumber;
        OccurredTime = occurredTime;
    }
}
