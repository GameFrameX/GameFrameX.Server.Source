// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB 仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Net;
using System.Text;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// Admin API 固定窗口限流单元测试 / Admin API fixed-window rate limit unit tests
/// </summary>
/// <remarks>
/// 真实 Kestrel 监听：默认启用形态下同 IP 超过窗口许可数得 429（登录爆破/重放/洪泛闸门）；
/// PermitLimit <= 0 显式关闭时行为与未引入限流前等价（全部 200）。端口 28191 为测试专用。
/// </remarks>
public class OnlineAdminApiRateLimitTests : IDisposable
{
    /// <summary>
    /// HTTP 客户端（共享连接池）。
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new HttpClient();

    /// <summary>
    /// 测试专用监听端口（避开 28190 的 OnlineAdminApiServerTests）。
    /// </summary>
    private const int TestPort = 28191;

    /// <summary>
    /// 当前测试的 HTTP 服务实例。
    /// </summary>
    private AdminApiServer _server;

    /// <summary>
    /// 默认限流形态：PermitLimit=2 时第 3 个请求（同窗口同 IP）应得 429。
    /// </summary>
    [Fact]
    public async Task StartAsync_RateLimitExceeded_ShouldReturn429()
    {
        _server = OnlineAdminApiComposition.CreateServer(CreateComposition().Runtime, BuildAdminOptions(2, 60));
        await _server.StartAsync();

        var content = new StringContent("{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}", Encoding.UTF8, "application/json");
        var first = await SharedHttpClient.PostAsync(Url, content);
        var second = await SharedHttpClient.PostAsync(Url, content);
        var third = await SharedHttpClient.PostAsync(Url, content);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    /// <summary>
    /// 显式关闭限流（PermitLimit=0）：超量请求仍全部 200，行为与未引入限流前等价。
    /// </summary>
    [Fact]
    public async Task StartAsync_RateLimitDisabled_ShouldServeAllRequests()
    {
        _server = OnlineAdminApiComposition.CreateServer(CreateComposition().Runtime, BuildAdminOptions(0, 10));
        await _server.StartAsync();

        for (var i = 0; i < 5; i++)
        {
            var content = new StringContent("{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}", Encoding.UTF8, "application/json");
            var response = await SharedHttpClient.PostAsync(Url, content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>
    /// 限流参数组合校验：PermitLimit > 0 而 Window <= 0 视为非法配置。
    /// </summary>
    [Fact]
    public void IsValid_PermitLimitPositiveWithNonPositiveWindow_ShouldBeInvalid()
    {
        var options = BuildAdminOptions(10, 0);
        Assert.False(options.IsValid());

        var valid = BuildAdminOptions(10, 10);
        Assert.True(valid.IsValid());
    }

    /// <summary>
    /// 请求目标地址（未知 action 同样消耗限流配额：闸门位于路由分发之前）。
    /// </summary>
    private static string Url
    {
        get { return "http://127.0.0.1:" + TestPort + "/online/admin/no_such_action"; }
    }

    /// <summary>
    /// 构造 Online 单入口组合产物（业务运行时形态；授权三元组 1/1/1001）。
    /// </summary>
    /// <returns>组合产物。</returns>
    private static OnlineCapabilityComposition CreateComposition()
    {
        return OnlineCapabilityComposition.AddOnlineCapabilities(options =>
        {
            options.TenantId = 1;
            options.AppId = 1;
            options.ServerId = 1001;
            options.EnableRuntime = true;
        });
    }

    /// <summary>
    /// 构造管理面选项（测试端口 28191 + 限流参数）。
    /// </summary>
    /// <param name="permitLimit">窗口许可数。</param>
    /// <param name="windowSeconds">窗口秒数。</param>
    /// <returns>管理面选项。</returns>
    private static AdminApiOptions BuildAdminOptions(int permitLimit, int windowSeconds)
    {
        return new AdminApiOptions
        {
            AdminPort = TestPort,
            AdminApiPrefix = "online/admin",
            RateLimitPermitLimit = permitLimit,
            RateLimitWindowSeconds = windowSeconds,
        };
    }

    /// <summary>
    /// 释放测试服务（优雅停机）。
    /// </summary>
    public void Dispose()
    {
        if (_server != null)
        {
            _server.StopAsync().GetAwaiter().GetResult();
        }
    }
}
