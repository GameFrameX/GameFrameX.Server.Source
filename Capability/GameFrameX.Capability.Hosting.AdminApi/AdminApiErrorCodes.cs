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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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

namespace GameFrameX.Capability.Hosting.AdminApi;

/// <summary>
/// admin API 错误码条目（协议码 + 可本地化消息键；数值语义由能力侧错误码表注入，机制层不解释）。
/// </summary>
public sealed class AdminApiErrorCode
{
    /// <summary>
    /// 初始化 <see cref="AdminApiErrorCode"/>。
    /// </summary>
    /// <param name="code">协议错误码。</param>
    /// <param name="messageKey">可本地化消息键。</param>
    public AdminApiErrorCode(int code, string messageKey)
    {
        Code = code;
        MessageKey = messageKey ?? string.Empty;
    }

    /// <summary>
    /// 获取协议错误码。
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// 获取可本地化消息键。
    /// </summary>
    public string MessageKey { get; }
}

/// <summary>
/// admin API 管道错误码映射（组合根注入：把能力侧错误码表的机制用位映射进通用管道）。
/// <para>
/// 维护约束：五个机制用位缺一不可（构造校验 fail-fast）——
/// MalformedJson（请求体解析失败）/ UnknownAction（未知 action）/ ScopeMissing（作用域缺失）/
/// InternalError（未处理异常）/ CapabilityNotInstalled（action 属于已禁用能力）。
/// </para>
/// </summary>
public sealed class AdminApiErrorCodes
{
    /// <summary>
    /// 初始化 <see cref="AdminApiErrorCodes"/>（五个机制用位全部必填）。
    /// </summary>
    /// <param name="malformedJson">请求体解析失败码。</param>
    /// <param name="unknownAction">未知 action 码。</param>
    /// <param name="scopeMissing">作用域缺失码。</param>
    /// <param name="internalError">未处理异常码。</param>
    /// <param name="capabilityNotInstalled">action 属于已禁用能力码。</param>
    public AdminApiErrorCodes(AdminApiErrorCode malformedJson, AdminApiErrorCode unknownAction, AdminApiErrorCode scopeMissing, AdminApiErrorCode internalError, AdminApiErrorCode capabilityNotInstalled)
    {
        MalformedJson = malformedJson ?? throw new ArgumentNullException(nameof(malformedJson));
        UnknownAction = unknownAction ?? throw new ArgumentNullException(nameof(unknownAction));
        ScopeMissing = scopeMissing ?? throw new ArgumentNullException(nameof(scopeMissing));
        InternalError = internalError ?? throw new ArgumentNullException(nameof(internalError));
        CapabilityNotInstalled = capabilityNotInstalled ?? throw new ArgumentNullException(nameof(capabilityNotInstalled));
    }

    /// <summary>
    /// 获取请求体解析失败码。
    /// </summary>
    public AdminApiErrorCode MalformedJson { get; }

    /// <summary>
    /// 获取未知 action 码。
    /// </summary>
    public AdminApiErrorCode UnknownAction { get; }

    /// <summary>
    /// 获取作用域缺失码。
    /// </summary>
    public AdminApiErrorCode ScopeMissing { get; }

    /// <summary>
    /// 获取未处理异常码。
    /// </summary>
    public AdminApiErrorCode InternalError { get; }

    /// <summary>
    /// 获取「action 属于已禁用能力」码。
    /// </summary>
    public AdminApiErrorCode CapabilityNotInstalled { get; }
}
