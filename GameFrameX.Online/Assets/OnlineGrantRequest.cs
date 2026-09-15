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

using GameFrameX.Online.Scope;

/// <summary>
/// 统一资产入口请求（vault:C4 S3.4：所有资产发放/扣除/撤销/补发/人工调整的唯一提交形态，X5）。
/// <para>
/// 维护约束（资产红线）：作用域三元组以鉴权上下文为准（<c>OnlineScopeResolver.EnsureAuthorized</c>
/// 产物），客户端提交的同名字段不可覆盖；<see cref="IdempotencyKey"/> 必填且绑定玩家位
/// （六类来源各自的业务单号规则由调用方生成，服务端按 VC-1.4 语义校验键格式）；
/// <see cref="HomeServerId"/> 为玩家归属服（0 = 取作用域区服；跨服发奖的路由判定输入）；
/// 请求模型只承载「服务端意图」——不存在客户端直接提交余额/库存/奖励结果的通道（VC-3.8 本仓半边）。
/// </para>
/// </summary>
public sealed class OnlineGrantRequest
{
    /// <summary>
    /// 获取生效作用域（必须含玩家主体位）。
    /// </summary>
    public OnlineScope Scope
    {
        get;
    }

    /// <summary>
    /// 获取变更来源（六类业务来源；系统补偿来源业务调用方禁止指定）。
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
    /// 获取变更原因（业务语义描述，审计追溯必填）。
    /// </summary>
    public string Reason
    {
        get;
    }

    /// <summary>
    /// 获取业务单号（支付订单号/兑换流水/结算标识等来源侧单号，审计追溯必填）。
    /// </summary>
    public string BusinessOrderId
    {
        get;
    }

    /// <summary>
    /// 获取操作者（撤销与人工调整必填；系统来源为空字符串）。
    /// </summary>
    public string OperatorId
    {
        get;
    }

    /// <summary>
    /// 获取变更行列表（同一资产只一行，数额非 0）。
    /// </summary>
    public IReadOnlyList<OnlineAssetChangeLine> Changes
    {
        get;
    }

    /// <summary>
    /// 获取幂等键（业务意图标识；重复回调/领取/兑换/结算以相同键重试只生效一次）。
    /// </summary>
    public string IdempotencyKey
    {
        get;
    }

    /// <summary>
    /// 获取归属服标识（玩家家服；0 = 取作用域区服。跨服发奖的路由判定输入，VC-3.10）。
    /// </summary>
    public long HomeServerId
    {
        get;
    }

    /// <summary>
    /// 获取关联链路键（可空；贯穿事件信封）。
    /// </summary>
    public string CorrelationId
    {
        get;
    }

    /// <summary>
    /// 构造统一入口请求。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="source">变更来源。</param>
    /// <param name="operation">操作类型。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="businessOrderId">业务单号。</param>
    /// <param name="operatorId">操作者（撤销/人工调整必填）。</param>
    /// <param name="changes">变更行列表。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="homeServerId">归属服（0 = 取作用域区服）。</param>
    /// <param name="correlationId">关联链路键（可空）。</param>
    public OnlineGrantRequest(OnlineScope scope, OnlineAssetChangeSource source, OnlineGrantOperation operation, string reason, string businessOrderId, string operatorId, IReadOnlyList<OnlineAssetChangeLine> changes, string idempotencyKey, long homeServerId = 0, string correlationId = null)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Source = source;
        Operation = operation;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        BusinessOrderId = businessOrderId ?? throw new ArgumentNullException(nameof(businessOrderId));
        OperatorId = operatorId ?? string.Empty;
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
        IdempotencyKey = idempotencyKey ?? throw new ArgumentNullException(nameof(idempotencyKey));
        HomeServerId = homeServerId;
        CorrelationId = correlationId;
    }
}
