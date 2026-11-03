// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Keys;

/// <summary>
/// 区服域行键重写端口（持久化层绑定：对区服维度的行（如资产归属列 <c>HomeServerId</c>、
/// 对局结算 ServerId 快照列）做单列 UPDATE 重写；框架默认为空实现，GameDb 实现见 Persistence 包）。
/// </summary>
public interface IOnlineServerMergeRowRewriter
{
    /// <summary>
    /// 统计区服域待重写行数（dry-run 报告数据面）。
    /// </summary>
    /// <param name="sourceServerIds">源区服标识集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待重写行数（按表 / 类别分项）。</returns>
    Task<IReadOnlyDictionary<string, long>> CountServerDomainRowsAsync(IReadOnlyList<long> sourceServerIds, CancellationToken cancellationToken);

    /// <summary>
    /// 重写区服域行归属（幂等：已指向目标服的行不再计数；dry-run = 只统计不落写）。
    /// </summary>
    /// <param name="sourceServerIds">源区服标识集合。</param>
    /// <param name="targetServerId">目标区服标识。</param>
    /// <param name="dryRun">是否 dry-run 预演。</param>
    /// <param name="batchSize">批大小。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受影响行数（按表 / 类别分项）。</returns>
    Task<IReadOnlyDictionary<string, long>> RewriteServerDomainRowsAsync(IReadOnlyList<long> sourceServerIds, long targetServerId, bool dryRun, int batchSize, CancellationToken cancellationToken);
}
