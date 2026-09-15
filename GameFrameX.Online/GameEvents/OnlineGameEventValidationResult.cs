// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 游戏事件 L0 校验结果（受理 / 拒绝 + 拒绝码 + 可读原因）。
/// </summary>
public sealed class OnlineGameEventValidationResult
{
    /// <summary>
    /// 获取或设置是否受理。
    /// </summary>
    public bool IsAccepted
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置拒绝码（受理时为 <see cref="OnlineGameEventRejectionReason.None"/>）。
    /// </summary>
    public OnlineGameEventRejectionReason Reason
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置拒绝原因（脱敏可读描述，随死信记录留存供投递方排查）。
    /// </summary>
    public string Message
    {
        get;
        set;
    }

    /// <summary>
    /// 构造受理结果。
    /// </summary>
    /// <returns>受理结果实例。</returns>
    public static OnlineGameEventValidationResult Accept()
    {
        return new OnlineGameEventValidationResult
        {
            IsAccepted = true,
            Reason = OnlineGameEventRejectionReason.None,
            Message = null,
        };
    }

    /// <summary>
    /// 构造拒绝结果。
    /// </summary>
    /// <param name="reason">拒绝码。</param>
    /// <param name="message">拒绝原因。</param>
    /// <returns>拒绝结果实例。</returns>
    public static OnlineGameEventValidationResult Reject(OnlineGameEventRejectionReason reason, string message)
    {
        return new OnlineGameEventValidationResult
        {
            IsAccepted = false,
            Reason = reason,
            Message = message,
        };
    }
}
