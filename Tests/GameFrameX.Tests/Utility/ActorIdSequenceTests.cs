//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Utility;
using Xunit;

namespace GameFrameX.Tests.Utility;

/// <summary>
/// ActorIdGenerator.ActorIdSequence 的单元测试：用假时钟覆盖推进、耗尽等待与时钟回拨分支。
/// </summary>
public class ActorIdSequenceTests
{
    private static readonly DateTime StartTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// 可控时钟：UtcNow 由测试推进，等待动作按请求的毫秒数推进时钟并记录历史。
    /// </summary>
    private sealed class FakeClock
    {
        private DateTime _utcNow;

        public FakeClock(DateTime start)
        {
            _utcNow = start;
        }

        public List<int> WaitHistory { get; } = new List<int>();

        public DateTime UtcNow()
        {
            return _utcNow;
        }

        public void AdvanceSeconds(long seconds)
        {
            _utcNow = _utcNow.AddSeconds(seconds);
        }

        public void Wait(int milliseconds)
        {
            WaitHistory.Add(milliseconds);
            _utcNow = _utcNow.AddMilliseconds(milliseconds);
        }
    }

    private static long StartSecond()
    {
        return (long)(StartTime - IdGenerator.UtcTimeStart).TotalSeconds;
    }

    private static ActorIdGenerator.ActorIdSequence CreateSequence(FakeClock clock, long maximumIncrease)
    {
        return new ActorIdGenerator.ActorIdSequence(maximumIncrease, clock.UtcNow, clock.Wait);
    }

    /// <summary>
    /// 同一秒内自增、跨秒归零，正常推进不触发等待。
    /// </summary>
    [Fact]
    public void Next_ShouldIncreaseWithinSecondAndResetOnNewSecond()
    {
        var clock = new FakeClock(StartTime);
        var sequence = CreateSequence(clock, 10);
        var second = StartSecond();

        sequence.Next(out var firstSecond, out var firstIncrease);
        sequence.Next(out var secondTaken, out var secondIncrease);

        Assert.Equal(second, firstSecond);
        Assert.Equal(0, firstIncrease);
        Assert.Equal(second, secondTaken);
        Assert.Equal(1, secondIncrease);

        clock.AdvanceSeconds(1);
        sequence.Next(out var newSecond, out var newIncrease);

        Assert.Equal(second + 1, newSecond);
        Assert.Equal(0, newIncrease);
        Assert.Empty(clock.WaitHistory);
    }

    /// <summary>
    /// 本秒序号耗尽后在锁外等待到下一秒，不得借用未来的秒。
    /// </summary>
    [Fact]
    public void Next_ShouldWaitForNextSecondWhenExhausted()
    {
        var clock = new FakeClock(StartTime);
        var sequence = CreateSequence(clock, 2);
        var second = StartSecond();

        sequence.Next(out _, out var firstIncrease);
        sequence.Next(out _, out var secondIncrease);
        sequence.Next(out _, out var thirdIncrease);
        Assert.Equal(new long[] { 0, 1, 2 }, new[] { firstIncrease, secondIncrease, thirdIncrease });

        sequence.Next(out var afterWaitSecond, out var afterWaitIncrease);

        Assert.Equal(second + 1, afterWaitSecond);
        Assert.Equal(0, afterWaitIncrease);
        Assert.Single(clock.WaitHistory);
        Assert.InRange(clock.WaitHistory[0], 1, 1000);
    }

    /// <summary>
    /// 短时钟回拨：等待时钟追上上次使用的秒后继续自增，不产生历史重复。
    /// </summary>
    [Fact]
    public void Next_ShouldWaitForClockCatchUpOnSmallRollback()
    {
        var clock = new FakeClock(StartTime);
        var sequence = CreateSequence(clock, 100);
        var second = StartSecond();

        sequence.Next(out _, out var firstIncrease);
        sequence.Next(out _, out var secondIncrease);
        Assert.Equal(0, firstIncrease);
        Assert.Equal(1, secondIncrease);

        clock.AdvanceSeconds(-2);
        sequence.Next(out var caughtUpSecond, out var caughtUpIncrease);

        Assert.Equal(second, caughtUpSecond);
        Assert.Equal(2, caughtUpIncrease);
        Assert.Single(clock.WaitHistory);
        Assert.Equal(2000, clock.WaitHistory[0]);
    }

    /// <summary>
    /// 回拨量恰好等于容忍上限时走等待分支，不抛异常。
    /// </summary>
    [Fact]
    public void Next_ShouldWaitAtExactToleranceBoundary()
    {
        var clock = new FakeClock(StartTime);
        var sequence = CreateSequence(clock, 100);
        var second = StartSecond();

        sequence.Next(out _, out var firstIncrease);
        Assert.Equal(0, firstIncrease);

        clock.AdvanceSeconds(-ActorIdGenerator.MaxClockBackwardsToleranceSeconds);
        sequence.Next(out var caughtUpSecond, out var caughtUpIncrease);

        Assert.Equal(second, caughtUpSecond);
        Assert.Equal(1, caughtUpIncrease);
        Assert.Single(clock.WaitHistory);
        Assert.Equal((int)(ActorIdGenerator.MaxClockBackwardsToleranceSeconds * 1000), clock.WaitHistory[0]);
    }

    /// <summary>
    /// 回拨量超过容忍上限时抛 InvalidOperationException，不等待，且抛出后状态未被污染。
    /// </summary>
    [Fact]
    public void Next_ShouldThrowWhenRollbackExceedsTolerance()
    {
        var clock = new FakeClock(StartTime);
        var sequence = CreateSequence(clock, 100);
        var second = StartSecond();

        sequence.Next(out _, out var firstIncrease);
        Assert.Equal(0, firstIncrease);

        clock.AdvanceSeconds(-(ActorIdGenerator.MaxClockBackwardsToleranceSeconds + 1));
        Assert.Throws<InvalidOperationException>(() => sequence.Next(out _, out _));
        Assert.Empty(clock.WaitHistory);

        // 时钟恢复越过上次使用的秒后，序列应在新秒正常归零推进
        clock.AdvanceSeconds(ActorIdGenerator.MaxClockBackwardsToleranceSeconds + 2);
        sequence.Next(out var recoveredSecond, out var recoveredIncrease);

        Assert.Equal(second + 1, recoveredSecond);
        Assert.Equal(0, recoveredIncrease);
    }
}
