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
/// Online 公共请求上下文（vault:C2 S1.2：任何 Online 请求至少包含 <c>RequestId</c>、<c>ProtocolVersion</c>、
/// <c>ClientVersion</c>、<c>Timestamp</c>；产生副作用的请求必须携带 <c>IdempotencyKey</c>）。
/// <para>
/// 维护约束：<c>TenantId</c>/<c>AppId</c>/<c>ServerId</c> 以鉴权或服务端路由为准，不由本上下文的客户端字段直接覆盖
/// （作用域统一经 <c>OnlineScope</c> 由服务端管线注入）；<c>RequestId</c> 标识一次请求尝试，
/// <c>IdempotencyKey</c> 标识一次业务意图，二者语义不同不可混用。
/// </para>
/// </summary>
public sealed class OnlineRequestContext
{
    /// <summary>
    /// 获取或设置请求标识（一次请求尝试的唯一标识，贯穿日志与响应回显）。
    /// </summary>
    public string RequestId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置协议版本（Online 公共契约协议版本号，从 1 起算；低于服务端最低支持版本将被拒绝）。
    /// </summary>
    public int ProtocolVersion
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置客户端版本（调用方自报版本字符串，仅供灰度与兼容统计，不参与鉴权判定）。
    /// </summary>
    public string ClientVersion
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置请求时间戳（UTC 毫秒；服务端按容忍窗口校验偏移，超窗拒绝防重放）。
    /// </summary>
    public long Timestamp
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置幂等键（一次业务意图的标识；仅副作用请求必填，格式见 <c>OnlineRequestContextValidator</c>）。
    /// </summary>
    public string IdempotencyKey
    {
        get;
        set;
    }

    /// <summary>
    /// 将上下文重置为默认值（复用场景）。
    /// </summary>
    public void Clear()
    {
        RequestId = null;
        ProtocolVersion = default;
        ClientVersion = null;
        Timestamp = default;
        IdempotencyKey = null;
    }
}
