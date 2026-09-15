//  ==========================================================================================
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

namespace GameFrameX.Online.Session;

/// <summary>
/// 会话开启结果（vault:C3 S2.4：签发即建会话；明文 Token 仅在本结果中出现一次）。
/// <para>
/// 维护约束：<see cref="Token"/> 为唯一一次明文透出——服务端只存 SHA-256 指纹，调用方须即刻交付客户端；
/// <see cref="ReplacedSessionIds"/> 为多端策略（SingleDevice 拒绝 / LatestWins 顶替）裁决时被关闭的旧会话，
/// 供调用方向旧端推送顶号提示；Coexist 策略下该列表为空。
/// </para>
/// </summary>
public sealed class OnlineSessionStartResult
{
    /// <summary>
    /// 获取新建的会话实体（状态 Authenticated，Token 字段为指纹非明文）。
    /// </summary>
    public OnlineSession Session
    {
        get;
        set;
    }

    /// <summary>
    /// 获取签发的明文 Token（仅此一次透出）。
    /// </summary>
    public string Token
    {
        get;
        set;
    }

    /// <summary>
    /// 获取 Token 过期时刻（Unix 毫秒）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取多端策略裁决中被顶替关闭的旧会话标识列表。
    /// </summary>
    public IReadOnlyList<string> ReplacedSessionIds
    {
        get;
        set;
    }
}
