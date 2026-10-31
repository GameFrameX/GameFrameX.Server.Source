// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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

using GameFrameX.Apps.Player.Player.Component;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Localization.Providers;
using GameFrameX.Hotfix.Logic.Player.Login;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// HotfixModule 实例解析与 HTTP 处理器列表回归单测（GFX-1178 / CR-L07）：
/// <c>GetInstance</c> 对未知类型名必须抛带类型上下文的 <see cref="InvalidOperationException"/>，
/// 而非旧实现经 <c>ConcurrentDictionary.GetOrAdd</c> 值工厂返回 null 抛出的脱离语境 <see cref="ArgumentNullException"/>；
/// <c>GetListHttpHandler</c> 对同一处理器实例的多 cmd 注册必须按引用去重。
/// </summary>
public sealed class HotfixModuleGetInstanceTests
{
    private static bool _environmentInitialized;

    private static async Task<HotfixModule> CreateModuleAsync()
    {
        if (!_environmentInitialized)
        {
            if (GlobalSettings.CurrentSetting == null)
            {
                GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
            }

            await ComponentRegister.InitAsync(typeof(PlayerComponent).Assembly);
            LocalizationService.RegisterProvider(new AssemblyResourceProvider(typeof(GameFrameX.Localization.Keys.CoreExceptions.HotfixModule).Assembly));
            _environmentInitialized = true;
        }

        return new HotfixModule(typeof(PlayerComponentAgent).Assembly);
    }

    [Fact]
    public async Task GetInstance_UnknownTypeName_ThrowsInvalidOperationExceptionWithTypeContext()
    {
        var module = await CreateModuleAsync();
        const string unknownTypeName = "GameFrameX.Hotfix.Tests.NoSuchTypeAnywhere";

        var exception = Assert.Throws<InvalidOperationException>(() => module.GetInstance<object>(unknownTypeName));

        Assert.Contains(unknownTypeName, exception.Message);
    }

    [Fact]
    public async Task GetInstance_KnownTypeName_ReturnsInstanceAndCachesIt()
    {
        var module = await CreateModuleAsync();
        var typeName = typeof(PlayerComponentAgent).FullName;

        var first = module.GetInstance<object>(typeName);
        var second = module.GetInstance<object>(typeName);

        Assert.NotNull(first);
        Assert.IsType<PlayerComponentAgent>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetListHttpHandler_DeduplicatesHandlerInstancesByReference()
    {
        var module = await CreateModuleAsync();

        var list = module.GetListHttpHandler();

        // 热更程序集内存在真实 HTTP 处理器（每个处理器按 OriginalCmd/StandardCmd 注册两条）
        Assert.NotEmpty(list);
        // 同一处理器实例不得出现两次（引用去重）
        Assert.Equal(list.Count, list.Distinct().Count());
    }
}
