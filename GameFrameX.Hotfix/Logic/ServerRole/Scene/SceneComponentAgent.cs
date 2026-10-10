// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
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
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
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

using GameFrameX.Apps.ServerRole.Scene.Component;
using GameFrameX.Apps.ServerRole.Scene.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Scene;

/// <summary>
/// 场景在线服务器（Scene Role）业务组件代理：进入/离开/查询场景在线列表。
/// </summary>
/// <remarks>
/// Scene Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="SceneRules"/>（纯函数，可单测）；
/// 本类只做状态编排：场景懒创建、幂等进入、离开移除与在线列表回填。
/// </remarks>
public class SceneComponentAgent : StateComponentAgent<SceneComponent, SceneState>
{
    /// <summary>
    /// 进入场景：场景懒创建（首位进入者建场景）；重复进入幂等；已满返回 <see cref="SceneErrorCode.SceneFull"/>。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">进入请求。</param>
    /// <param name="response">进入响应。</param>
    public Task OnEnterAsync(long playerId, ReqSceneEnter request, RespSceneEnter response)
    {
        var scene = GetOrCreateScene(request.SceneId);
        if (!scene.Players.Contains(playerId))
        {
            if (!SceneRules.CanEnter(scene.Players.Count))
            {
                response.ErrorCode = (int)SceneErrorCode.SceneFull;
                return Task.CompletedTask;
            }

            scene.Players.Add(playerId);
        }

        response.PlayerCount = scene.Players.Count;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 离开场景：不在该场景返回 <see cref="SceneErrorCode.NotInScene"/>；离开返回剩余人数。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">离开请求。</param>
    /// <param name="response">离开响应。</param>
    public Task OnLeaveAsync(long playerId, ReqSceneLeave request, RespSceneLeave response)
    {
        if (!State.Scenes.TryGetValue(request.SceneId, out var scene) || !scene.Players.Contains(playerId))
        {
            response.ErrorCode = (int)SceneErrorCode.NotInScene;
            return Task.CompletedTask;
        }

        scene.Players.Remove(playerId);
        response.PlayerCount = scene.Players.Count;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询场景在线列表：场景不存在时返回 0 人空列表。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">查询请求。</param>
    /// <param name="response">查询响应。</param>
    public Task OnQueryAsync(long playerId, ReqSceneQuery request, RespSceneQuery response)
    {
        if (State.Scenes.TryGetValue(request.SceneId, out var scene))
        {
            response.PlayerCount = scene.Players.Count;
            foreach (var id in scene.Players)
            {
                response.PlayerIds.Add(id);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询场景状态，不存在时按需懒创建。
    /// </summary>
    /// <param name="sceneId">场景ID。</param>
    /// <returns>场景状态。</returns>
    private SceneStateItem GetOrCreateScene(long sceneId)
    {
        if (!State.Scenes.TryGetValue(sceneId, out var scene))
        {
            scene = new SceneStateItem { SceneId = sceneId };
            State.Scenes[sceneId] = scene;
        }

        return scene;
    }
}
