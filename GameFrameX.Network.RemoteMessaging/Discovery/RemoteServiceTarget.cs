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
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Network.RemoteMessaging.Discovery;

/// <summary>
/// 服务器间调用的结构化端点目标（承载服务类型 + 可选实例标识），取代 <c>{Role}_{InstanceId}</c> 字符串拼接拨号协议。
/// </summary>
/// <remarks>
/// The structured endpoint target of inter-server calls (server type + optional instance id),
/// replacing the former <c>{Role}_{InstanceId}</c> string-concatenation dialing protocol.
/// <see cref="ServerType"/> 必填、由 <see cref="Contracts.RemoteCallContext.ServiceName"/> 赋值；
/// <see cref="InstanceId"/> 可空——为空表示 ServerType 级确定性选择（Active 实例中 InstanceId 序数最小者），
/// 非空表示直达指定实例（Instance 视图含 Draining，下线中实例上的在途投递仍合法）。
/// <para>
/// <see cref="ToString"/> 产出熔断器与端点健康评分的统计键：ServerType 级为 <c>Social</c>，
/// 实例级为 <c>Social/2001</c>——实例级失败只熔断该实例，不牵连同 ServerType 的兄弟实例与 ServerType 级调用。
/// 两个分量都禁止包含 <c>/</c>（构造期校验），否则键的分量边界产生歧义。
/// </para>
/// </remarks>
public sealed class RemoteServiceTarget
{
    /// <summary>
    /// 初始化端点目标。
    /// </summary>
    /// <remarks>
    /// Initializes the target. <paramref name="serverType"/> must be non-blank and contain no
    /// <c>/</c>; a non-empty <paramref name="instanceId"/> is validated the same way.
    /// </remarks>
    /// <param name="serverType">服务进程类型名（必填）/ The server process type name (required)</param>
    /// <param name="instanceId">实例唯一标识（可空 = ServerType 级确定性选择）/ The instance id (null = deterministic ServerType-level selection)</param>
    /// <exception cref="ArgumentException">当 <paramref name="serverType"/> 为空白或包含 <c>/</c>，或 <paramref name="instanceId"/> 非空且为空白 / 包含 <c>/</c> 时抛出 / Thrown when serverType is blank or contains <c>/</c>, or instanceId is non-empty and blank or contains <c>/</c></exception>
    public RemoteServiceTarget(string serverType, string instanceId = null)
    {
        if (string.IsNullOrWhiteSpace(serverType))
        {
            throw new ArgumentException("Server type must not be null or whitespace.", nameof(serverType));
        }

        if (serverType.IndexOf('/') >= 0)
        {
            throw new ArgumentException($"Server type must not contain '/': {serverType}", nameof(serverType));
        }

        if (instanceId != null)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new ArgumentException("Instance id must not be whitespace when provided.", nameof(instanceId));
            }

            if (instanceId.IndexOf('/') >= 0)
            {
                throw new ArgumentException($"Instance id must not contain '/': {instanceId}", nameof(instanceId));
            }
        }

        ServerType = serverType;
        InstanceId = instanceId;
    }

    /// <summary>
    /// 获取服务进程类型名（与心跳注册表中的 Role 同一概念）。
    /// </summary>
    /// <remarks>
    /// Gets the server process type name (the same concept as the role in the heartbeat registry).
    /// </remarks>
    /// <value>服务进程类型名 / The server process type name</value>
    public string ServerType { get; }

    /// <summary>
    /// 获取实例唯一标识；为 null 时表示 ServerType 级确定性选择。
    /// </summary>
    /// <remarks>
    /// Gets the instance id; null means a deterministic ServerType-level selection.
    /// </remarks>
    /// <value>实例唯一标识或 null / The instance id, or null</value>
    public string InstanceId { get; }

    /// <summary>
    /// 产出熔断 / 健康统计键：ServerType 级为 <c>Social</c>，实例级为 <c>Social/2001</c>。
    /// </summary>
    /// <remarks>
    /// Produces the circuit-breaker / health-scoring key: <c>Social</c> at the ServerType level,
    /// <c>Social/2001</c> at the instance level. The format is a contract — failure isolation
    /// keys are split on it, so neither component may contain <c>/</c>.
    /// </remarks>
    /// <returns>统计键字符串 / The statistics key string</returns>
    public override string ToString()
    {
        return InstanceId == null ? ServerType : $"{ServerType}/{InstanceId}";
    }
}
