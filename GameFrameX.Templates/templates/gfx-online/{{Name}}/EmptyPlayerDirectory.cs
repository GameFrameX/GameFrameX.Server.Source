// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws and regulations of the People's Republic of China and relevant international regulations.
//
//  使用本项目须严格遵守相应法律法规与开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file at the root of the source code for the full license text.
//
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯其他合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations.
//
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes or liabilities arising from secondary development of this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;

namespace {{Name}};

/// <summary>
/// 最小宿主的玩家目录（满足 online-social 以 RequireHostService 声明的宿主扩展点）。
/// <para>
/// 维护约束：本宿主不接玩家库，搜索与查找恒返回空集合——好友搜索无结果、好友展示名为空，
/// 其余社交功能不受影响。接入真实玩家数据时替换为读取玩家库的实现，并在 Program.cs 的 Override 处更换实例。
/// </para>
/// </summary>
public sealed class EmptyPlayerDirectory : IOnlinePlayerDirectory
{
    /// <inheritdoc />
    public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> SearchByNameAsync(long tenantId, long appId, string keyword, int limit, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(new List<OnlinePlayerDirectoryEntry>());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> FindAsync(long tenantId, long appId, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(new List<OnlinePlayerDirectoryEntry>());
    }
}
