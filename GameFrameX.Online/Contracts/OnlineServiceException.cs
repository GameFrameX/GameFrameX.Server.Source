//  ==========================================================================================
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

namespace GameFrameX.Online.Contracts;

/// <summary>
/// Online 契约实现层的段位化异常（vault:C2 S1.4：Token 契约等返回形 DTO 无法携带错误码时的失败通道）。
/// <para>
/// 维护约束：仅用于「契约 DTO 形状承载不了错误码」的场景（如 <c>IOnlineSessionTokenContract</c> 的结果类型）；
/// 服务层常规业务失败应使用 <see cref="OnlineResult{TData}"/> 段位化返回，不抛本异常。
/// 宿主装配层捕获本异常后映射为 <c>OnlineResponse.Fail(Code)</c> 响应信封，异常本体不透出客户端。
/// </para>
/// </summary>
public sealed class OnlineServiceException : Exception
{
    /// <summary>
    /// 获取协议错误码（段位含义见 <see cref="OnlineErrorCode"/>）。
    /// </summary>
    public OnlineErrorCode Code
    {
        get;
    }

    /// <summary>
    /// 初始化 <see cref="OnlineServiceException"/>。
    /// </summary>
    /// <param name="code">协议错误码。</param>
    /// <param name="message">失败描述（服务端内部语义）。</param>
    public OnlineServiceException(OnlineErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }
}
