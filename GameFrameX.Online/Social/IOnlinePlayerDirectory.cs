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
/// 玩家目录查询契约（vault:C7 S6.2「好友支持搜索」的可插拔输入）。
/// <para>
/// 维护约束：Social 域需要「按名字找玩家」与「把玩家标识还原成展示名」两种能力，但这两项的数据归属
/// 在身份域（C94）。本契约是**窄接口**——只暴露搜索与名称解析，不暴露账号/设备/会话等身份域字段，
/// 使 Social 域依赖抽象而非身份域实现（依赖方向：Social → 本契约 ← 运行时装配注入身份域实现）。
/// </para>
/// <para>
/// 未装配时的降级语义由调用方决定：好友搜索返回空结果、展示名回退为标识字面量，
/// 不得因此阻断好友列表、关系变更等核心链路。
/// </para>
/// </summary>
public interface IOnlinePlayerDirectory
{
    /// <summary>
    /// 按展示名关键字检索玩家（作用域内）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="keyword">名称关键字（前后空白由实现忽略；空关键字不返回结果）。</param>
    /// <param name="limit">返回条数上限（调用方须传正值）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配的玩家目录条目（顺序由实现保证稳定）。</returns>
    Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> SearchByNameAsync(long tenantId, long appId, string keyword, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按玩家标识批量解析目录条目（好友列表展示名的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerIds">玩家标识集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析到的条目（不存在的标识不出现在结果中）。</returns>
    Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> FindAsync(long tenantId, long appId, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default);
}
