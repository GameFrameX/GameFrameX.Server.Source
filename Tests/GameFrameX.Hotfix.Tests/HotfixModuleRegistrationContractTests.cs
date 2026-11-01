// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using System.Security.Cryptography;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Localization.Providers;
using GameFrameX.Hotfix.Tests.Probes;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 热更注册面契约回归护栏：组件类型守卫、组件→Actor 反查、RPC/消息/HTTP 三注册面 fail-fast 与装载哈希校验。
/// </summary>
/// <remarks>
/// 注册面在装载期以稳定契约异常拒绝违规类型与冲突注册，不静默保留旧项、不抛无上下文的运行时异常；
/// 热更装载在灰度开关开启时具备内容完整性防线。探针集中于独立探针程序集，注册入口经反射直驱（顺序无关）。
/// </remarks>
public sealed class HotfixModuleRegistrationContractTests
{
    [Fact]
    public async Task GetComponentTypeFromAgent_NonGenericBaseType_ThrowsContractException()
    {
        await InitializeEnvironmentAsync();

        // 旧代码 GetGenericArguments()[0] 对非泛型基类抛 IndexOutOfRangeException；契约要求稳定契约异常并带类型全名
        var exception = Assert.Throws<InvalidOperationException>(
            () => HotfixModule.GetComponentTypeFromAgent(typeof(HotfixRegistrationProbes.DirectInterfaceComponentAgent)));

        Assert.Contains(typeof(HotfixRegistrationProbes.DirectInterfaceComponentAgent).FullName, exception.Message);
    }

    [Fact]
    public async Task AddEvent_UnregisteredComponent_ThrowsWithContext()
    {
        await InitializeEnvironmentAsync();

        var module = CreateCleanAssemblyModule();

        // 旧代码 ComponentActorDic 裸索引器抛无上下文 KeyNotFoundException；契约要求带组件全名的契约异常
        var invocationException = Assert.Throws<TargetInvocationException>(
            () => InvokeRegistration(module, "AddEvent", typeof(HotfixRegistrationProbes.UnregisteredComponentEventListener)));

        var exception = Assert.IsType<InvalidOperationException>(invocationException.InnerException);
        Assert.Contains(typeof(HotfixRegistrationProbes.UnregisteredProbeComponent).FullName, exception.Message);
    }

    [Fact]
    public async Task AddRpcHandler_DuplicateRequest_Throws()
    {
        await InitializeEnvironmentAsync();

        var module = CreateCleanAssemblyModule();
        Assert.True((bool)InvokeRegistration(module, "AddRpcHandler", typeof(HotfixRegistrationProbes.ValidRpcProbeHandler)));

        // 旧代码 Type 实例比较恒 false，冲突静默保留旧 handler；契约要求重复注册 fail-fast
        var invocationException = Assert.Throws<TargetInvocationException>(
            () => InvokeRegistration(module, "AddRpcHandler", typeof(HotfixRegistrationProbes.DuplicateRpcProbeHandler)));

        var exception = Assert.IsType<InvalidOperationException>(invocationException.InnerException);
        Assert.Contains(typeof(HotfixRegistrationProbes.FirstRpcProbeRequestMessage).FullName, exception.Message);
        Assert.Contains(typeof(HotfixRegistrationProbes.DuplicateRpcProbeHandler).FullName, exception.Message);
    }

    [Fact]
    public async Task AddRpcHandler_NoParameterlessCtor_ThrowsAtRegistration()
    {
        await InitializeEnvironmentAsync();

        var module = CreateCleanAssemblyModule();

        // 旧实现注册期不校验，GetRpcHandler 每消息反射实例化才抛；契约要求注册期 fail-fast
        var invocationException = Assert.Throws<TargetInvocationException>(
            () => InvokeRegistration(module, "AddRpcHandler", typeof(HotfixRegistrationProbes.MissingParameterlessConstructorRpcProbeHandler)));

        var exception = Assert.IsType<InvalidOperationException>(invocationException.InnerException);
        Assert.Contains(typeof(HotfixRegistrationProbes.MissingParameterlessConstructorRpcProbeHandler).FullName, exception.Message);
    }

