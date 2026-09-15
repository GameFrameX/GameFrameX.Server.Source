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

namespace GameFrameX.Online.Idempotency;

/// <summary>
/// Online 幂等审计记录（谁在什么时候、以什么键、得到什么判定；不含明文请求/响应体）。
/// </summary>
public sealed class OnlineIdempotencyAuditRecord
{
    /// <summary>
    /// 获取或设置判定类型。
    /// </summary>
    public OnlineIdempotencyOutcomeKind OutcomeKind
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置作用域绑定键（可追溯到租户/App/区服[/玩家]）。
    /// </summary>
    public string ScopeKey
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置幂等键（业务意图标识）。
    /// </summary>
    public string IdempotencyKey
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置请求摘要（SHA-256 十六进制，不可逆；用于冲突比对不还原内容）。
    /// </summary>
    public string RequestDigest
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置判定时刻（UTC 毫秒）。
    /// </summary>
    public long OccurredTime
    {
        get;
        set;
    }
}
