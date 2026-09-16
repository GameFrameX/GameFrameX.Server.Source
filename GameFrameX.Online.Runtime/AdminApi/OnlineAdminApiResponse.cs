// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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

using GameFrameX.Online.Contracts;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// Online admin API 内层业务响应体（vault:C2 S1.2 公共响应结构的 admin 线缆形态，对齐 Admin 侧 <c>OnlineApiResponse&lt;T&gt;</c>）。
/// <para>
/// 维护约束：物理传输外层为 Foundation <c>HttpJsonResultData</c> 信封（code=0），本类型序列化为 JSON 字符串后置于信封
/// <c>Data</c> 字段（Admin 侧 <c>ToHttpJsonResult</c> 双层解包契约）；机器判断走 <see cref="Code"/>，本地化走
/// <see cref="MessageKey"/>；成功响应的 <see cref="Data"/> 必须非 null（Admin 解包对 null Data 抛基础设施异常）。
/// </para>
/// </summary>
public sealed class OnlineAdminApiResponse
{
    /// <summary>
    /// 获取或设置协议业务码（0=成功；非 0 按 <see cref="Contracts.OnlineErrorCode"/> 八段分层）。
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
    } = string.Empty;

    /// <summary>
    /// 获取或设置诊断消息（服务端原文，仅日志/排查用，客户端不得依赖其文案判断流程）。
    /// </summary>
    public string Message
    {
        get;
        set;
    } = string.Empty;

    /// <summary>
    /// 获取或设置请求标识回显（取自请求 <c>RequestId</c>，支撑链路追踪；请求未携带时由服务端生成）。
    /// </summary>
    public string RequestId
    {
        get;
        set;
    } = string.Empty;

    /// <summary>
    /// 获取或设置服务端时间（Unix 秒；Admin 线缆契约）。
    /// </summary>
    public long ServerTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置业务数据载荷（成功时必须非 null）。
    /// </summary>
    public object Data
    {
        get;
        set;
    }

    /// <summary>
    /// 构造成功响应。
    /// </summary>
    /// <param name="data">业务数据载荷（必须非 null）。</param>
    /// <param name="requestId">请求标识回显。</param>
    /// <param name="serverTime">服务端时间（UTC 毫秒）。</param>
    /// <returns>成功响应实例。</returns>
    public static OnlineAdminApiResponse Ok(object data, string requestId, long serverTime)
    {
        return new OnlineAdminApiResponse
        {
            Code = 0,
            MessageKey = string.Empty,
            Message = string.Empty,
            RequestId = requestId,
            ServerTime = serverTime,
            Data = data,
        };
    }

    /// <summary>
    /// 构造失败响应（MessageKey 由错误码按 <c>Online.Error.{段名}.{成员名}</c> 规则派生）。
    /// </summary>
    /// <param name="code">协议错误码。</param>
    /// <param name="message">诊断消息（服务端原文）。</param>
    /// <param name="requestId">请求标识回显。</param>
    /// <param name="serverTime">服务端时间（UTC 毫秒）。</param>
    /// <returns>失败响应实例（Data 为 null）。</returns>
    public static OnlineAdminApiResponse Fail(Contracts.OnlineErrorCode code, string message, string requestId, long serverTime)
    {
        return new OnlineAdminApiResponse
        {
            Code = (int)code,
            MessageKey = code.ToMessageKey(),
            Message = message ?? string.Empty,
            RequestId = requestId,
            ServerTime = serverTime,
            Data = null,
        };
    }
}
