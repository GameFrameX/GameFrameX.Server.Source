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

namespace GameFrameX.Online.Session;

using GameFrameX.Online.Identity;

/// <summary>
/// 会话实体（vault:C3 S2.4：一次连接与鉴权上下文，含连接映射、Token 指纹与重连窗口）。
/// <para>
/// 维护约束：Token 只存 SHA-256 指纹（<see cref="CurrentTokenHash"/>），明文 Token 不落存储；
/// 刷新为原子轮换——旧指纹即刻失效（<see cref="TokenGeneration"/> 递增，重放按 6xxx 冲突）；
/// 进入终态后指纹清空、<see cref="CloseReason"/> 必填；状态推进单写者纪律见 <see cref="OnlineSessionState"/>。
/// </para>
/// </summary>
public sealed class OnlineSession
{
    /// <summary>
    /// 获取或设置会话标识（"sess-" 前缀，服务端生成）。
    /// </summary>
    public string Id
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置应用标识。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区服标识（会话与 Token 的作用域含 Server 位）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置归属游戏账号标识。
    /// </summary>
    public long GameAccountId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置登录设备标识（可空；换绑与多端策略审计用）。
    /// </summary>
    public string DeviceId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置当前连接映射标识（网络通道标识；未连接为空）。
    /// </summary>
    public string ConnectionId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置会话生命周期状态。
    /// </summary>
    public OnlineSessionState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置签发时快照的多端登录策略（策略只影响本会话的裁决口径）。
    /// </summary>
    public OnlineMultiDevicePolicy MultiDevicePolicy
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置当前 Token 指纹（SHA-256 十六进制；终态后为空）。
    /// </summary>
    public string CurrentTokenHash
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 Token 轮换代数（签发为 1，每次刷新递增；审计与重放判定辅助）。
    /// </summary>
    public int TokenGeneration
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 Token 有效期快照（秒；刷新按原有效期滑动续期）。
    /// </summary>
    public long TokenTimeToLiveSeconds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 Token 过期时间（Unix 毫秒）。
    /// </summary>
    public long TokenExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时间（Unix 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置鉴权时间（Unix 毫秒）。
    /// </summary>
    public long AuthenticatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置连接建立时间（Unix 毫秒）。
    /// </summary>
    public long ConnectedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近心跳时间（Unix 毫秒）。
    /// </summary>
    public long LastHeartbeatAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置断线时间（Unix 毫秒；0 = 未断线）。
    /// </summary>
    public long DisconnectAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置重连窗口截止时间（Unix 毫秒；0 = 无窗口；超窗由清理任务转终态）。
    /// </summary>
    public long ReconnectDeadlineAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置终态时间（Unix 毫秒；0 = 未终态）。
    /// </summary>
    public long ClosedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置终态原因码（终态时必填）。
    /// </summary>
    public OnlineSessionCloseReason CloseReason
    {
        get;
        set;
    }
}
