// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Events;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// <see cref="IOnlineEventPublisher"/> 的记录桩（供测试断言事件发布事实与审计字段）。
    /// </summary>
    public sealed class OnlineEventRecorder : IOnlineEventPublisher
    {
        /// <summary>并发保护锁。</summary>
        private readonly object _syncRoot = new object();

        /// <summary>已发布事件。</summary>
        private readonly List<OnlineEvent> _events = new List<OnlineEvent>();

        /// <summary>
        /// 获取已发布事件快照。
        /// </summary>
        public IReadOnlyList<OnlineEvent> Events
        {
            get
            {
                lock (_syncRoot)
                {
                    return _events.ToArray();
                }
            }
        }

        /// <summary>
        /// 获取已发布事件数。
        /// </summary>
        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _events.Count;
                }
            }
        }

        /// <summary>
        /// 按事件类型过滤已发布事件。
        /// </summary>
        /// <param name="eventType">事件类型。</param>
        /// <returns>匹配的事件列表。</returns>
        public IReadOnlyList<OnlineEvent> Filter(string eventType)
        {
            lock (_syncRoot)
            {
                return _events.FindAll(candidate => candidate.EventType == eventType).ToArray();
            }
        }

        /// <summary>
        /// 记录待发布事件（无传输，直接入册）。
        /// </summary>
        /// <param name="onlineEvent">待发布事件。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>完成通知。</returns>
        public Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                _events.Add(onlineEvent);
            }

            return Task.CompletedTask;
        }
    }
}
