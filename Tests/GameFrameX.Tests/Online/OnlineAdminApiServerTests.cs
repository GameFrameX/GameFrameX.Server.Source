// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目 实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Online admin HTTP 服务冒烟测试（真实 Kestrel 监听：路由前缀、POST 往返、双层信封与优雅停机；
    /// Admin 侧 IOnlineServerClient 寻址 <c>{HttpManageUrl}/online/admin/{action}</c>）。
    /// C294 起管理面经 <c>UseAdminApi</c> 组合进运行时，<see cref="CapabilityRuntime.StartAsync"/> 先启业务面再开监听。
    /// <para>端口 28190 为测试专用（避开默认 28090，防本机 Game 服干扰）。</para>
    /// </summary>
    [Collection(CapabilityRuntimeCollection.Name)]
    public class OnlineAdminApiServerTests
    {
        /// <summary>
        /// HTTP 客户端（共享连接池）。
        /// </summary>
        private static readonly HttpClient SharedHttpClient = new HttpClient();

        /// <summary>
        /// 测试专用监听端口。
        /// </summary>
        private const int TestPort = 28190;

        /// <summary>
        /// 组合 Online 族并启用管理面（业务面 + 管理面；授权三元组 1/1/1001，监听测试端口）。
        /// </summary>
        /// <returns>组合产物（未启动）。</returns>
        private static CapabilityRuntime ComposeWithAdmin()
        {
            return OnlineCapabilityTestHarness.Compose(null, options => options.UseAdminApi(adminOptions =>
            {
                adminOptions.AdminPort = TestPort;
                adminOptions.AdminApiPrefix = "online/admin";
            }));
        }

        /// <summary>
        /// 组合并启动双面运行时，执行用例主体后恒停止（停监听 → 停业务面 → 撤桥 + 释放钩子声明）。
        /// </summary>
        /// <param name="body">用例主体。</param>
        /// <returns>异步任务。</returns>
        private static async Task WithStartedAdminAsync(Func<Task> body)
        {
            var runtime = ComposeWithAdmin();
            try
            {
                await runtime.StartAsync();
                await body();
            }
            finally
            {
                await runtime.StopAsync();
            }
        }

        /// <summary>
        /// 向测试端口 POST 一个 action。
        /// </summary>
        /// <param name="action">action 名。</param>
        /// <returns>HTTP 响应。</returns>
        private static Task<HttpResponseMessage> PostAsync(string action)
        {
            var content = new StringContent("{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}", Encoding.UTF8, "application/json");
            return SharedHttpClient.PostAsync("http://127.0.0.1:" + TestPort + "/online/admin/" + action, content);
        }

        /// <summary>
        /// 验证真实 HTTP 往返：POST {prefix}/{action} 返回 200 + 双层信封（外层 0 / 内层 0 / Data 非 null）。
        /// </summary>
        [Fact]
        public async Task StartAsync_RealHttpPost_ShouldReturnDoubleEnvelope()
        {
            await WithStartedAdminAsync(async () =>
            {
                var response = await PostAsync("query_online_overview");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                using (var document = JsonDocument.Parse(body))
                {
                    Assert.Equal(0, document.RootElement.GetProperty("code").GetInt32());
                    var innerJson = document.RootElement.GetProperty("data").GetString();
                    Assert.False(string.IsNullOrEmpty(innerJson));
                    using (var inner = JsonDocument.Parse(innerJson))
                    {
                        Assert.Equal(0, inner.RootElement.GetProperty("Code").GetInt32());
                        Assert.Equal(JsonValueKind.Object, inner.RootElement.GetProperty("Data").ValueKind);
                    }
                }
            });
        }

        /// <summary>
        /// 验证真实 HTTP 上未知 action 同样返回 200（业务错误只进内层 4002）。
        /// </summary>
        [Fact]
        public async Task StartAsync_UnknownAction_ShouldReturnInner4002OverHttp()
        {
            await WithStartedAdminAsync(async () =>
            {
                var response = await PostAsync("no_such_action");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                using (var document = JsonDocument.Parse(body))
                {
                    Assert.Equal(0, document.RootElement.GetProperty("code").GetInt32());
                    var innerJson = document.RootElement.GetProperty("data").GetString();
                    using (var inner = JsonDocument.Parse(innerJson))
                    {
                        Assert.Equal((int)OnlineErrorCode.ResourceNotFound, inner.RootElement.GetProperty("Code").GetInt32());
                    }
                }
            });
        }

        /// <summary>
        /// 验证停机后端口释放（第一次组合启停 → 第二次组合同端口启动成功，且仍能服务真实 HTTP 请求）。
        /// </summary>
        [Fact]
        public async Task StopAsync_ThenRestart_ShouldReleasePort()
        {
            var first = ComposeWithAdmin();
            try
            {
                await first.StartAsync();
            }
            finally
            {
                await first.StopAsync();
            }

            await WithStartedAdminAsync(async () =>
            {
                var response = await PostAsync("no_such_action");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            });
        }
    }
}
