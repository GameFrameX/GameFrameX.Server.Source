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
/// 资产变更批次（统一入口向存储层提交的原子应用单元；<see cref="OnlineAssetChangeBatch"/> 构造仅限本程序集，
/// 使「绕过统一入口直接改资产」在本程序集外结构性不可达——X5 唯一写入口的代码级落地）。
/// <para>
/// 维护约束：批次内所有变更行在单个玩家分片事务内原子应用（先全量校验后统一落账，VC-3.6 无半成品）；
/// 同一批次同一 (类别, 资产标识) 只允许一行（由统一入口先行聚合校验）；
/// 反转批次（补偿）以 <see cref="CompensatesTransactionId"/> 指向原交易。
/// </para>
/// </summary>
public sealed class OnlineAssetChangeBatch
{
    /// <summary>
    /// 获取所属交易标识。
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
    /// 获取归属服标识。
    /// </summary>
    public long HomeServerId
    {
        get;
    }

    /// <summary>
    /// 获取发起服标识。
    /// </summary>
    public long InitiatingServerId
    {
        get;
    }

    /// <summary>
    /// 获取变更来源。
    /// </summary>
    public OnlineAssetChangeSource Source
    {
        get;
    }

    /// <summary>
    /// 获取操作类型。
    /// </summary>
    public OnlineGrantOperation Operation
    {
        get;
    }

    /// <summary>
    /// 获取变更原因。
    /// </summary>
    public string Reason
    {
        get;
    }

    /// <summary>
    /// 获取业务单号。
    /// </summary>
    public string BusinessOrderId
    {
        get;
    }

    /// <summary>
    /// 获取操作者。
    /// </summary>
    public string OperatorId
    {
        get;
    }

    /// <summary>
    /// 获取变更行列表（同一资产只一行）。
    /// </summary>
    public IReadOnlyList<OnlineAssetChangeLine> Lines
    {
        get;
    }

    /// <summary>
    /// 获取补偿指向的原交易标识（反转批次非空；普通批次为空字符串）。
    /// </summary>
    public string CompensatesTransactionId
    {
        get;
    }

    /// <summary>
    /// 构造变更批次（internal：只允许统一入口在本程序集内构造，杜绝旁路写入）。
    /// </summary>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="homeServerId">归属服标识。</param>
    /// <param name="initiatingServerId">发起服标识。</param>
    /// <param name="source">变更来源。</param>
    /// <param name="operation">操作类型。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="businessOrderId">业务单号。</param>
    /// <param name="operatorId">操作者。</param>
    /// <param name="lines">变更行列表。</param>
    /// <param name="compensatesTransactionId">补偿指向原交易（普通批次传空字符串）。</param>
    internal OnlineAssetChangeBatch(string transactionId, long tenantId, long appId, long playerId, long homeServerId, long initiatingServerId, OnlineAssetChangeSource source, OnlineGrantOperation operation, string reason, string businessOrderId, string operatorId, IReadOnlyList<OnlineAssetChangeLine> lines, string compensatesTransactionId)
    {
        TransactionId = transactionId;
        TenantId = tenantId;
        AppId = appId;
        PlayerId = playerId;
        HomeServerId = homeServerId;
        InitiatingServerId = initiatingServerId;
        Source = source;
        Operation = operation;
        Reason = reason;
        BusinessOrderId = businessOrderId;
        OperatorId = operatorId;
        Lines = lines;
        CompensatesTransactionId = compensatesTransactionId;
    }
}
