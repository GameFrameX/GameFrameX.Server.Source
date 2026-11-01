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

using System.Collections.Concurrent;
using GameFrameX.Core.Abstractions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility;

namespace GameFrameX.Core.Actors.Impl;

/// <summary>
/// 判断Actor交叉死锁
/// </summary>
public static class ActorLimit
{
    /// <summary>
    /// 可以按需扩展检查规则
    /// </summary>
    public enum RuleType
    {
        /// <summary>
        /// 不检查
        /// </summary>
        None,

        /// <summary>
        /// 禁止双向调用
        /// </summary>
        NoBidirectionCall,
    }


    private static IRule _rule;

    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="type"> 检查规则 </param>
    public static void Init(RuleType type)
    {
        switch (type)
        {
            case RuleType.NoBidirectionCall:
                {
                    _rule = new NoBidirectionCallRule();
                }
                break;
            case RuleType.None:
                _rule = null;
                break;
            default:
                // Localization: Core.ActorLimit.UnsupportedRuleType - 不支持的rule类型:{0}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.ActorLimit.UnsupportedRuleType, type));
                break;
        }
    }

    /// <summary>
    /// 是否允许调用：规则激活时检查并在放行时登记「self → target」在飞调用。
    /// </summary>
    /// <param name="target">目标</param>
    /// <returns>返回是否调用</returns>
    public static bool AllowCall(long target)
    {
        var rule = _rule;
        if (rule != null)
        {
            return rule.CanCall(target);
        }

        return true;
    }

    /// <summary>
    /// 结束一次在飞调用：工作单元终态（执行完毕/入队失败/取消/超时放弃）时移除登记，内存天然有界。
    /// </summary>
    /// <param name="self">调用方Actor ID</param>
    /// <param name="target">目标Actor ID</param>
    public static void EndCall(long self, long target)
    {
        _rule?.EndCall(self, target);
    }

    private interface IRule
    {
        bool CanCall(long target);

        void EndCall(long self, long target);
    }


    #region NoBidirectionCallRule

    private class NoBidirectionCallRule : IRule
    {
        /// <summary>
        /// 并发在飞调用计数登记：self → { target → 在飞计数 }。
        /// 同一 caller→target 的并发调用按次数累加、按终态递减，全部结束后回收边与外壳键，内存天然有界。
        /// </summary>
        private readonly ConcurrentDictionary<long, ConcurrentDictionary<long, InFlightCounter>> _crossDic = new();

        /// <summary>
        /// 是否允许调用
        /// </summary>
        /// <param name="target">目标</param>
        /// <returns></returns>
        bool IRule.CanCall(long target)
        {
            var actorId = RuntimeContext.CurrentActor;
            // 从IO线程抛到actor，不涉及入队行为
            if (actorId == 0)
            {
                return true;
            }

            // Actor会在入队成功之后进行设置，这种属于Actor入队
            return CanCall(actorId, target);
        }

        private bool CanCall(long self, long target)
        {
            // 自己入自己的队允许，会直接执行
            if (self == target)
            {
                return true;
            }

            if (_crossDic.TryGetValue(target, out var set) && set.ContainsKey(self))
            {
                // Localization: Core.ActorLimit.CrossDeadlock - 发生交叉死锁，ActorId1:{0} ActorType1:{1} ActorId2:{2} ActorType2:{3}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.ActorLimit.CrossDeadlock, self, ActorIdGenerator.GetActorType(self), target, ActorIdGenerator.GetActorType(target)));
                return false;
            }

            var selfSet = _crossDic.GetOrAdd(self, k => new ConcurrentDictionary<long, InFlightCounter>());
            var counter = selfSet.GetOrAdd(target, k => new InFlightCounter());
            Interlocked.Increment(ref counter.Count);

            return true;
        }

        /// <summary>
        /// 终态递减在飞计数：计数归零时回收边，边集合清空后原子回收外层键。
        /// 调用方以工作单元携带的登记所有权标志守卫——未登记路径（discard/checkLock 跳过、Tell）不得递减他人计数。
        /// </summary>
        /// <param name="self">调用方Actor ID</param>
        /// <param name="target">目标Actor ID</param>
        void IRule.EndCall(long self, long target)
        {
            // IO 上下文与自调用未登记，无需移除
            if (self == 0 || self == target)
            {
                return;
            }

            if (_crossDic.TryGetValue(self, out var set) && set.TryGetValue(target, out var counter))
            {
                if (Interlocked.Decrement(ref counter.Count) <= 0)
                {
                    // 仅当仍为当前计数实例时移除边（原子引用比较）：并发重入者会命中同实例并使计数大于零，
                    // 不会进入本分支；被并发替换的实例残留由其自身路径的终态递减清理
                    set.TryRemove(new KeyValuePair<long, InFlightCounter>(target, counter));

                    if (set.IsEmpty)
                    {
                        _crossDic.TryRemove(new KeyValuePair<long, ConcurrentDictionary<long, InFlightCounter>>(self, set));
                    }
                }
            }
        }

        /// <summary>
        /// 在飞调用计数器（同 caller→target 并发调用按次数累加，首个终态不再清边）。
        /// </summary>
        private sealed class InFlightCounter
        {
            /// <summary>当前在飞计数。</summary>
            public int Count;
        }
    }

    #endregion
}
