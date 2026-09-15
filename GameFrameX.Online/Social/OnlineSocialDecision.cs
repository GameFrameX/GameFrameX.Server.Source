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

using GameFrameX.Online.Contracts;

/// <summary>
/// 社交互动裁决结果（vault:C7 S6.3：三通路共用的「是否允许 + 拒绝码 + 拒绝原因」）。
/// <para>
/// 维护约束：拒绝码必须取自冻结的 <see cref="OnlineErrorCode"/> 既有段位，
/// **不得为社交裁决新增错误码成员**（VC-1.11 守护测试锁定 23 成员同构）；
/// 允许时 <see cref="Code"/> 恒为 <see cref="OnlineErrorCode.None"/>。
/// <see cref="Reason"/> 是服务端内部语义，用于日志与 Admin 追溯，不直接透出客户端行为差异
/// （「被屏蔽」与「被禁言」在客户端提示上可区分，但不得暴露是**谁**屏蔽了你——屏蔽是单向私密事实）。
/// </para>
/// </summary>
public sealed class OnlineSocialDecision
{
    /// <summary>
    /// 获取是否允许本次互动。
    /// </summary>
    public bool Allowed
    {
        get;
        private set;
    }

    /// <summary>
    /// 获取拒绝码（允许时为 <see cref="OnlineErrorCode.None"/>）。
    /// </summary>
    public OnlineErrorCode Code
    {
        get;
        private set;
    }

    /// <summary>
    /// 获取拒绝原因（服务端内部语义；允许时为空字符串）。
    /// </summary>
    public string Reason
    {
        get;
        private set;
    }

    /// <summary>
    /// 初始化裁决结果（仅供工厂方法使用）。
    /// </summary>
    /// <param name="allowed">是否允许。</param>
    /// <param name="code">拒绝码。</param>
    /// <param name="reason">拒绝原因。</param>
    private OnlineSocialDecision(bool allowed, OnlineErrorCode code, string reason)
    {
        Allowed = allowed;
        Code = code;
        Reason = reason ?? string.Empty;
    }

    /// <summary>
    /// 构造放行裁决。
    /// </summary>
    /// <returns>放行结果实例。</returns>
    public static OnlineSocialDecision Allow()
    {
        return new OnlineSocialDecision(true, OnlineErrorCode.None, string.Empty);
    }

    /// <summary>
    /// 构造拒绝裁决。
    /// </summary>
    /// <param name="code">拒绝码（不得传 <see cref="OnlineErrorCode.None"/>）。</param>
    /// <param name="reason">拒绝原因。</param>
    /// <returns>拒绝结果实例。</returns>
    public static OnlineSocialDecision Deny(OnlineErrorCode code, string reason)
    {
        return new OnlineSocialDecision(false, code, reason);
    }
}
