using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 能力服务共享协议机制测试：注册表 CRUD 语义 / 组合根预置（UseService）/
    /// 模块间拓扑序共享解析 / 替换缝命中 / 宿主 Services 与 Modules 暴露面。
    /// </summary>
    public class CapabilityServiceSharingTests
    {
        /// <summary>
        /// 信号服务（测试载荷）。
        /// </summary>
        private sealed class SignalService
        {
            /// <summary>获取标记值。</summary>
            public string Marker { get; set; }
        }

        /// <summary>
        /// 生产模块（注册服务产物，无依赖）。
        /// </summary>
        private sealed class ProducerModule : ICapabilityModule
        {
            /// <summary>生产模块注册的产物（断言用）。</summary>
            public SignalService Produced { get; private set; }

            /// <inheritdoc />
            public CapabilityManifest Manifest { get; } = new("share-producer", "1.0.0", Array.Empty<string>());

            /// <inheritdoc />
            public void Register(CapabilityRegistrationContext context)
            {
                if (context.TryGetService(out SignalService existing))
                {
                    Produced = existing;
                    return;
                }

                Produced = new SignalService { Marker = "produced", };
                context.AddService(Produced);
            }
        }

        /// <summary>
        /// 消费模块（依赖生产模块，解析其产物）。
        /// </summary>
        private sealed class ConsumerModule : ICapabilityModule
        {
            /// <summary>消费模块解析到的产物（断言用）。</summary>
            public SignalService Resolved { get; private set; }

            /// <inheritdoc />
            public CapabilityManifest Manifest { get; } = new("share-consumer", "1.0.0", Array.Empty<string>(), new[] { "share-producer" });

            /// <inheritdoc />
            public void Register(CapabilityRegistrationContext context)
            {
                Resolved = context.GetService<SignalService>();
            }
        }

        /// <summary>
        /// 构造合法选项。
        /// </summary>
        /// <returns>选项。</returns>
        private static CapabilityHostOptions ValidOptions()
        {
            return new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1 };
        }

        /// <summary>
        /// 注册表 CRUD：Add 后 Get/TryGet 命中同实例；未注册类型 Get 抛 / TryGet 缺失；null 实例拒绝。
        /// </summary>
        [Fact]
        public void Registry_AddGetTry_RoundTripAndMissingSemantics()
        {
            var services = new CapabilityServices();
            var instance = new SignalService { Marker = "a", };
            services.AddService(instance);

            Assert.Same(instance, services.GetService<SignalService>());
            Assert.True(services.TryGetService(out SignalService found));
            Assert.Same(instance, found);
            Assert.Throws<InvalidOperationException>(() => services.GetService<object>());
            Assert.False(services.TryGetService(out object _));
            Assert.Throws<ArgumentNullException>(() => services.AddService<object>(null));
        }

        /// <summary>
        /// 同类型重复注册即装配失败（对齐 manifest Id 唯一 / job 全名唯一口径）。
        /// </summary>
        [Fact]
        public void Registry_DuplicateRegistration_FailsFast()
        {
            var services = new CapabilityServices();
            services.AddService(new SignalService());

            Assert.Throws<InvalidOperationException>(() => services.AddService(new SignalService()));
        }

        /// <summary>
        /// 独立注册表形态（单参构造）下上下文服务面可用（缺省自建注册表）。
        /// </summary>
        [Fact]
        public void Context_StandaloneConstructor_OwnsPrivateRegistry()
        {
            var context = new CapabilityRegistrationContext(new CapabilityManifest("solo", "1.0.0", Array.Empty<string>()));
            var instance = new SignalService { Marker = "solo", };
            context.AddService(instance);

            Assert.Same(instance, context.GetService<SignalService>());
            Assert.Same(instance, context.Services.GetService<SignalService>());
        }

        /// <summary>
        /// 模块间共享：拓扑序后模块解析先序模块产物（同一实例）；Build 后组合根经 host.Services 取用。
        /// </summary>
        [Fact]
        public void Build_ModuleProducts_SharedAcrossModulesAndExposedToRoot()
        {
            var producer = new ProducerModule();
            var consumer = new ConsumerModule();
            var builder = new CapabilityHostBuilder();
            builder.AddModule(producer);
            builder.AddModule(consumer);

            var host = builder.Build(ValidOptions());

            Assert.NotNull(producer.Produced);
            Assert.Same(producer.Produced, consumer.Resolved);
            Assert.Same(producer.Produced, host.Services.GetService<SignalService>());
        }

        /// <summary>
        /// 组合根预置（UseService）：模块 TryGetService 命中预置实例（替换缝语义），且未命中时可用缺省。
        /// </summary>
        [Fact]
        public void Builder_UseService_PreSeedWinsOverModuleDefault()
        {
            var replacement = new SignalService { Marker = "replaced", };
            var builder = new CapabilityHostBuilder();
            builder.UseService(replacement);
            builder.AddModule(new ProducerModule());

            var host = builder.Build(ValidOptions());

            Assert.Same(replacement, host.Services.GetService<SignalService>());
        }

        /// <summary>
        /// UseService 同类型重复预置即装配失败（预置面与模块注册面共用同一注册表与口径）。
        /// </summary>
        [Fact]
        public void Builder_UseService_DuplicatePreSeed_FailsFast()
        {
            var builder = new CapabilityHostBuilder();
            builder.UseService(new SignalService());

            Assert.Throws<InvalidOperationException>(() => builder.UseService(new SignalService()));
        }

        /// <summary>
        /// 宿主 Modules 面：暴露启用模块的拓扑序实例（与 Register 共用同一实例）；被过滤模块缺席。
        /// </summary>
        [Fact]
        public void Host_Modules_ExposesEnabledTopologicalInstances()
        {
            var producer = new ProducerModule();
            var consumer = new ConsumerModule();
            var builder = new CapabilityHostBuilder();
            builder.AddModule(producer);
            builder.AddModule(consumer);
            builder.SetFilter(new CapabilityAssemblyFilter(new[] { "share-producer" }, Array.Empty<string>()));

            var host = builder.Build(ValidOptions());

            var module = Assert.Single(host.Modules);
            Assert.Same(producer, module);
            Assert.Equal(new[] { "share-producer" }, host.Report.EnabledIdsInOrder);
        }
    }
}
