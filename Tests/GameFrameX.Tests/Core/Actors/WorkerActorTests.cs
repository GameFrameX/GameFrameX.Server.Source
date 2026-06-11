// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//  
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//  
//  本项目采用 Apache License 2.0 单协议分发，
//  This project is licensed solely under the Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
//  
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//  
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using GameFrameX.Core.Actors.Impl;
using GameFrameX.Utility.Setting;
using System.Reflection;
using System.Threading.Tasks.Dataflow;

namespace GameFrameX.Tests.Core.Actors;

public sealed class WorkerActorTests
{
    private static readonly object SettingsLock = new();

    [Fact]
    public async Task SendAsync_WithCancelledToken_ShouldCompleteAsCancelled()
    {
        EnsureSettings();
        var worker = new WorkerActor(1);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var executed = false;

        var task = worker.SendAsync(() => executed = true, cancellationToken: cancellationTokenSource.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.False(executed);
    }

    [Fact]
    public async Task SendAsync_WhenQueueIsCompleted_ShouldCompleteAsRejected()
    {
        EnsureSettings();
        var worker = new WorkerActor(2);
        var executed = false;
        var queue = GetQueue(worker);
        queue.Complete();
        await queue.Completion.WaitAsync(TimeSpan.FromSeconds(1));

        var task = worker.SendAsync(() => executed = true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.False(executed);
    }

    [Fact]
    public async Task SendAsync_ShouldRunAcceptedWorkInOrder()
    {
        EnsureSettings();
        var worker = new WorkerActor(3);
        var order = new List<int>();

        var first = worker.SendAsync(() => order.Add(1), timeOut: 5000);
        var second = worker.SendAsync(() => order.Add(2), timeOut: 5000);

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 1, 2 }, order);
    }

    private static IDataflowBlock GetQueue(WorkerActor worker)
    {
        var property = typeof(WorkerActor).GetProperty("ActionBlock", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<IDataflowBlock>(property!.GetValue(worker));
    }

    private static void EnsureSettings()
    {
        if (GlobalSettings.CurrentSetting != null)
        {
            return;
        }

        lock (SettingsLock)
        {
            if (GlobalSettings.CurrentSetting == null)
            {
                GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
            }
        }
    }
}
