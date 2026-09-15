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

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 对局列表项发现查询（vault:C5 S4.4：标签、容量与状态查询）。
/// <para>
/// 维护约束：各过滤项为 null 表示「不过滤」，不做隐式默认值填充——例如不设 <c>Mode</c> 即跨模式返回，
/// 而不是默认匹配 0。结果条数由 <see cref="Limit"/> 封顶，防止发现接口被当作全量导出使用。
/// </para>
/// </summary>
public sealed class OnlineMatchListingQuery
{
    /// <summary>
    /// 获取或设置玩法模式过滤（null 不过滤）。
    /// </summary>
    public int? Mode
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区域过滤（null 不过滤）。
    /// </summary>
    public int? Region
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置加入策略过滤（null 不过滤）。
    /// </summary>
    public OnlineJoinPolicy? JoinPolicy
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置标签过滤（null 或空表示不过滤；命中任一标签即匹配）。
    /// </summary>
    public List<string> Tags
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否包含已满/已关闭项（默认 <c>false</c>，只返回可加入项）。
    /// </summary>
    public bool IncludeUnavailable
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置返回条数上限（默认 50，必须为正）。
    /// </summary>
    public int Limit
    {
        get;
        set;
    }
}
