// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System.Collections.Generic;

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 游戏事件指标快照（vault:C8 S7.5 / VC-7.10 的 Server 半边：**现场复算**的计数事实，不是累计账本）。
/// <para>
/// 维护约束（红线）：本对象是**某一时间窗内原始事件的纯函数**——同一窗口同一批原始事件复算必然得到同一结果
/// （无采样、无近似、无历史累计状态），「可复算偏差 0」因此是结构性成立而非统计意义上的近似；
/// 真实报表的上卷与留存归数据侧（S7.9），本对象只服务服务端可观测性与测试断言。
/// </para>
/// </summary>
public sealed class OnlineGameEventMetrics
{
    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置统计窗口起点（UTC 毫秒，含）。
    /// </summary>
    public long FromTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置统计窗口终点（UTC 毫秒，含）。
    /// </summary>
    public long ToTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置窗口内**已受理**事件总数（按事件发生时刻落在窗口内计）。
    /// </summary>
    public int TotalCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置按分类的事件计数（键为分类名，仅含计数大于 0 的分类）。
    /// </summary>
    public Dictionary<string, int> CountByCategory
    {
        get;
        set;
    } = new Dictionary<string, int>();

    /// <summary>
    /// 获取或设置按事件名的计数（键为事件名，仅含计数大于 0 的事件名；可枚举 17 个标准事件名逐个判定）。
    /// </summary>
    public Dictionary<string, int> CountByName
    {
        get;
        set;
    } = new Dictionary<string, int>();

    /// <summary>
    /// 获取或设置窗口内被 L0 拒绝并进入死信的**脏事件**数（摄取健康度的唯一信号：正常应为 0）。
    /// </summary>
    public int RejectedCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置窗口内的**活跃玩家数**（去重口径：窗口内发生过 <see cref="OnlineGameEventName.SessionStart"/>
    /// 的玩家标识数，玩家标识非正者不计——聚合事件的玩家位为 0，不是玩家）。
    /// 与计数类指标的差别是它**不是**某事件名的条数，故单独成项；胜率等比值指标可由
    /// <see cref="GetNameCount"/> 的 Win / Lose 计数派生，不在此固化比值口径。
    /// </summary>
    public int ActivePlayerCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取某个分类的事件计数（无记录返回 0，便于断言与报表直接取值）。
    /// </summary>
    /// <param name="category">事件分类。</param>
    /// <returns>该分类的事件计数。</returns>
    public int GetCategoryCount(OnlineGameEventCategory category)
    {
        return CountByCategory.TryGetValue(category.ToString(), out var count) ? count : 0;
    }

    /// <summary>
    /// 获取某个事件名的事件计数（无记录返回 0）。
    /// </summary>
    /// <param name="eventName">事件名。</param>
    /// <returns>该事件名的事件计数。</returns>
    public int GetNameCount(string eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return 0;
        }

        return CountByName.TryGetValue(eventName, out var count) ? count : 0;
    }
}