    [Fact]
    public async Task AddRpcHandler_StoresFactory_AndGetRpcHandlerCreatesFreshInstances()
    {
        await InitializeEnvironmentAsync();

        var module = CreateCleanAssemblyModule();
        Assert.True((bool)InvokeRegistration(module, "AddRpcHandler", typeof(HotfixRegistrationProbes.ValidRpcProbeHandler)));

        // 旧 map 值存响应消息 Type，GetRpcHandler 实例化响应类型后判 IMessageHandler 必抛 WrongMessageHandlerType；
        // 工厂化后同请求两次取件必须得到不同的新实例（处理器有状态）
        var requestType = typeof(HotfixRegistrationProbes.FirstRpcProbeRequestMessage);
        var firstHandler = module.GetRpcHandler(requestType);
        var secondHandler = module.GetRpcHandler(requestType);

        Assert.IsType<HotfixRegistrationProbes.ValidRpcProbeHandler>(firstHandler);
        Assert.IsType<HotfixRegistrationProbes.ValidRpcProbeHandler>(secondHandler);
        Assert.NotSame(firstHandler, secondHandler);
    }

    [Fact]
    public async Task AddMessageHandler_DuplicateMsgId_Throws()
    {
        await InitializeEnvironmentAsync();

        var module = CreateCleanAssemblyModule();
        Assert.True((bool)InvokeRegistration(module, "AddMessageHandler", typeof(HotfixRegistrationProbes.DuplicateMessageIdFirstComponentHandler)));

        // 旧代码 msgId 冲突仅记日志返回 true（假成功）；契约要求 fail-fast
        var invocationException = Assert.Throws<TargetInvocationException>(
            () => InvokeRegistration(module, "AddMessageHandler", typeof(HotfixRegistrationProbes.DuplicateMessageIdSecondComponentHandler)));

        var exception = Assert.IsType<InvalidOperationException>(invocationException.InnerException);
        Assert.Contains(HotfixRegistrationProbes.ProbeDuplicateMessageId.ToString(), exception.Message);
        Assert.Contains(typeof(HotfixRegistrationProbes.DuplicateMessageIdSecondComponentHandler).FullName, exception.Message);
    }

    [Fact]
    public async Task AddHttpHandler_LowercaseName_RegistersOnce()
    {
        await InitializeEnvironmentAsync();

        var module = CreateCleanAssemblyModule();

        // 旧代码对 OriginalCmd/StandardCmd 连续 TryAdd 无相等守卫：全小写命令第二次 TryAdd 同键失败抛"重复注册"
        Assert.True((bool)InvokeRegistration(module, "AddHttpHandler", typeof(HotfixRegistrationProbes.statusHttpHandler)));

        var handler = module.GetHttpHandler("status");
        Assert.IsType<HotfixRegistrationProbes.statusHttpHandler>(handler);
    }

    [Fact]
    public async Task GetHttpHandler_CaseVariant_ReturnsSameHandler()
    {
        await InitializeEnvironmentAsync();

        var module = CreateCleanAssemblyModule();
        Assert.True((bool)InvokeRegistration(module, "AddHttpHandler", typeof(HotfixRegistrationProbes.PlayerInfoHttpHandler)));

        // 旧字典区分大小写，大小写变体 URL 命中路由后查不到处理器（返回 null）
        var canonical = module.GetHttpHandler("PlayerInfo");
        var caseVariant = module.GetHttpHandler("playerinfo");
        Assert.NotNull(caseVariant);
        Assert.Same(canonical, caseVariant);

        var standardized = module.GetHttpHandler("player_info");
        Assert.NotNull(standardized);
        Assert.Same(canonical, standardized);
    }

    [Fact]
    public async Task Init_HashMismatch_ThrowsOnStartupPath()
    {
        var assemblyCopy = await PrepareAssemblyCopyAsync();
        var assemblyFilePath = assemblyCopy.FilePath;
        var assemblySha256 = assemblyCopy.Sha256;

        using (new HotfixVerifySwitchScope(enabled: true))
        {
            var module = new HotfixModule(assemblyFilePath, new HotfixLoadOptions { ExpectedSha256 = "00" + assemblySha256.Substring(2), });

            // 起服路径（reload=false）：哈希不匹配异常上抛
            var exception = Assert.Throws<InvalidOperationException>(() => module.Init(false));
            Assert.Contains(assemblyFilePath, exception.Message);
        }
    }

    [Fact]
    public async Task Init_HashMismatch_ReturnsFalseWithoutLoadContextOnReloadPath()
    {
        var assemblyCopy = await PrepareAssemblyCopyAsync();
        var assemblyFilePath = assemblyCopy.FilePath;
        var assemblySha256 = assemblyCopy.Sha256;

        using (new HotfixVerifySwitchScope(enabled: true))
        {
            var module = new HotfixModule(assemblyFilePath, new HotfixLoadOptions { ExpectedSha256 = "00" + assemblySha256.Substring(2), });

            // reload 路径：返回 false（不抛），且校验发生在 ALC 创建之前——Unload 无上下文可卸载
            Assert.False(module.Init(true));
            Assert.Null(module.Unload());
        }
    }

