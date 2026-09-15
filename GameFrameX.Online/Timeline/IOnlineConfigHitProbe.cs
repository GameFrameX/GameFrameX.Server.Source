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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.Timeline;

/// <summary>
/// 配置命中探针（玩家时间线「运营配置」腿的可插拔数据源）。
/// <para>
/// 维护约束（红线）：本仓不存在配置命中记录存储（配置域归配置 / 运营侧），故按 C97 <c>IOnlinePartyPresenceProbe</c> /
/// C99 <c>IOnlinePlayerDirectory</c> 的**可空探针**先例定义窄接口——只暴露「按玩家列出配置命中」一项能力，
/// 运行时装配由 Server 仓接到真实配置域（X4：运行时装配归 Server 仓）。测试以确定性假实现替换。
/// </para>
/// <para>
/// 未装配（构造参数为 null）时该腿返回**空槽**：时间线不得从其它域推断补齐配置命中——
/// 推断值与真实配置域必然漂移且无法追溯，对齐消费方「缺事件即空槽展示，不做本地推断」的红线。
/// 探针必须只读：实现方不得通过本接口写入配置域。
/// </para>
/// </summary>
public interface IOnlineConfigHitProbe
{
    /// <summary>
    /// 列出指定玩家在作用域内的配置命中记录。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中记录（无命中返回空集合；顺序由实现保证稳定，时间线侧自行全量排序）。</returns>
    Task<IReadOnlyList<OnlineConfigHitRecord>> ListHitsAsync(long tenantId, long appId, long serverId, long playerId, CancellationToken cancellationToken = default);
}
