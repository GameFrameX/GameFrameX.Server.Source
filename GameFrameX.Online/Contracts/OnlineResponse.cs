// ==========================================================================================
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

namespace GameFrameX.Online.Contracts;

/// <summary>
/// Online 公共响应结构（vault:C2 S1.2：任何响应至少包含 <c>Code</c>、<c>MessageKey</c>、<c>RequestId</c>、
/// <c>ServerTime</c>、<c>Data</c>，VC-1.2 响应字段完整性）。
/// <para>
/// 维护约束：<c>Code</c> 为机器可判断协议码（<see cref="OnlineErrorCode"/>），<c>MessageKey</c> 为可本地化键，
/// 双通道并存；客户端不得依赖中文文案判断流程。成功与失败响应都必须回显 <c>RequestId</c> 以支撑全链路追踪。
/// </para>
/// </summary>
/// <typeparam name="TData">业务数据载荷类型。</typeparam>
public sealed class OnlineResponse<TData>
{
    /// <summary>
    /// 获取或设置协议错误码（0=成功；非 0 见 <see cref="OnlineErrorCode"/> 八段分层）。
    /// </summary>
    public int Code
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置可本地化消息键（<c>Online.Error.{段名}.{成员名}</c>；成功时为空字符串）。
    /// </summary>
    public string MessageKey
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置请求标识回显（取自请求上下文 <c>RequestId</c>，支撑链路追踪）。
    /// </summary>
    public string RequestId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置服务端时间（UTC 毫秒；客户端本地时间校准与超时判定的基准）。
    /// </summary>
    public long ServerTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置业务数据载荷（失败响应允许为空）。
    /// </summary>
    public TData Data
    {
        get;
        set;
    }

    /// <summary>
    /// 构造成功响应（Code=0、MessageKey 为空、回显 RequestId）。
    /// </summary>
    /// <param name="requestId">请求标识回显。</param>
    /// <param name="serverTime">服务端 UTC 毫秒时间。</param>
    /// <param name="data">业务数据载荷。</param>
    /// <returns>成功响应实例。</returns>
    public static OnlineResponse<TData> Success(string requestId, long serverTime, TData data)
    {
        return new OnlineResponse<TData>
        {
            Code = (int)OnlineErrorCode.None,
            MessageKey = string.Empty,
            RequestId = requestId,
            ServerTime = serverTime,
            Data = data,
        };
    }

    /// <summary>
    /// 构造失败响应（MessageKey 由错误码派生，回显 RequestId）。
    /// </summary>
    /// <param name="errorCode">协议错误码。</param>
    /// <param name="requestId">请求标识回显。</param>
    /// <param name="serverTime">服务端 UTC 毫秒时间。</param>
    /// <returns>失败响应实例。</returns>
    public static OnlineResponse<TData> Fail(OnlineErrorCode errorCode, string requestId, long serverTime)
    {
        return new OnlineResponse<TData>
        {
            Code = (int)errorCode,
            MessageKey = errorCode.ToMessageKey(),
            RequestId = requestId,
            ServerTime = serverTime,
            Data = default,
        };
    }
}
