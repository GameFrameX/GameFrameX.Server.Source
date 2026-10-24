// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Docs:      https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Assets;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 资产能力包验收测试（C294 机制化）：无参模块扫描可发现 / 真实族白名单单独装配资产能力、产物经宿主共享服务面取用 /
    /// 宿主 Override 替换缝恒胜出包内默认存储 / 资产能力不声明 Options 根类型（旧 OnlineAssetsOptions 已删除）。
    /// </summary>
    [Collection(CapabilityRuntimeCollection.Name)]
    public class OnlineAssetsCapabilityPackageTests
    {
        /// <summary>
        /// 白名单圈定只装配资产能力（资产能力无必选依赖，单独即构成闭包）。
        /// </summary>
        /// <param name="options">组合选项。</param>
        private static void AssetsOnly(CapabilityCompositionOptions options)
        {
            options.Filter = new CapabilityAssemblyFilter(new[] { "online-assets" });
        }

        /// <summary>
        /// 无参模块扫描可发现：资产包程序集扫描产出唯一的 online-assets 模块。
        /// </summary>
        [Fact]
        public void DiscoverModules_FindsAssetsCapabilityModule()
        {
            var modules = CapabilityAssembly.DiscoverModules(new[] { typeof(OnlineAssetsCapabilityModule).Assembly });

            var module = Assert.Single(modules);
            Assert.Equal("online-assets", module.Manifest.Id);
        }

        /// <summary>
        /// 白名单单独装配资产能力：发放 / 查询服务与两个存储接口经宿主共享服务面可取用，装配清单只含 online-assets。
        /// </summary>
        [Fact]
        public async Task Compose_AssetsAlone_ExposesProductsThroughHostServices()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                var services = runtime.Host.Services;
                Assert.NotNull(services.GetService<OnlineGrantService>());
                Assert.NotNull(services.GetService<OnlineAssetQueryService>());
                Assert.NotNull(services.GetService<IOnlineAssetStore>());
                Assert.NotNull(services.GetService<IOnlineAssetTransactionStore>());
                Assert.Equal(new[] { "online-assets" }, runtime.Report.EnabledIdsInOrder);
                return Task.CompletedTask;
            }, configure: AssetsOnly);
        }

        /// <summary>
        /// 存储替换缝：宿主 Override 的资产存储恒胜出包内 InMemory 默认（生产持久化替换位）。
        /// </summary>
        [Fact]
        public async Task OverrideAssetStore_WinsOverInMemoryDefault()
        {
            var replacement = new InMemoryOnlineAssetStore();

            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                Assert.Same(replacement, runtime.Host.Services.GetService<IOnlineAssetStore>());
                return Task.CompletedTask;
            }, configure: options =>
            {
                AssetsOnly(options);
                options.Override<IOnlineAssetStore>(replacement);
            });
        }

        /// <summary>
        /// 资产能力是无参数能力：清单不声明 Options 根类型，报告条目无 Options 字段生效值。
        /// </summary>
        [Fact]
        public async Task Manifest_DeclaresNoOptionsType_AndReportCarriesNoSettingsFields()
        {
            Assert.Null(OnlineAssetsCapabilityModule.ManifestInstance.OptionsType);

            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                var entry = Assert.Single(runtime.Report.Entries, candidate => candidate.Manifest.Id == "online-assets");
                Assert.Equal(CapabilityFilterState.Enabled, entry.State);
                Assert.Null(entry.Manifest.OptionsType);
                Assert.Empty(entry.SettingsFields);
                return Task.CompletedTask;
            }, configure: AssetsOnly);
        }
    }
}
