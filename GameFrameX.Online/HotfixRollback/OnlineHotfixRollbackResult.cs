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
/// Hotfix 回滚命令回执（回放标记 + 前后版本 + 兼容报告摘要）。
/// <para>
/// 维护约束：<see cref="IsReplay"/> 为 <c>true</c> 表示幂等命中首次结果（命令未重复执行）；
/// <see cref="CompatibilitySummary"/> 为首次执行时的协议兼容差异摘要
/// （<c>Removed/Changed/Added</c> 计数），回放时从回放令牌还原同构摘要。
/// </para>
/// </summary>
public sealed class OnlineHotfixRollbackResult
{
    /// <summary>
    /// 获取或设置目标（回滚到）版本号。
    /// </summary>
    public string TargetVersion
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置回滚前活跃版本号。
    /// </summary>
    public string PreviousVersion
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否为幂等回放（<c>true</c> = 命中首次结果，本次未重复执行、未重复落审计）。
    /// </summary>
    public bool IsReplay
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置协议兼容报告摘要（首次执行时的三类差异计数文本）。
    /// </summary>
    public string CompatibilitySummary
    {
        get;
        set;
    }
}