    [Fact]
    public async Task Init_HashMatch_Loads()
    {
        var assemblyCopy = await PrepareAssemblyCopyAsync();
        var assemblyFilePath = assemblyCopy.FilePath;
        var assemblySha256 = assemblyCopy.Sha256;

        using (new HotfixVerifySwitchScope(enabled: true))
        {
            var module = new HotfixModule(assemblyFilePath, new HotfixLoadOptions { ExpectedSha256 = assemblySha256, });
            Assert.True(module.Init(true));
        }
    }

    [Fact]
    public async Task Init_SwitchDisabled_WrongHashStillLoads()
    {
        var assemblyCopy = await PrepareAssemblyCopyAsync();
        var assemblyFilePath = assemblyCopy.FilePath;
        var assemblySha256 = assemblyCopy.Sha256;

        // 灰度开关关闭（默认）：不做校验，错误哈希仍装载成功（存量部署零影响）
        using (new HotfixVerifySwitchScope(enabled: false))
        {
            var module = new HotfixModule(assemblyFilePath, new HotfixLoadOptions { ExpectedSha256 = "00" + assemblySha256.Substring(2), });
            Assert.True(module.Init(true));
        }
    }

    /// <summary>
    /// 复制本地化程序集到临时目录并计算其 SHA-256（拷贝隔离默认 ALC 的同身份程序集）。
    /// </summary>
    private static async Task<(string FilePath, string Sha256)> PrepareAssemblyCopyAsync()
    {
        await InitializeEnvironmentAsync();

        var sourceAssemblyFilePath = Path.Combine(AppContext.BaseDirectory, "GameFrameX.Localization.dll");
        Assert.True(File.Exists(sourceAssemblyFilePath), $"测试依赖程序集不存在: {sourceAssemblyFilePath}");

        var assemblyFilePath = Path.Combine(Path.GetTempPath(), $"GameFrameX.Hotfix.Tests.Probes.{Guid.NewGuid():N}.dll");
        File.Copy(sourceAssemblyFilePath, assemblyFilePath);

        using (var fileStream = File.OpenRead(assemblyFilePath))
        using (var sha256 = SHA256.Create())
        {
            return (assemblyFilePath, Convert.ToHexString(sha256.ComputeHash(fileStream)));
        }
    }

    /// <summary>
    /// 灰度开关作用域：就地翻转 <see cref="AppSetting.HotfixVerifyEnabled"/> 字段并在归还时复原
    /// （进程级设置有字段冲突检测，不能整体替换设置实例）。
    /// </summary>
    private readonly struct HotfixVerifySwitchScope : IDisposable
    {
        private readonly bool originalEnabled;

        public HotfixVerifySwitchScope(bool enabled)
        {
            originalEnabled = GlobalSettings.CurrentSetting.HotfixVerifyEnabled;
            GlobalSettings.CurrentSetting.HotfixVerifyEnabled = enabled;
        }

        public void Dispose()
        {
            GlobalSettings.CurrentSetting.HotfixVerifyEnabled = originalEnabled;
        }
    }

    /// <summary>
    /// 构造无任何注册类型的干净程序集模块（本地化程序集仅含资源键，无代理/监听器/处理器）。
    /// </summary>
    private static HotfixModule CreateCleanAssemblyModule()
    {
        return new HotfixModule(typeof(GameFrameX.Localization.Keys.CoreExceptions.HotfixModule).Assembly);
    }

    /// <summary>
    /// 反射直驱私有注册入口（探针类型不进入任何整集扫描面）。
    /// </summary>
    private static object InvokeRegistration(HotfixModule module, string methodName, params object[] arguments)
    {
        var method = typeof(HotfixModule).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(module, arguments);
    }

    /// <summary>
    /// 初始化组件注册表与进程级设置（沿用装配护栏测试先例的缺省初始化）。
    /// </summary>
    private static async Task InitializeEnvironmentAsync()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        await ComponentRegister.InitAsync(typeof(GameFrameX.Apps.Player.Player.Component.PlayerComponent).Assembly);

        // 本地化键常量为编译期内联，测试进程可能始终未加载资源程序集；显式注册资源提供者使命中确定化。
        LocalizationService.RegisterProvider(new AssemblyResourceProvider(typeof(GameFrameX.Localization.Keys.CoreExceptions.HotfixModule).Assembly));
    }
}
