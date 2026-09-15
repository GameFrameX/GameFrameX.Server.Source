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
/// 内容审核裁决结果（vault:C7 S6.7：审核扩展点的返回载体）。
/// <para>
/// 维护约束：本类型**不携带拒绝错误码**——审核拒绝一律映射 <c>RiskControlRejected</c>（风控段），
/// 由 <see cref="OnlineChatService"/> 统一落定。让插件自选错误码会把错误码表的控制权交给外部实现，
/// 消息里出现任意段位的码就再也说不清是谁定的。
/// </para>
/// </summary>
public sealed class OnlineChatModerationVerdict
{
    /// <summary>
    /// 获取是否放行。
    /// </summary>
    public bool Allowed
    {
        get;
        private set;
    }

    /// <summary>
    /// 获取拒绝原因（放行时为空字符串；供审计与排障，不透出给发送者）。
    /// </summary>
    public string Reason
    {
        get;
        private set;
    }

    /// <summary>
    /// 初始化裁决结果（仅供工厂方法使用）。
    /// </summary>
    /// <param name="allowed">是否放行。</param>
    /// <param name="reason">拒绝原因。</param>
    private OnlineChatModerationVerdict(bool allowed, string reason)
    {
        Allowed = allowed;
        Reason = reason ?? string.Empty;
    }

    /// <summary>
    /// 构造放行裁决。
    /// </summary>
    /// <returns>放行结果实例。</returns>
    public static OnlineChatModerationVerdict Allow()
    {
        return new OnlineChatModerationVerdict(true, string.Empty);
    }

    /// <summary>
    /// 构造拒绝裁决。
    /// </summary>
    /// <param name="reason">拒绝原因。</param>
    /// <returns>拒绝结果实例。</returns>
    public static OnlineChatModerationVerdict Reject(string reason)
    {
        return new OnlineChatModerationVerdict(false, reason);
    }
}
