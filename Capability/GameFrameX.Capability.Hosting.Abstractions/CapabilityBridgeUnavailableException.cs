// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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

using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 能力桥契约不可用异常（租约已撤销 / 已替换；消费方不得回旧数据或降级，族层统一映射为 8002 依赖不可用）。
/// <para>
/// 维护约束：本异常是桥显式失败红线的机制载体——捕获后只能走统一失败路径（族层错误码映射），
/// 不得吞掉后回退空实现。
/// </para>
/// </summary>
public sealed class CapabilityBridgeUnavailableException : Exception
{
    /// <summary>
    /// 初始化 <see cref="CapabilityBridgeUnavailableException"/>。
    /// </summary>
    /// <param name="contractTypeName">契约类型全名（诊断用）。</param>
    public CapabilityBridgeUnavailableException(string contractTypeName)
        : base(BuildMessage(contractTypeName))
    {
        ContractTypeName = contractTypeName ?? string.Empty;
    }

    /// <summary>
    /// 获取契约类型全名。
    /// </summary>
    public string ContractTypeName { get; }

    /// <summary>
    /// 构造异常消息（本地化）。
    /// </summary>
    /// <param name="contractTypeName">契约类型全名。</param>
    /// <returns>异常消息。</returns>
    private static string BuildMessage(string contractTypeName)
    {
        // Localization: Capability.Hosting.BridgeUnavailable - 能力桥契约已撤销或被替换。
        return string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.BridgeUnavailable), contractTypeName);
    }
}
