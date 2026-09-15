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
using GameFrameX.Online.Contracts;

/// <summary>
/// 跨服资产投递传输（vault:C4 S3.8：发起服 → 归属服的统一交易投递通道；真实传输由运行时装配提供）。
/// <para>
/// 维护约束（红线）：归属服<b>不可达必须以异常表达</b>（路由器以异常为补偿队列入队依据）；
/// 业务性失败（余额不足等）经 <see cref="OnlineResult{TData}"/> 正常返回，不得入补偿队列；
/// 投递必须保持幂等语义（同一请求重复投递只生效一次——由统一入口幂等保证，传输层不得自行去重改写请求）。
/// </para>
/// </summary>
public interface IOnlineCrossServerGrantTransport
{
    /// <summary>向目标服投递统一资产交易。</summary>
    /// <param name="targetServerId">目标（归属）服标识。</param>
    /// <param name="request">统一入口请求（原样投递，不改写）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>目标服统一入口执行结果。</returns>
    Task<OnlineResult<OnlineGrantResult>> DeliverAsync(long targetServerId, OnlineGrantRequest request, CancellationToken cancellationToken = default);
}
