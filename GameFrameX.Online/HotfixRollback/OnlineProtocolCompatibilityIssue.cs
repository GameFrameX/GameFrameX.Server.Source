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

namespace GameFrameX.Online.HotfixRollback;

/// <summary>
/// Hotfix 回滚协议兼容差异行（vault:C9 S8.5 / VC-8.9「协议兼容检查通过」的检查产物：
/// 当前活跃清单与目标回滚清单之间的一行定位差异；<see cref="IssueKind"/> 决定该差异是否阻断回滚）。
/// </summary>
public sealed class OnlineProtocolCompatibilityIssue
{
    /// <summary>
    /// 协议兼容差异类型（判定规则见 <see cref="OnlineProtocolCompatibilityChecker"/>；
    /// <see cref="RemovedMessage"/> 与 <see cref="ChangedMessage"/> 为不兼容差异（拒绝回滚），
    /// <see cref="AddedMessage"/> 为兼容差异（入报告提示，不阻断））。
    /// </summary>
    public enum IssueKind
    {
        /// <summary>
        /// 当前版本有、目标版本无——回滚丢失既有协议面，已升级客户端流量会被拒（不兼容，拒绝回滚）。
        /// </summary>
        RemovedMessage = 0,

        /// <summary>
        /// 同名不同消息号 / 同消息号不同名——路由错乱（不兼容，拒绝回滚）。
        /// </summary>
        ChangedMessage = 1,

        /// <summary>
        /// 目标版本独有的消息行——回滚恢复旧处理器，无害（兼容，差异入报告提示）。
        /// </summary>
        AddedMessage = 2,
    }

    /// <summary>
    /// 获取或设置差异类型。
    /// </summary>
    public IssueKind Kind
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置定位消息名（差异行锚定的协议消息名）。
    /// </summary>
    public string MessageName
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置当前（活跃）版本侧的消息号（0 表示该侧无此行）。
    /// </summary>
    public int CurrentMessageId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置目标（回滚）版本侧的消息号（0 表示该侧无此行）。
    /// </summary>
    public int TargetMessageId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置差异描述（人类可读的单行说明，随检查报告与拒绝错误反馈）。
    /// </summary>
    public string Description
    {
        get;
        set;
    }
}
