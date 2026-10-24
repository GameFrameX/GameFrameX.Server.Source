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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online;


/// <summary>
/// 好友域桥验证种子（C294 §4.6：生产配置键 <c>FriendBridgeSeedPlayerIds</c> 删除后，种子语义挪进测试夹具；
/// 语句级迁移自旧 <c>OnlineFriendDomainBridgeWiring.SeedFriendDomainBridgeVerificationAsync</c>——
/// 首元素为请求方、其余为好友，经真实 <see cref="OnlineFriendService"/> 写路径两两建立，幂等收敛）。
/// </summary>
public static class FriendBridgeVerificationSeed
{
    /// <summary>
    /// 经真实写路径两两建立种子好友关系（空 / 单元素列表为空操作；重复种子幂等命中既有关系）。
    /// </summary>
    /// <param name="host">已装配能力宿主。</param>
    /// <param name="friendPlayerIds">种子标识列表（首元素 = 请求方，其余 = 好友）。</param>
    /// <returns>成功建立的关系数。</returns>
    public static async Task<int> SeedAsync(CapabilityHost host, IReadOnlyList<long> friendPlayerIds)
    {
        Assert.NotNull(host);
        if (friendPlayerIds == null || friendPlayerIds.Count < 2)
        {
            return 0;
        }

        var friends = host.Services.GetOptionalService<OnlineFriendService>();
        if (friends == null)
        {
            return 0;
        }

        var requesterId = friendPlayerIds[0];
        var established = 0;
        for (var index = 1; index < friendPlayerIds.Count; index++)
        {
            var friendPlayerId = friendPlayerIds[index];
            if (friendPlayerId <= 0 || friendPlayerId == requesterId)
            {
                continue;
            }

            var requested = await friends.RequestAsync(new OnlineScope(host.TenantId, host.AppId, host.ServerId, requesterId), friendPlayerId);
            if (!requested.IsSuccess)
            {
                continue;
            }

            var accepted = await friends.AcceptAsync(new OnlineScope(host.TenantId, host.AppId, host.ServerId, friendPlayerId), requested.Data.FriendshipId);
            if (!accepted.IsSuccess && accepted.Code != OnlineErrorCode.StateOperationForbidden)
            {
                continue;
            }

            established++;
        }

        return established;
    }
}
