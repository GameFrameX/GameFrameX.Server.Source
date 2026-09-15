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

namespace GameFrameX.Online.Match;

/// <summary>
/// 输入确认（vault:C6 S5.2「服务端发送快照、增量、事件广播和输入确认」）。
/// <para>
/// 维护约束（红线）：<see cref="ServerSequence"/> 是服务端权威序号——
/// 只有被接受的输入才推进它；被拒绝（含重复包）的输入原样返回当前序号，
/// 客户端据此得知「我的这一步没有被采纳」而不会自行推演（VC-5.4）。
/// </para>
/// </summary>
public sealed class OnlineMatchInputAck
{
    /// <summary>
    /// 获取或设置是否被接受。
    /// </summary>
    public bool Accepted
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置确认后的服务器序号（被拒绝时为对局当前序号，未被推进）。
    /// </summary>
    public long ServerSequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置对应的客户端序号（回显，便于客户端配对）。
    /// </summary>
    public long ClientSequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置拒绝原因（被接受时为 <see cref="OnlineMatchInputRejection.None"/>）。
    /// </summary>
    public OnlineMatchInputRejection Rejection
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否为重复包（重复包按幂等处理：不生效、不算失败，VC-5.4）。
    /// </summary>
    public bool IsDuplicate
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置可读说明（诊断用，不参与判定）。
    /// </summary>
    public string Message
    {
        get;
        set;
    }
}
