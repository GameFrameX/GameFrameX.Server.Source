using GameFrameX.StartUp.ServiceDefaults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GameFrameX.Tests.StartUp;

public class DatabaseHealthRedactionTests
{
    [Theory]
    [InlineData(HealthStatus.Healthy)]
    [InlineData(HealthStatus.Degraded)]
    [InlineData(HealthStatus.Unhealthy)]
    public void DatabaseHealthResult_PreservesStatusWithoutExposingDriverDetails(HealthStatus status)
    {
        const string credential = "private-connection-value";
        var original = new HealthCheckResult(status, credential, new InvalidOperationException(credential),
            new Dictionary<string, object> { ["connection"] = credential });
        var sanitized = ServiceDefaultsExtensions.SanitizeDatabaseHealthResult(original);
        Assert.Equal(status, sanitized.Status);
        Assert.Null(sanitized.Exception);
        Assert.Empty(sanitized.Data);
        Assert.DoesNotContain(credential, sanitized.Description);
    }
}
