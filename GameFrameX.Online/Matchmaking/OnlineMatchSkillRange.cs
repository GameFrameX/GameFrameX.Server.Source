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
/// 技术水平区间（vault:C5 S4.5：SkillRange 字段；S4.6：等级段位范围匹配与等待时间扩展）。
/// <para>
/// 维护约束：区间语义为闭区间 <c>[Min, Max]</c>；<see cref="Widen"/> 的下界截断在 0，
/// 不允许产生负值（负分段会让「越等越宽」在低分段退化为无意义区间）。
/// </para>
/// </summary>
public sealed class OnlineMatchSkillRange
{
    /// <summary>
    /// 获取或设置区间下界。
    /// </summary>
    public int Min
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区间上界。
    /// </summary>
    public int Max
    {
        get;
        set;
    }

    /// <summary>
    /// 判定区间是否覆盖指定值。
    /// </summary>
    /// <param name="value">待判定值。</param>
    /// <returns>覆盖返回 <c>true</c>。</returns>
    public bool Contains(int value)
    {
        return value >= Min && value <= Max;
    }

    /// <summary>
    /// 判定两个区间是否有交集（成组的最低条件）。
    /// </summary>
    /// <param name="other">另一区间。</param>
    /// <returns>有交集返回 <c>true</c>。</returns>
    public bool Intersects(OnlineMatchSkillRange other)
    {
        if (other == null)
        {
            return false;
        }

        return Min <= other.Max && other.Min <= Max;
    }

    /// <summary>
    /// 双侧扩宽区间（等待时间扩展的实现；下界截断在 0）。
    /// </summary>
    /// <param name="amount">单侧扩宽量（必须非负）。</param>
    /// <returns>扩宽后的新区间。</returns>
    public OnlineMatchSkillRange Widen(int amount)
    {
        var widenedMin = Min - amount;
        if (widenedMin < 0)
        {
            widenedMin = 0;
        }

        return new OnlineMatchSkillRange
        {
            Min = widenedMin,
            Max = Max + amount,
        };
    }

    /// <summary>
    /// 复制区间。
    /// </summary>
    /// <returns>区间副本。</returns>
    public OnlineMatchSkillRange Copy()
    {
        return new OnlineMatchSkillRange
        {
            Min = Min,
            Max = Max,
        };
    }
}
