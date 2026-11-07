using GameFrameX.Core.Session;
using Xunit;

namespace GameFrameX.Tests.Core.Session;

public sealed class PlayerSendResultTests
{
    [Theory]
    [InlineData(PlayerSendStatus.LocalDelivered, true)]
    [InlineData(PlayerSendStatus.Offline, false)]
    [InlineData(PlayerSendStatus.Failed, false)]
    public void IsSuccess_ShouldOnlyAcceptCompletedLocalDelivery(PlayerSendStatus status, bool expectedSuccess)
    {
        var result = new PlayerSendResult { PlayerId = 123, Status = status, ErrorMessage = "diagnostic" };

        Assert.Equal(expectedSuccess, result.IsSuccess);
        Assert.Equal(123, result.PlayerId);
        Assert.Equal(status, result.Status);
        Assert.Equal("diagnostic", result.ErrorMessage);
    }

    [Fact]
    public void DefaultResult_ShouldNotReportSuccess()
    {
        Assert.False(new PlayerSendResult().IsSuccess);
    }
}
