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
/// <see cref="OnlineErrorCode"/> 的协议派生规则：MessageKey 命名、段判定与未知码兜底。
/// <para>
/// 维护约束：MessageKey 格式 <c>Online.Error.{段名}.{成员名}</c>（段名固定为
/// System/Session/Scope/Param/Business/Idempotency/RateLimit/Transient 八个，与 Admin
/// ErrorSegmentRegistry 段登记名去掉 Online 前缀一一对应）；客户端不得依赖中文文案判断流程，
/// 只允许按 <c>Code</c>（机器可判断）与 <c>MessageKey</c>（可本地化）双通道处理（vault:C2）。
/// </para>
/// </summary>
public static class OnlineErrorCodeExtensions
{
    /// <summary>
    /// 段名数组，下标 = 协议段号（1～8），供 MessageKey 组装与段名查询复用。
    /// </summary>
    private static readonly string[] SegmentNames = new string[]
    {
        string.Empty,
        "System",
        "Session",
        "Scope",
        "Param",
        "Business",
        "Idempotency",
        "RateLimit",
        "Transient",
    };

    /// <summary>
    /// 计算错误码的可本地化 MessageKey（<c>Online.Error.{段名}.{成员名}</c>，成功码返回 <see cref="string.Empty"/>）。
    /// </summary>
    /// <param name="errorCode">协议错误码。</param>
    /// <returns>MessageKey 字符串；<see cref="OnlineErrorCode.None"/> 返回空字符串。</returns>
    public static string ToMessageKey(this OnlineErrorCode errorCode)
    {
        if (errorCode == OnlineErrorCode.None)
        {
            return string.Empty;
        }

        var segment = (int)errorCode / 1000;
        if (segment < 1 || segment >= SegmentNames.Length)
        {
            return OnlineErrorCode.InternalError.ToMessageKey();
        }

        return "Online.Error." + SegmentNames[segment] + "." + errorCode;
    }

    /// <summary>
    /// 计算错误码所属的协议段号（1～8）。
    /// </summary>
    /// <param name="errorCode">协议错误码。</param>
    /// <returns>段号；成功码与段外未知码返回 1（系统段，与兜底映射一致）。</returns>
    public static int ToSegment(this OnlineErrorCode errorCode)
    {
        var segment = (int)errorCode / 1000;
        if (segment < 1 || segment > 8)
        {
            return 1;
        }

        return segment;
    }

    /// <summary>
    /// 将任意整型协议码解析为受支持的错误码：段外或段内未登记的未知码兜底映射到 <see cref="OnlineErrorCode.InternalError"/>。
    /// <para>消费端（客户端/网关）必须经本方法消费协议码，保证未知码不崩溃、不误判成功（VC-1.10 Server 半边约定）。</para>
    /// </summary>
    /// <param name="code">原始整型协议码。</param>
    /// <returns>受支持的错误码；0 映射 <see cref="OnlineErrorCode.None"/>。</returns>
    public static OnlineErrorCode Resolve(int code)
    {
        if (code == 0)
        {
            return OnlineErrorCode.None;
        }

        if (Enum.IsDefined(typeof(OnlineErrorCode), code))
        {
            return (OnlineErrorCode)code;
        }

        return OnlineErrorCode.InternalError;
    }
}
