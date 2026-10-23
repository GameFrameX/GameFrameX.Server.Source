// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Network.RemoteMessaging.Contracts;

namespace GameFrameX.Network.RemoteMessaging.Unified;

/// <summary>
/// 系统消息发送结果（单向模式，无响应）。
/// </summary>
/// <remarks>
/// Server message send result (one-way pattern, no response).
/// </remarks>
public sealed class ServerSendResult
{
    /// <summary>
    /// 是否发送成功
    /// </summary>
    /// <remarks>
    /// Indicates whether the send was successful.
    /// </remarks>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 错误描述
    /// </summary>
    /// <remarks>
    /// Error description.
    /// </remarks>
    public string ErrorMessage { get; init; }

    /// <summary>
    /// 选中的目标实例ID
    /// </summary>
    /// <remarks>
    /// Selected target instance ID.
    /// </remarks>
    public string SelectedInstance { get; init; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    /// <remarks>
    /// Creates a successful result.
    /// </remarks>
    /// <param name="selectedInstance">选中的实例 / Selected instance</param>
    /// <returns>成功的发送结果 / A successful send result</returns>
    public static ServerSendResult Ok(string selectedInstance = null)
    {
        return new ServerSendResult
        {
            IsSuccess = true,
            SelectedInstance = selectedInstance,
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    /// <remarks>
    /// Creates a failure result.
    /// </remarks>
    /// <param name="errorMessage">错误描述 / Error description</param>
    /// <returns>失败的发送结果 / A failed send result</returns>
    public static ServerSendResult Fail(string errorMessage)
    {
        return new ServerSendResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
        };
    }
}
