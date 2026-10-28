// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
// 
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本组织及其贡献者概不承担。
//  shall be borne by the developer; the project organization and contributors assume no responsibility.
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  CNB  仓库：https://cnb.cool/GameFrameX
//  CNB Repository:    https://cnb.cool/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Threading.Tasks;
using GameFrameX.Core.BaseHandler.RPC;

namespace GameFrameX.Hotfix.Logic.ServerRole.Friend;

/// <summary>
/// 接受好友申请处理器。
/// </summary>
[MessageMapping(typeof(ReqFriendAccept))]
internal sealed class ReqFriendAcceptHandler : BaseRpcMessageHandler<ReqFriendAccept, RespFriendAccept>
{
    protected override async Task ActionAsync(ReqFriendAccept request, RespFriendAccept response, CancellationToken cancellationToken)
    {
        var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetworkChannel);
        if (playerId == 0)
        {
            response.ErrorCode = (int)FriendErrorCode.NotLoggedIn;
            return;
        }

        await GameFrameX.Hotfix.Logic.Player.Friend.FriendDomainBridge.AcceptAsync(playerId, request.FromPlayerId, response);
    }
}
