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
/// Online 公共请求上下文校验器（vault:C2：缺上下文请求被拒 VC-1.1，副作用请求缺幂等键被拒）。
/// <para>
/// 维护约束：所有 Online 消息入口必须先经本校验器再进入业务处理，禁止各 Handler 自行放宽；
/// 校验失败统一映射 <see cref="OnlineErrorCode.ParameterInvalid"/>（4xxx 段），拒绝原因随日志留痕。
/// </para>
/// </summary>
public static class OnlineRequestContextValidator
{
    /// <summary>
    /// 服务端最低支持的协议版本（vault:C2 协议从 1 起算；上调属破坏性变更，须回 vault 契约评审）。
    /// </summary>
    public const int MinimumProtocolVersion = 1;

    /// <summary>
    /// 幂等键最大长度（字符数）。
    /// </summary>
    public const int MaximumIdempotencyKeyLength = 128;

    /// <summary>
    /// 校验请求上下文的完整性与幂等键格式。
    /// </summary>
    /// <param name="context">待校验的请求上下文。</param>
    /// <param name="hasSideEffect">是否副作用请求（true 时幂等键必填并校验格式）。</param>
    /// <returns>校验通过返回 <see langword="null"/>；失败返回 4xxx 段错误码。</returns>
    public static OnlineErrorCode? Validate(OnlineRequestContext context, bool hasSideEffect)
    {
        if (context == null)
        {
            return OnlineErrorCode.ParameterInvalid;
        }

        if (string.IsNullOrWhiteSpace(context.RequestId))
        {
            return OnlineErrorCode.ParameterInvalid;
        }

        if (context.ProtocolVersion < MinimumProtocolVersion)
        {
            return OnlineErrorCode.ParameterInvalid;
        }

        if (hasSideEffect)
        {
            if (!IsIdempotencyKeyValid(context.IdempotencyKey))
            {
                return OnlineErrorCode.ParameterInvalid;
            }
        }

        return null;
    }

    /// <summary>
    /// 校验幂等键格式：非空、长度不超过上限、仅允许字母/数字/下划线/连字符。
    /// <para>
    /// 维护约束：键格式由服务端强制（vault:C2 风险表「幂等键由客户端任意生成」缓解项），
    /// 且幂等记录按作用域绑定（见 <c>OnlineIdempotencyService</c>），禁止跨玩家复用。
    /// </para>
    /// </summary>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <returns>格式合法返回 <c>true</c>。</returns>
    public static bool IsIdempotencyKeyValid(string idempotencyKey)
    {
        if (string.IsNullOrEmpty(idempotencyKey) || idempotencyKey.Length > MaximumIdempotencyKeyLength)
        {
            return false;
        }

        foreach (var ch in idempotencyKey)
        {
            var isAllowed = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }
}
