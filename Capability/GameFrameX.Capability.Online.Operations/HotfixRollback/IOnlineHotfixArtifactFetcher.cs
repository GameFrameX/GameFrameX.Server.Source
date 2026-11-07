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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Capability.Online.Operations.HotfixRollback;

/// <summary>
/// 登记期制品获取器契约：清单登记携带制品字段时，在登记落簿前把制品取到本机版本目录，
/// 保证「已登记」与「已落盘」同时成立——切换命令只消费已落盘版本，应急操作零下载。
/// <para>
/// 维护约束（红线）：
/// ① <b>完成即保证</b>——本方法正常返回 = 目标制品已在 <c>{CWD}/{Version}/</c> 落盘且 SHA-256 一致；
/// ② <b>失败以异常表达</b>——网络 / 校验不符 / 超限 / 磁盘 / 版本目录内容不一致一律抛出
/// （<see cref="OnlineHotfixRollbackService"/> 捕获后映射 8002，登记未生效可重试）；
/// ③ <b>幂等与不可变更</b>——同版本重复获取：在盘制品 checksum 一致即通过（不重复下载）、
/// 不一致即拒绝（版本目录一经落盘不可变更，防制品被悄悄替换）；
/// ④ <b>失败零残留</b>——任何失败路径不遗留 staging 临时目录与半成品文件。
/// </para>
/// </summary>
public interface IOnlineHotfixArtifactFetcher
{
    /// <summary>
    /// 确保目标版本制品已在盘且校验一致（下载 + SHA-256 校验 + 同卷原子落盘；语义见接口备注）。
    /// </summary>
    /// <param name="version">版本号（版本目录名；禁止路径分隔符与「..」段）。</param>
    /// <param name="sourceUrl">制品下载地址（绝对 http/https；null = 不下载，仅校验在盘制品一致性）。</param>
    /// <param name="artifactSha256">期望的 SHA-256（64 位十六进制字符串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知（制品已就位；任何失败以异常表达）。</returns>
    Task EnsureArtifactAsync(string version, string sourceUrl, string artifactSha256, CancellationToken cancellationToken = default);
}
