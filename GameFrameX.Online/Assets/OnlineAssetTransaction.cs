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
/// 资产交易记录（vault:C4 S3.3：一次业务操作的幂等边界；每笔变更必须带 TransactionId + IdempotencyKey）。
/// <para>
/// 维护约束（红线）：<see cref="IdempotencyKey"/> 绑定玩家作用域（相同业务意图重试只生效一次，
/// 幂等判定经 C93 <c>OnlineIdempotencyService</c> 组装 Foundation 原语）；状态迁移受
/// <see cref="OnlineAssetTransactionState"/> 状态机约束，终态不可再迁移；<see cref="AppliedLedgerEntryIds"/>
/// 记录本交易落账的账本条目（补偿反转的输入）；重启恢复以非终态记录为扫描输入（VC-3.12）。
/// </para>
/// </summary>
public sealed class OnlineAssetTransaction
{
    /// <summary>
    /// 获取或设置交易标识（全局唯一，<c>tx-</c> 前缀；账本条目的交易外键）。
    /// </summary>
    public string TransactionId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置幂等键（业务意图标识；与玩家作用域绑定）。
    /// </summary>
    public string IdempotencyKey
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置规范化请求文本摘要（同键不同摘要判冲突的审计留痕；幂等判定由 Foundation 执行）。
    /// </summary>
    public string RequestDigest
    {
        get;
        set;
    }

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
    /// 获取或设置归属服标识（资产所有者的家服）。
    /// </summary>
    public long HomeServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置发起服标识（执行本次变更的服务器）。
    /// </summary>
    public long InitiatingServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置变更来源。
    /// </summary>
    public OnlineAssetChangeSource Source
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作类型。
    /// </summary>
    public OnlineGrantOperation Operation
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置变更原因（审计追溯）。
    /// </summary>
    public string Reason
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置业务单号（来源侧单号：支付订单/兑换流水/结算标识等）。
    /// </summary>
    public string BusinessOrderId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作者（人工资金动作必填）。
    /// </summary>
    public string OperatorId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置期望变更行数（创建时按请求变更行数固化；恢复任务以
    /// 「已落账条目数 == 期望行数」区分完整落账后中断与部分应用，VC-3.12 判定依据）。
    /// </summary>
    public int ExpectedChangeCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置交易状态（迁移受状态机约束）。
    /// </summary>
    public OnlineAssetTransactionState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本交易落账的账本条目标识列表（补偿反转输入；未落账为空）。
    /// </summary>
    public IReadOnlyList<string> AppliedLedgerEntryIds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置落定时刻（UTC 毫秒；未落定为 0）。
    /// </summary>
    public long SettledTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置失败描述（Failed/CompensationPending 时的内部语义说明）。
    /// </summary>
    public string FailureMessage
    {
        get;
        set;
    }
}
