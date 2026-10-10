// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
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
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Discovery.Routing;
using GameFrameX.Network.Abstractions;

namespace GameFrameX.Core.Session;

/// <summary>
/// 玩家运行时会话管理器契约。成员仅镜像 <see cref="PlayerSessionManager"/> 既有公共面（C192 接口化裁定），不补充新成员；
/// 获取路径为 <c>PlayerSessionManager.Instance</c>（locator 式静态入口，仓内无 DI 容器）。
/// </summary>
public interface IPlayerSessionManager
{
    /// <summary>
    /// 玩家路由外发同步目标（控制库写入钩子）。装配槽位由 Launcher 写入，引用面按本接口。
    /// </summary>
    IPlayerRouteSyncTarget PlayerRouteSyncTarget { get; set; }

    /// <summary>
    /// 获取当前在线玩家的数量。
    /// </summary>
    /// <returns>当前在线玩家的数量。</returns>
    int Count();

    /// <summary>
    /// 获取分页的玩家列表。
    /// </summary>
    /// <param name="pageSize">每页的玩家数量。</param>
    /// <param name="pageIndex">当前页的索引，从0开始。</param>
    /// <returns>指定页的玩家会话列表。</returns>
    List<IPlayerSession> GetPageList(int pageSize, int pageIndex);

    /// <summary>
    /// 按玩家ID踢下线，移除其会话。
    /// </summary>
    /// <param name="playerId">要踢掉的玩家ID。</param>
    void KickOffLineByPlayerId(long playerId);

    /// <summary>
    /// 根据玩家ID获取对应的会话对象。
    /// 会话对象必须已经存在才会返回。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <returns>对应的会话对象，如果不存在则返回null。</returns>
    IPlayerSession GetByPlayerId(long playerId);

    /// <summary>
    /// 根据会话ID获取连接的会话对象。
    /// </summary>
    /// <param name="sessionId">会话ID。</param>
    /// <returns>对应的会话对象，如果不存在则返回null。</returns>
    IPlayerSession Get(string sessionId);

    /// <summary>
    /// 根据指定的查询条件获取会话对象。
    /// </summary>
    /// <param name="predicate">查询条件的委托。</param>
    /// <returns>符合条件的会话对象，如果不存在则返回null。</returns>
    IPlayerSession Get(Func<IPlayerSession, bool> predicate);

    /// <summary>
    /// 根据指定的查询条件获取会话对象列表。
    /// </summary>
    /// <param name="predicate">查询条件的委托。</param>
    /// <returns>符合条件的会话对象列表。</returns>
    List<IPlayerSession> GetList(Func<IPlayerSession, bool> predicate);

    /// <summary>
    /// 移除指定会话ID的玩家。
    /// </summary>
    /// <param name="sessionId">要移除的会话ID。</param>
    /// <returns>被移除的会话对象，如果不存在则返回null。</returns>
    IPlayerSession Remove(string sessionId);

    /// <summary>
    /// 移除所有在线玩家的会话。
    /// </summary>
    /// <returns>一个表示异步操作的任务。</returns>
    Task RemoveAll();

    /// <summary>
    /// 获取指定会话ID的网络连接通道。
    /// </summary>
    /// <param name="sessionId">会话ID。</param>
    /// <returns>对应的网络连接通道，如果不存在则返回null。</returns>
    INetworkChannel GetChannel(string sessionId);

    /// <summary>
    /// 添加新的连接会话。
    /// </summary>
    /// <param name="session">要添加的会话对象。</param>
    void Add(IPlayerSession session);

    /// <summary>
    /// 更新会话，处理玩家ID和签名的更新。
    /// 如果玩家ID已在其他设备上登录，则先经通知器通知旧会话再关闭其连接。
    /// </summary>
    /// <param name="sessionId">会话ID，用于标识当前会话</param>
    /// <param name="playerId">玩家ID，表示当前会话所关联的玩家</param>
    /// <param name="sign">签名，用于验证会话的唯一性</param>
    /// <param name="duplicateLoginNotifier">
    /// 顶号通知器：向旧会话发送"账号已在其他设备登录"提示。通知必须在 Close 之前内联 await——
    /// 事件派发（Tell 入队）与 Close 存在竞态，异步入队可能晚于断连导致通知丢失；
    /// 顶号提示 RespPrompt 属游戏协议（Proto 在 Core 之上），故由调用方注入构造逻辑。必填参数使漏注入成为编译错误。
    /// </param>
    Task UpdateSession(string sessionId, long playerId, string sign, Func<IPlayerSession, Task> duplicateLoginNotifier);

    /// <summary>
    /// 获取玩家路由快照（内存态）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="snapshot">路由快照。</param>
    /// <returns>是否命中。</returns>
    bool TryGetPlayerRoute(long playerId, out SessionRouteSnapshot snapshot);

    /// <summary>
    /// 将玩家标记为在线并更新路由信息（内存态）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="serverType">服务器类型，默认当前服务。</param>
    /// <param name="serverId">服务器ID，默认当前服务。</param>
    void SetPlayerRouteOnline(long playerId, string serverType = null, int? serverId = null);

    /// <summary>
    /// 将玩家标记为离线（内存态）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    void SetPlayerRouteOffline(long playerId);
}
