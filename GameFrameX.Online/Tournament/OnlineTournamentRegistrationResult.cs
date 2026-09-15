namespace GameFrameX.Online.Tournament;

/// <summary>
/// 报名登记落档结果（本次是否新登记 + 该玩家当前生效的登记）。
/// <para>
/// 维护约束（红线）：<see cref="IsNew"/> 是**存储层显式给出的判定**，调用方不得改用「比对时间戳 / 比对引用」
/// 之类的启发式反推重放——登记内容对同一次报名请求本就完全相同，任何基于内容差异的推断都会把
/// 并发重复报名误判为首次报名，进而重复发事件、重复计参赛人数。
/// </para>
/// </summary>
public sealed class OnlineTournamentRegistrationResult
{
    /// <summary>
    /// 获取或设置本次是否为新登记（false 表示该玩家此前已登记，本次为幂等重放）。
    /// </summary>
    public bool IsNew
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置该玩家当前生效的登记（新登记时为本次落档的登记，重放时为既有登记）。
    /// </summary>
    public OnlineTournamentRegistration Registration
    {
        get;
        set;
    }
}
