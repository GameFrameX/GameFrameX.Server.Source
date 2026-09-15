// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others that are prohibited by laws and regulations!
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Online.Assets;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 资产交易记录存储接口（vault:C4 S3.3：交易幂等边界的持久化契约；重启恢复的扫描数据源）。
/// <para>
/// 维护约束：交易记录只由统一入口写入（状态迁移受状态机约束，终态不可再迁移）；
/// <see cref="ListByStateAsync"/> 供恢复任务与运维巡检消费非终态记录（VC-3.12）；
/// 生产装配以持久化实现替换，内存实现为本仓默认。
/// </para>
/// </summary>
public interface IOnlineAssetTransactionStore
{
    /// <summary>按交易标识查找。</summary>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>交易记录；不存在返回 null。</returns>
    Task<OnlineAssetTransaction> FindAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>保存交易记录（以交易标识为键整体覆盖；状态迁移由统一入口保证合法）。</summary>
    /// <param name="transaction">交易记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveAsync(OnlineAssetTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>按状态列举交易记录（恢复与巡检输入；按创建时刻升序）。</summary>
    /// <param name="state">目标状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配状态的交易记录列表。</returns>
    Task<IReadOnlyList<OnlineAssetTransaction>> ListByStateAsync(OnlineAssetTransactionState state, CancellationToken cancellationToken = default);
}
