// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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


using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Utility;

/// <summary>
/// ActorId 生成器。
/// ID结构：14位(服务器ID) + 7位(类型) + 30位(时间戳) + 12位(自增) = 63位。
/// </summary>
/// <remarks>
/// ActorId Generator.
/// ID structure: 14 bits (Server ID) + 7 bits (Type) + 30 bits (Timestamp) + 12 bits (Auto-increment) = 63 bits.
/// Supports player actors, guild actors, and global actors.
/// Global actors use format: serverId * 100000 + globalFunctionId.
/// </remarks>
public static class ActorIdGenerator
{
    private const int GlobalActorIdMultiplier = 1000;
    private const int MultiActorTypeBitMask = 0x7F;

    /// <summary>
    /// 可容忍的最大时钟回拨秒数：回拨量不超过该值时在锁外等待时钟追上，超过时拒绝生成 ID 并抛出异常。
    /// </summary>
    /// <remarks>
    /// Maximum tolerated clock rollback in seconds: wait for the clock to catch up within it, refuse to generate IDs beyond it.
    /// </remarks>
    internal const long MaxClockBackwardsToleranceSeconds = 5;

    /// <summary>
    /// 多 Actor 布局（14 位服务器 + 7 位类型）专属的秒级序号状态，与唯一 ID 布局互不挤占容量。
    /// </summary>
    private static readonly ActorIdSequence MultiActorIdSequence = new ActorIdSequence(GlobalConst.MaxActorIncrease, () => DateTime.UtcNow, Thread.Sleep);

    /// <summary>
    /// 唯一 ID 布局（14 位模块）专属的秒级序号状态。
    /// </summary>
    private static readonly ActorIdSequence UniqueIdSequence = new ActorIdSequence(GlobalConst.MaxUniqueIncrease, () => DateTime.UtcNow, Thread.Sleep);

    /// <summary>
    /// 根据ActorId获取服务器ID。
    /// </summary>
    /// <remarks>
    /// Gets the server ID from the ActorId.
    /// </remarks>
    /// <param name="actorId">ActorId / The Actor identifier</param>
    /// <returns>服务器ID / The server ID</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 actorId 小于最小服务器ID时抛出 / Thrown when actorId is less than minimum server ID</exception>
    public static int GetServerId(long actorId)
    {
        if (actorId < GlobalConst.MinServerId)
        {
            throw new ArgumentOutOfRangeException(nameof(actorId), LocalizationService.GetString(Localization.Keys.Utility.ExceptionMessages.ActorIdLessThanMinServerId, GlobalConst.MinServerId));
        }

        if (actorId <= GlobalConst.MaxGlobalId)
        {
            return (int)(actorId / GlobalActorIdMultiplier);
        }

        return (int)(actorId >> GlobalConst.ServerIdOrModuleIdMask);
    }

    /// <summary>
    /// 根据ActorId获取ActorType。
    /// </summary>
    /// <remarks>
    /// Gets the actor type from the ActorId.
    /// </remarks>
    /// <param name="actorId">ActorId / The Actor identifier</param>
    /// <returns>Actor类型 / The actor type</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 actorId 无效时抛出 / Thrown when actorId is invalid</exception>
    public static ushort GetActorType(long actorId)
    {
        if (actorId < GlobalConst.MinServerId)
        {
            throw new ArgumentOutOfRangeException(nameof(actorId), LocalizationService.GetString(Localization.Keys.Utility.ExceptionMessages.ActorIdLessThanMinServerIdDetail, actorId, GlobalConst.MinServerId));
        }

        if (actorId <= GlobalConst.MaxGlobalId)
        {
            // 全局actor
            return (ushort)(actorId % GlobalActorIdMultiplier);
        }

        return (ushort)((actorId >> GlobalConst.ActorTypeMask) & MultiActorTypeBitMask);
    }


