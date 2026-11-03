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
/// 合服参与者钩子（玩法 / 数据域归并扩展点；框架内置参与者之外，玩法包按需实现并注册）。
/// </summary>
/// <remarks>
/// 红线：① 参与者必须幂等可重入（同一计划重复执行 = no-op 或回放）；② <see cref="OnlineServerMergeContext.DryRun"/>
/// 为 true 时零写入；③ 不得触碰全局域行（好友/身份/玩家存储/钱包/账本/库存/排行投影——合服零操作铁律）。
/// </remarks>
public interface IServerMergeParticipant
{
    /// <summary>
    /// 参与者名（步骤报告与排序观测面）。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 执行本域归并步骤（dry-run = 预估不落写）。
    /// </summary>
    /// <param name="context">合服执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本步报告。</returns>
    Task<OnlineServerMergeStepReport> MergeAsync(OnlineServerMergeContext context, CancellationToken cancellationToken);
}
