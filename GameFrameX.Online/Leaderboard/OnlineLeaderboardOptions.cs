// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
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

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜组件选项（防刷阈值 / 限流窗口 / 读缓存；运行时装配注入，缺省值即安全默认）。
/// <para>
/// 维护约束：阈值调整只影响「拦多拦少」，不得改变拦截语义（越界 → 风控拒绝、高频 → 限流拒绝）；
/// 压测需同时验证「高频被拦」与「正常节奏不受影响」两半（VC-7.4，阈值口径以 vault L6 为准）。
/// 缓存 TTL 只影响读延迟不影响正确性——任何写榜立即失效对应榜单缓存。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardOptions
{
    /// <summary>
    /// 获取或设置单次提交分数合理上限的全局默认值（榜单未显式配置时生效；取绝对值比较，正负同界）。
    /// </summary>
    public long DefaultMaxScorePerSubmission
    {
        get;
        set;
    } = 1_000_000;

    /// <summary>
    /// 获取或设置限流窗口内允许的提交次数（按 玩家 分桶；仅首次执行分支计数，幂等重放不烧配额）。
    /// </summary>
    public int RateLimitMaxSubmissions
    {
        get;
        set;
    } = 120;

    /// <summary>
    /// 获取或设置限流滑动窗口时长（秒）。
    /// </summary>
    public int RateLimitWindowSeconds
    {
        get;
        set;
    } = 60;

    /// <summary>
    /// 获取或设置是否启用 Top N 读缓存。
    /// </summary>
    public bool CacheEnabled
    {
        get;
        set;
    } = true;

    /// <summary>
    /// 获取或设置读缓存 TTL（秒；过期后下次查询重建快照，写榜立即失效不受 TTL 约束）。
    /// </summary>
    public int CacheTtlSeconds
    {
        get;
        set;
    } = 5;

    /// <summary>
    /// 获取或设置单页条目数上限（防一次拉全榜；请求超出按上限截断）。
    /// </summary>
    public int MaxPageSize
    {
        get;
        set;
    } = 200;
}