    /// <summary>
    /// 根据ActorType获取ActorId。
    /// </summary>
    /// <remarks>
    /// Gets the ActorId based on the actor type and server ID.
    /// </remarks>
    /// <param name="type">Actor类型 / The actor type</param>
    /// <param name="serverId">服务器ID，默认为0时使用当前服务器ID / The server ID, defaults to 0 which uses current server ID</param>
    /// <returns>生成的ActorId / The generated ActorId</returns>
    /// <exception cref="ArgumentException">当类型或服务器ID无效时抛出 / Thrown when type or serverId is invalid</exception>
    /// <exception cref="InvalidOperationException">当时钟回拨超过容忍上限时抛出 / Thrown when the clock rolls back beyond the tolerance limit</exception>
    public static long GetActorId(ushort type, int serverId = 0)
    {
        if (type == GlobalConst.ActorTypeSeparator)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.ExceptionMessages.ActorTypeError, type));
        }

        if (serverId < 0)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.ExceptionMessages.ServerIdNegative, serverId));
        }

        if (serverId == 0)
        {
            serverId = GlobalSettings.CurrentSetting.ServerId;
        }

        if (type < GlobalConst.ActorTypeSeparator)
        {
            return GetMultiActorId(type, serverId);
        }

        return GetGlobalActorId(type, serverId);
    }

    /// <summary>
    /// 根据ActorType类型和服务器ID获取全局ActorId。
    /// </summary>
    /// <remarks>
    /// Gets the global ActorId based on actor type and server ID.
    /// Uses format: serverId * 1000 + actorType.
    /// </remarks>
    /// <param name="actorType">Actor类型 / The actor type</param>
    /// <param name="serverId">服务器ID / The server ID</param>
    /// <returns>生成的全局ActorId / The generated global ActorId</returns>
    private static long GetGlobalActorId(ushort actorType, int serverId)
    {
        if (serverId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(serverId), LocalizationService.GetString(Localization.Keys.Utility.ExceptionMessages.ServerIdLessThanOrEqualZero));
        }

        if (actorType >= GlobalConst.ActorTypeMax || actorType == GlobalConst.ActorTypeSeparator || actorType == GlobalConst.ActorTypeNone)
        {
            throw new ArgumentOutOfRangeException(nameof(actorType), LocalizationService.GetString(Localization.Keys.Utility.ExceptionMessages.ActorTypeInvalid));
        }

        return serverId * GlobalActorIdMultiplier + actorType;
    }

    /// <summary>
    /// 取多 Actor 布局的下一个 (秒, 自增)：序号推进与读取在序列内部同一临界区完成，锁外拿到的是本线程私有值。
    /// </summary>
    private static long GetMultiActorId(ushort type, int serverId)
    {
        MultiActorIdSequence.Next(out var second, out var increase);
        var actorId = (long)serverId << GlobalConst.ServerIdOrModuleIdMask; // serverId-14位, 支持1000~9999
        actorId |= (long)type << GlobalConst.ActorTypeMask; // 多actor类型-7位, 支持0~127
        actorId |= second << GlobalConst.TimestampMask; // 时间戳-30位, 支持34年
        actorId |= increase; // 自增-12位, 每秒4096个
        return actorId;
    }

    /// <summary>
    /// 根据模块获取唯一ID。
    /// </summary>
    /// <remarks>
    /// Gets a unique ID based on the module.
    /// </remarks>
    /// <param name="module">模块ID，默认为最大值 / The module ID, defaults to maximum value</param>
    /// <returns>生成的唯一ID / The generated unique ID</returns>
    /// <exception cref="InvalidOperationException">当时钟回拨超过容忍上限时抛出 / Thrown when the clock rolls back beyond the tolerance limit</exception>
    public static long GetUniqueId(ushort module = GlobalConst.IdModuleMax)
    {
        return GetUniqueIdByModule(module);
    }

    /// <summary>
    /// 根据模块获取唯一ID。
    /// </summary>
    /// <remarks>
    /// Gets a unique ID based on the module. Maximum module value is 999.
    /// ID structure: 14 bits (module) + 30 bits (timestamp) + 19 bits (auto-increment).
    /// The sequence waits for the next second when the per-second capacity is exhausted,
    /// and waits for the clock to catch up on small rollback (refuses with an exception beyond the tolerance).
    /// </remarks>
    /// <param name="module">模块ID，默认为999，最大值不能超过999 / The module ID, defaults to 999, maximum is 999</param>
    /// <returns>生成的唯一ID / The generated unique ID</returns>
    /// <exception cref="ArgumentOutOfRangeException">当模块值超过999时抛出 / Thrown when module exceeds 999</exception>
    /// <exception cref="InvalidOperationException">当时钟回拨超过容忍上限时抛出 / Thrown when the clock rolls back beyond the tolerance limit</exception>
    public static long GetUniqueIdByModule(ushort module = 999)
    {
        if (module > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(module), LocalizationService.GetString(Localization.Keys.Utility.ExceptionMessages.ModuleInvalid));
        }

        UniqueIdSequence.Next(out var second, out var increase);
        var id = (long)module << GlobalConst.ServerIdOrModuleIdMask; // 模块id 14位 支持 0~9999
        id |= second << GlobalConst.ModuleIdTimestampMask; // 时间戳 30位
        id |= increase; // 自增-19位, 每秒524288个
        return id;
    }

    /// <summary>
    /// 单一 ID 布局的 (秒, 自增) 序号状态：推进与读取在同一临界区内完成，调用方在锁外只使用自己的局部值。
    /// </summary>
    /// <remarks>
    /// The (second, increase) sequence state of one ID layout: advancing and reading happen in the same critical section,
    /// callers only use their own local values outside the lock.
    /// 时间源与等待动作由构造注入：生产环境使用 DateTime.UtcNow 与 Thread.Sleep，测试注入假时钟与假等待。
    /// 序号耗尽时在锁外等待下一秒（不借用未来的秒）；时钟回拨不超过容忍上限时在锁外等待时钟追上，超过时抛出异常。
    /// </remarks>
    internal sealed class ActorIdSequence
    {
        private readonly object _lockObject = new object();
        private readonly long _maximumIncrease;
        private readonly Func<DateTime> _utcNowProvider;
        private readonly Action<int> _waitAction;
        private long _lastSecond;
        private long _lastIncrease;

        /// <summary>
        /// 以指定的每秒序号上限、时间源与等待动作创建序列。
        /// </summary>
        /// <param name="maximumIncrease">每秒允许的最大自增值 / The maximum auto-increase value per second</param>
        /// <param name="utcNowProvider">当前 UTC 时间提供者 / The current UTC time provider</param>
        /// <param name="waitAction">锁外等待动作，参数为等待毫秒数 / The wait action invoked outside the lock, in milliseconds</param>
        internal ActorIdSequence(long maximumIncrease, Func<DateTime> utcNowProvider, Action<int> waitAction)
        {
            _maximumIncrease = maximumIncrease;
            _utcNowProvider = utcNowProvider;
            _waitAction = waitAction;
        }

        /// <summary>
        /// 取下一个 (秒, 自增)：同一秒内自增，跨秒归零；序号耗尽或短时钟回拨时在锁外等待后重试。
        /// </summary>
        /// <remarks>
        /// Gets the next (second, increase): increases within the same second, resets on a new second;
        /// retries after waiting outside the lock on sequence exhaustion or small clock rollback.
        /// </remarks>
        /// <param name="second">取出的秒值 / The second value handed out</param>
        /// <param name="increase">取出的自增值 / The increase value handed out</param>
        /// <exception cref="InvalidOperationException">当时钟回拨超过容忍上限时抛出 / Thrown when the clock rolls back beyond the tolerance limit</exception>
        internal void Next(out long second, out long increase)
        {
            while (true)
            {
                int waitMilliseconds;
                lock (_lockObject)
                {
                    var currentSecond = GetCurrentSecond(_utcNowProvider());
                    if (currentSecond > _lastSecond)
                    {
                        _lastSecond = currentSecond;
                        _lastIncrease = 0L;
                        second = _lastSecond;
                        increase = _lastIncrease;
                        return;
                    }

                    if (currentSecond == _lastSecond)
                    {
                        if (_lastIncrease < _maximumIncrease)
                        {
                            ++_lastIncrease;
                            second = _lastSecond;
                            increase = _lastIncrease;
                            return;
                        }

                        // 本秒序号已耗尽：等待到下一秒，不借用未来的秒，保证逻辑时钟不超前墙上时钟
                        waitMilliseconds = GetWaitMillisecondsUntilSecond(_lastSecond + 1, _utcNowProvider());
                    }
                    else
                    {
                        var backwardsSeconds = _lastSecond - currentSecond;
                        if (backwardsSeconds > MaxClockBackwardsToleranceSeconds)
                        {
                            // Localization: Utility.Exceptions.ActorIdClockMovedBackwards - 系统时钟回拨 {0} 秒，超过容忍上限 {1} 秒，拒绝生成ID
                            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Utility.ExceptionMessages.ActorIdClockMovedBackwards, backwardsSeconds, MaxClockBackwardsToleranceSeconds));
                        }

                        // 短回拨：等待时钟追上上次使用的秒，随后在该秒内继续自增，不产生历史重复 ID
                        waitMilliseconds = GetWaitMillisecondsUntilSecond(_lastSecond, _utcNowProvider());
                    }
                }

                // 在锁外等待，避免持锁阻塞其他线程
                _waitAction(waitMilliseconds);
            }
        }

        /// <summary>
        /// 计算当前时间在纪元起点之后经过的整秒数。
        /// </summary>
        private static long GetCurrentSecond(DateTime utcNow)
        {
            return (long)(utcNow - IdGenerator.UtcTimeStart).TotalSeconds;
        }

        /// <summary>
        /// 计算距离目标秒起点还需等待的毫秒数（向上取整，至少 1 毫秒）。
        /// </summary>
        private static int GetWaitMillisecondsUntilSecond(long targetSecond, DateTime utcNow)
        {
            var remainingMilliseconds = (IdGenerator.UtcTimeStart.AddSeconds(targetSecond) - utcNow).TotalMilliseconds;
            return (int)System.Math.Ceiling(System.Math.Max(remainingMilliseconds, 1d));
        }
    }
}
