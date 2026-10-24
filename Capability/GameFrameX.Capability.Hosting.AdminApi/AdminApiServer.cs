// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Net;
using System.Threading.RateLimiting;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Foundation.Localization.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace GameFrameX.Capability.Hosting.AdminApi;

/// <summary>
/// 能力组件 admin HTTP 服务（独立 Kestrel 监听 <see cref="AdminApiOptions.AdminPort"/>，
/// 路由 <c>POST {prefix}/{action}</c>，全部业务结果经 <see cref="AdminApiDispatcher"/> 产出双层信封）。
/// <para>
/// 维护约束：环境中立（不读 ASPNETCORE_* 环境假设，无配置文件依赖，装配选项即全部配置）；
/// 与 Game 服主进程的协议端口互不冲突（独立端口）；停止走优雅关闭（在途请求完成后退出）；
/// 与业务装配开关正交（管理面可独立启停，零能力形态下全部 action 返回未安装码）；
/// 实现 <see cref="ICapabilityAdminApiPlane"/> 挂进 <see cref="CapabilityRuntime"/>（管理面后启先停）。
/// </para>
/// </summary>
public sealed class AdminApiServer : ICapabilityAdminApiPlane
{
    /// <summary>
    /// 限流策略名（按客户端 IP 分区的固定窗口）。
    /// </summary>
    private const string RateLimitPolicyName = "admin-api-fixed-window";

    /// <summary>
    /// 调度器。
    /// </summary>
    private readonly AdminApiDispatcher _dispatcher;

    /// <summary>
    /// 装配选项。
    /// </summary>
    private readonly AdminApiOptions _options;

    /// <summary>
    /// Web 应用实例（启动后非空）。
    /// </summary>
    private WebApplication _application;

    /// <summary>
    /// 初始化 <see cref="AdminApiServer"/>。
    /// </summary>
    /// <param name="options">装配选项。</param>
    /// <param name="dispatcher">调度器。</param>
    public AdminApiServer(AdminApiOptions options, AdminApiDispatcher dispatcher)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// 启动监听（返回即已就绪；长驻服务由宿主生命周期承载——RunAsync 会二次触发 Kestrel 启动被拒绝，禁用）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_application != null)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrelOptions => { kestrelOptions.Listen(IPAddress.Any, _options.AdminPort); });

        // 按 IP 固定窗口限流：登录爆破 / admin 重放 / 消息洪泛的闸门；PermitLimit <= 0 显式关闭
        var rateLimitEnabled = _options.RateLimitPermitLimit > 0;
        if (rateLimitEnabled)
        {
            builder.Services.AddRateLimiter(rateLimiterOptions =>
            {
                rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                rateLimiterOptions.AddPolicy(RateLimitPolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress ?? IPAddress.Any,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = _options.RateLimitPermitLimit,
                        Window = TimeSpan.FromSeconds(_options.RateLimitWindowSeconds),
                        QueueLimit = 0,
                    }));
            });
        }

        var application = builder.Build();
        var prefix = (_options.AdminApiPrefix ?? string.Empty).Trim('/');
        if (prefix.Length == 0)
        {
            // Localization: Capability.Hosting.AdminApi.PrefixRequired - admin API 前缀不能为空。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.PrefixRequired));
        }

        if (rateLimitEnabled)
        {
            application.UseRateLimiter();
        }

        var route = application.MapPost(prefix + "/{action}", async (string action, HttpRequest httpRequest) =>
        {
            string bodyJson;
            using (var reader = new StreamReader(httpRequest.Body))
            {
                bodyJson = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            var responseJson = await _dispatcher.DispatchAsync(action, bodyJson).ConfigureAwait(false);
            return Results.Text(responseJson, "application/json");
        });

        if (rateLimitEnabled)
        {
            route.RequireRateLimiting(RateLimitPolicyName);
        }

        _application = application;
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 停止监听（优雅关闭：在途请求完成后退出并释放端口）。
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task StopAsync()
    {
        if (_application == null)
        {
            return;
        }

        try
        {
            await _application.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await _application.DisposeAsync().ConfigureAwait(false);
            _application = null;
        }
    }
}
