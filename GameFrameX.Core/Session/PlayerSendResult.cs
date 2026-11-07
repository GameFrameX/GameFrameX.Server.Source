namespace GameFrameX.Core.Session;

/// <summary>
/// 本地玩家消息的投递结果；成功仅表示通道写入完成，不表示客户端已处理。
/// </summary>
public sealed class PlayerSendResult
{
    /// <summary>投递状态。默认值为失败，避免空结果被误认为成功。</summary>
    public PlayerSendStatus Status { get; init; } = PlayerSendStatus.Failed;

    /// <summary>目标玩家标识。</summary>
    public long PlayerId { get; init; }

    /// <summary>失败的诊断信息；成功或离线时可为空。</summary>
    public string ErrorMessage { get; init; }

    /// <summary>是否已完成本地通道写入。</summary>
    public bool IsSuccess => Status == PlayerSendStatus.LocalDelivered;
}
