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

using GameFrameX.Online.Scope;

namespace GameFrameX.Online.HotfixRollback;

/// <summary>
/// Hotfix 回滚命令请求（vault:C9 S8.5：回滚是唯一受控操作——操作者显式命令 + 审计；
/// 登记（Register）与激活（Activate）为簿记 / 装配面初始化入口，不落审计）。
/// <para>
/// 维护约束（红线）：<see cref="Scope"/> 只锚定 TenantId/AppId 两键（App 级资产，ServerId 固定 0
/// ——服务内统一归一，调用方传入的 ServerId 不参与定位）；<see cref="OperatorId"/>、<see cref="Reason"/>、
/// <see cref="TargetVersion"/>、<see cref="IdempotencyKey"/> 必填（宁拒毋缺，缺失拒绝 4001）；
/// 幂等键承载「回滚到版本 X」这一业务意图——相同键不同意图判 6002 冲突（C93）。
/// </para>
/// </summary>
public sealed class OnlineHotfixRollbackRequest
{
    /// <summary>
    /// 获取或设置生效作用域（服务内只用 TenantId/AppId 两键定位，ServerId 归一为 0）。
    /// </summary>
    public OnlineScope Scope
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置目标回滚版本号（须已登记清单；禁止包含 <c>|</c>）。
    /// </summary>
    public string TargetVersion
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作者标识（受控操作审计三要素之一，必填）。
    /// </summary>
    public string OperatorId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作者显示名（审计检索辅助定位；可为空，定位以 <see cref="OperatorId"/> 为准）。
    /// </summary>
    public string OperatorName
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作原因（受控操作审计三要素之一，必填）。
    /// </summary>
    public string Reason
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置命令幂等键（同一回滚命令重试 / 重发携带相同键；格式按 C93 幂等键校验）。
    /// </summary>
    public string IdempotencyKey
    {
        get;
        set;
    }
}
