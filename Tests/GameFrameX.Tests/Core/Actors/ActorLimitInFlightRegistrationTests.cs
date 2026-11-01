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

using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Actors.Impl;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Core.Actors;

/// <summary>
/// ActorLimit 在飞登记契约回归：放行时登记、终态移除、死锁形态拒绝。
/// </summary>
/// <remarks>
/// NoBidirectionCallRule 的 _crossDic 语义为"并发在飞调用"（放行登记、终态移除，内存天然有界），
/// 替代旧实现"只增不减"的累计集合；限流拒绝以异常返回等待方，不再伪装成功。
/// 规则激活窗口内并行用例的 IO 上下文调用（CurrentActor=0）不检查不登记，互不干扰。
/// </remarks>
[Collection(GameAppRuntimeCollection.Name)]
public sealed class ActorLimitInFlightRegistrationTests
{
    [Fact]
    public void NoBidirectionCall_AcquiresAndReleases()
    {
        EnsureSettings();
        ActorLimit.Init(ActorLimit.RuleType.NoBidirectionCall);
        try
        {
            const long callerActorId = 990_201;
            const long targetActorId = 990_202;

            RuntimeContext.SetContext(91, callerActorId);

            // 放行即登记 caller → target
            Assert.True(ActorLimit.AllowCall(targetActorId));

            // 反向调用构成在飞交叉（死锁形态）：拒绝（旧实现的检测窗口不覆盖排队期）
            RuntimeContext.SetContext(92, targetActorId);
            Assert.False(ActorLimit.AllowCall(callerActorId));

            // 终态移除登记后，同一反向调用放行（旧代码无 EndCall，登记永不移除 → 此处恒拒绝，A/B 判别点）
            ActorLimit.EndCall(callerActorId, targetActorId);
            Assert.True(ActorLimit.AllowCall(callerActorId));
        }
        finally
        {
            RuntimeContext.ResetContext();
            ActorLimit.Init(ActorLimit.RuleType.None);
        }
    }

    [Fact]
    public async Task SendAsync_RejectedByRule_ThrowsInsteadOfFakingSuccess()
    {
        EnsureSettings();
        const long workerActorId = 990_301;
        var workerActor = new WorkerActor(workerActorId);
        ActorLimit.Init(ActorLimit.RuleType.NoBidirectionCall);
        try
        {
            // 构造 worker → 990_302 在飞：worker 等待 990_302 期间，990_302 对 worker 的等待调用构成交叉死锁形态
            RuntimeContext.SetContext(93, workerActorId);
            Assert.True(ActorLimit.AllowCall(990_302));

            RuntimeContext.SetContext(94, 990_302);

            var executed = false;

            // 旧实现 debug+限流拒绝时静默返回"成功"（Task.CompletedTask），等待方无感知；
            // 契约：拒绝必须以异常放行等待方（工作单元未执行）
            var task = workerActor.SendAsync(() => executed = true, timeOut: 5_000);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains(990_302.ToString(), exception.Message);
            Assert.Contains(workerActorId.ToString(), exception.Message);
            Assert.False(executed);
        }
        finally
        {
            RuntimeContext.ResetContext();
            ActorLimit.Init(ActorLimit.RuleType.None);
        }
    }

    /// <summary>
    /// 确保进程级设置已初始化（WorkerActor 构造与 SendAsync 读取默认超时配置）。
    /// </summary>
    private static void EnsureSettings()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }
    }
}
