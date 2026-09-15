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

namespace GameFrameX.Online.Social;

/// <summary>
/// 推送出口回执（vault:C7 S6.8「Socket 推送」的传输结果）。
/// <para>
/// 维护约束（红线）：<see cref="Retryable"/> 是服务层在「重试」与「落 Failed 终态」之间抉择的唯一判据——
/// <c>false</c> 表示重试也不会成功（如接收者不存在、连接被永久拒绝），服务层据此直接落
/// <see cref="OnlineNotificationState.Failed"/> 终态，不再排入重试，避免无效重投耗尽重试预算并拖慢队列；
/// 传输性失败必须由推送出口显式表达，不得吞掉后返回成功（见 <see cref="IOnlineNotificationDispatcher"/>）。
/// </para>
/// </summary>
public sealed class OnlineNotificationDispatchOutcome
{
    /// <summary>
    /// 获取本次推送是否成功送达。
    /// </summary>
    public bool Delivered
    {
        get;
        private set;
    }

    /// <summary>
    /// 获取失败原因（成功时为空字符串；服务层写入 <see cref="OnlineNotification.LastError"/>）。
    /// </summary>
    public string FailureReason
    {
        get;
        private set;
    }

    /// <summary>
    /// 获取失败是否可重试（<c>false</c> = 重试也不会成功，服务层直接落终态）。
    /// </summary>
    public bool Retryable
    {
        get;
        private set;
    }

    /// <summary>
    /// 初始化推送回执（仅供工厂方法使用，外部不得构造「既成功又带失败原因」的畸形回执）。
    /// </summary>
    /// <param name="delivered">是否送达。</param>
    /// <param name="failureReason">失败原因。</param>
    /// <param name="retryable">失败是否可重试。</param>
    private OnlineNotificationDispatchOutcome(bool delivered, string failureReason, bool retryable)
    {
        Delivered = delivered;
        FailureReason = failureReason ?? string.Empty;
        Retryable = retryable;
    }

    /// <summary>
    /// 构造成功回执。
    /// </summary>
    /// <returns>送达回执（无失败原因、不可重试）。</returns>
    public static OnlineNotificationDispatchOutcome Deliver()
    {
        return new OnlineNotificationDispatchOutcome(true, string.Empty, false);
    }

    /// <summary>
    /// 构造失败回执。
    /// </summary>
    /// <param name="failureReason">失败原因（写入通知记录供重试与排障）。</param>
    /// <param name="retryable">重试是否有成功可能。</param>
    /// <returns>失败回执。</returns>
    public static OnlineNotificationDispatchOutcome Fail(string failureReason, bool retryable)
    {
        return new OnlineNotificationDispatchOutcome(false, failureReason, retryable);
    }
}
