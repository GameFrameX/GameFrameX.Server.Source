namespace GameFrameX.Core.Session;

/// <summary>
/// 本地玩家消息的投递状态。取消通过异常传播，不作为普通投递失败。
/// </summary>
public enum PlayerSendStatus
{
    /// <summary>消息已成功写入玩家通道。</summary>
    LocalDelivered = 0,

    /// <summary>玩家没有可用的本地连接。</summary>
    Offline = 1,

    /// <summary>消息类型不支持或通道写入失败。</summary>
    Failed = 2,
}
