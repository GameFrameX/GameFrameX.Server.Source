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

using GameFrameX.Utility.Hotload;
using Xunit;
using static GameFrameX.Tests.Utility.GameAppRuntimeCollection;

namespace GameFrameX.Tests.Utility.Hotload;

/// <summary>
/// GFX-1174（C434）CR-M05：退役链票据键与过渡标志不变量的回归测试。
/// </summary>
[Collection(Name)]
public class CollectibleAssemblyHostRetireTicketTests
{
    private sealed class FakeModule : ICollectibleAssemblyModule
    {
        public System.Reflection.Assembly LoadedAssembly => typeof(CollectibleAssemblyHostRetireTicketTests).Assembly;

        public int UnloadCallCount { get; private set; }

        public WeakReference Unload()
        {
            UnloadCallCount++;
            return new WeakReference(new object());
        }
    }

    [Fact]
    public async Task Retire_TwoModules_BothUnloadAndTransitionClearsOnlyAfterDrain()
    {
        var host = new CollectibleAssemblyHost<FakeModule>();
        var moduleA = new FakeModule();
        var moduleB = new FakeModule();

        host.Retire(moduleA, TimeSpan.FromMilliseconds(50));
        host.Retire(moduleB, TimeSpan.FromMilliseconds(300));
        Assert.True(host.InTransition);

        var firstDrained = await WaitAsync(() => moduleA.UnloadCallCount == 1, TimeSpan.FromSeconds(5));
        Assert.True(firstDrained);
        // 仅第一个模块卸载后退役链未排空，过渡态必须仍为 true（票据键保证两个模块互不覆盖）
        Assert.True(host.InTransition);

        var allDrained = await WaitAsync(() => moduleB.UnloadCallCount == 1 && !host.InTransition, TimeSpan.FromSeconds(5));
        Assert.True(allDrained);
        Assert.False(host.InTransition);
    }

    private static async Task<bool> WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }
}
