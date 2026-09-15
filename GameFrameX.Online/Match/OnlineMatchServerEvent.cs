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
/// 服务器事件（vault:C6 S5.2「事件广播」与增量补发的最小单元）。
/// <para>
/// 维护约束（红线）：<see cref="Sequence"/> 必须取自对局的服务端权威序号且全局单调——
/// 重连补发依赖「客户端最后确认序号 → 当前序号」这一区间切分（VC-5.6），
/// 任何绕过序号发布事件的写法都会让补发区间失真。
/// <see cref="Payload"/> 是玩法私有载荷（不透明字节），服务端不解释其内容。
/// </para>
/// </summary>
public sealed class OnlineMatchServerEvent
{
    /// <summary>
    /// 获取或设置服务器序号。
    /// </summary>
    public long Sequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件类型名（如 <c>RoundResolved</c>、<c>MemberJoined</c>）。
    /// </summary>
    public string EventType
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩法私有载荷（不透明字节；可为空）。
    /// </summary>
    public byte[] Payload
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置发生时刻（UTC 毫秒）。
    /// </summary>
    public long OccurredTime
    {
        get;
        set;
    }

    /// <summary>
    /// 复制事件（载荷逐字节拷贝，避免调用方改写已入日志的载荷）。
    /// </summary>
    /// <returns>事件副本。</returns>
    public OnlineMatchServerEvent Copy()
    {
        return new OnlineMatchServerEvent
        {
            Sequence = Sequence,
            EventType = EventType,
            Payload = Payload == null ? null : (byte[])Payload.Clone(),
            OccurredTime = OccurredTime,
        };
    }
}
