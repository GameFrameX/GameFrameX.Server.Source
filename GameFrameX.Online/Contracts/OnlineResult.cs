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
/// Online 服务层统一操作结果（vault:C3 服务端半边：业务可预期失败段位化返回，不抛异常）。
/// <para>
/// 维护约束：<see cref="Code"/> 复用 <see cref="OnlineErrorCode"/> 分段，不新增码段；
/// <see cref="IsSuccess"/> 仅在 Code == <see cref="OnlineErrorCode.None"/> 时为真；失败时 <see cref="Data"/> 为 null，
/// 调用方不得在失败分支读取 Data；<see cref="Message"/> 为服务端内部语义描述，不直接透出客户端
/// （客户端消费 Code + MessageKey 双通道，见 C93 <c>OnlineErrorCodeExtensions</c>）。
/// </para>
/// </summary>
/// <typeparam name="TData">成功负载数据类型。</typeparam>
public sealed class OnlineResult<TData>
{
    /// <summary>
    /// 获取协议错误码（<see cref="OnlineErrorCode.None"/> 表示成功）。
    /// </summary>
    public OnlineErrorCode Code
    {
        get;
    }

    /// <summary>
    /// 获取失败描述（服务端内部语义；成功时为空字符串）。
    /// </summary>
    public string Message
    {
        get;
    }

    /// <summary>
    /// 获取成功负载数据；失败时为 null。
    /// </summary>
    public TData Data
    {
        get;
    }

    /// <summary>
    /// 获取操作是否成功。
    /// </summary>
    public bool IsSuccess
    {
        get
        {
            return Code == OnlineErrorCode.None;
        }
    }

    /// <summary>
    /// 初始化 <see cref="OnlineResult{TData}"/>（仅供工厂方法与派生组装使用）。
    /// </summary>
    /// <param name="code">协议错误码。</param>
    /// <param name="message">失败描述。</param>
    /// <param name="data">成功负载数据。</param>
    private OnlineResult(OnlineErrorCode code, string message, TData data)
    {
        Code = code;
        Message = message;
        Data = data;
    }

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    /// <param name="data">成功负载数据。</param>
    /// <returns>成功结果实例。</returns>
    public static OnlineResult<TData> Ok(TData data)
    {
        return new OnlineResult<TData>(OnlineErrorCode.None, string.Empty, data);
    }

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="code">协议错误码（不得传 <see cref="OnlineErrorCode.None"/>）。</param>
    /// <param name="message">失败描述。</param>
    /// <returns>失败结果实例。</returns>
    public static OnlineResult<TData> Fail(OnlineErrorCode code, string message)
    {
        return new OnlineResult<TData>(code, message ?? string.Empty, default(TData));
    }
}
