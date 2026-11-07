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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
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
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Operations.HotfixRollback;

/// <summary>
/// Hotfix 回滚状态变迁日志出口（观测面端口：回滚执行链从审计落定到幂等落定的关键节点轨迹；
/// 未接线时日志跳过——生产环境视为装配缺陷，宿主组合根应注入桥接实现）。
/// <para>
/// 维护约束（红线）：
/// ① <b>实现不得抛出阻断业务路径的异常</b>——日志失败不得改变回滚结果（对齐资产域告警出口口径）；
/// ② <b>能力包零 LogHelper 惯例</b>——本包不直接引用宿主日志实现，生产桥接与可断言收集器
/// 均经运行时装配注入（对齐 <see cref="IOnlineHotfixRollbackExecutor"/> 的装配面先例）；
/// ③ <b>只承载状态变迁行</b>——链路启动 / 执行器切换 / 活跃指针逐次尝试与耗尽 / 反切结果 / 完成；
/// 协议错误码与审计明细不经本口（结果回执与统一审计链路承载）。
/// </para>
/// </summary>
public interface IOnlineHotfixRollbackLog
{
    /// <summary>
    /// 记录一条 Info 级状态变迁日志（链路启动 / 切换成功 / 回滚完成）。
    /// </summary>
    /// <param name="message">日志内容（服务层组装的结构化轨迹行）。</param>
    void Info(string message);

    /// <summary>
    /// 记录一条 Warn 级告警日志（指针写入重试 / 已反切回旧版本等需运维关注的非致命变迁）。
    /// </summary>
    /// <param name="message">日志内容（服务层组装的结构化轨迹行）。</param>
    void Warn(string message);

    /// <summary>
    /// 记录一条 Error 级日志（指针写入重试耗尽 / 反切失败等须对账的致命变迁）。
    /// </summary>
    /// <param name="message">日志内容（服务层组装的结构化轨迹行）。</param>
    void Error(string message);
}
