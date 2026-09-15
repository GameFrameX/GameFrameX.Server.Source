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

namespace GameFrameX.Online.Social;

/// <summary>
/// 好友列表条目（vault:C7 S6.2：好友列表需带在线状态与展示名）。
/// <para>
/// 维护约束：在线状态是**读取时的即时事实**，不落库、不缓存进好友关系记录——关系是持久事实，
/// 在线与否是 Presence 域的瞬时状态，二者混存会出现「好友已下线但关系记录仍写在线」的陈旧读。
/// 未装配在线探针时 <see cref="Online"/> 恒为 <c>false</c>（降级为「未知」而非误报在线）。
/// </para>
/// </summary>
public sealed class OnlineFriendSummary
{
    /// <summary>
    /// 获取或设置好友玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置好友展示名（未装配玩家目录时回退为标识字面量）。
    /// </summary>
    public string Name
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置好友归属区服标识。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置好友当前是否在线（读取时事实；无在线探针时恒为 <c>false</c>）。
    /// </summary>
    public bool Online
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成为好友的时刻（UTC 毫秒）。
    /// </summary>
    public long EstablishedAtTime
    {
        get;
        set;
    }
}
