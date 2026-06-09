// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//  
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//  
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
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

using System.Reflection;
using GameFrameX.Core.Actors;

namespace GameFrameX.Tests.Core.Actors;

public sealed class ActorManagerTimerSaveTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(1000)]
    [InlineData(1001)]
    public async Task TimerSaveInBatches_ShouldSaveEveryItemAndWaitLastBatch(int actorCount)
    {
        var savedItems = new List<int>();
        var completedItems = new List<int>();
        var items = Enumerable.Range(1, actorCount).ToArray();

        await InvokeTimerSaveInBatches(
            items,
            item =>
            {
                savedItems.Add(item);
                return Task.Run(() =>
                {
                    lock (completedItems)
                    {
                        completedItems.Add(item);
                    }
                });
            },
            () => true,
            () => Task.CompletedTask);

        Assert.Equal(actorCount, savedItems.Count);
        Assert.Equal(actorCount, completedItems.Count);
        Assert.Equal(items, savedItems);
        Assert.Equal(items, completedItems.OrderBy(static x => x).ToArray());
    }

    [Fact]
    public async Task TimerSaveInBatches_WhenStopping_ShouldWaitAlreadyPostedTasksBeforeReturning()
    {
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSaveToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCompleted = false;
        var isWorkingCalls = 0;

        var invokeTask = InvokeTimerSaveInBatches(
            new[] { 1, 2 },
            _ =>
            {
                saveStarted.SetResult();
                return Task.Run(async () =>
                {
                    await allowSaveToFinish.Task;
                    saveCompleted = true;
                });
            },
            () => Interlocked.Increment(ref isWorkingCalls) == 1,
            () => Task.CompletedTask);

        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(invokeTask.IsCompleted);

        allowSaveToFinish.SetResult();
        await invokeTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(saveCompleted);
    }

    private static Task InvokeTimerSaveInBatches(
        IEnumerable<int> items,
        Func<int, Task> saveAsync,
        Func<bool> isWorking,
        Func<Task> delayAsync)
    {
        var method = typeof(ActorManager).GetMethod("TimerSaveInBatches", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!
            .MakeGenericMethod(typeof(int))
            .Invoke(null, new object[] { items, saveAsync, isWorking, delayAsync });

        return Assert.IsAssignableFrom<Task>(task);
    }
}
