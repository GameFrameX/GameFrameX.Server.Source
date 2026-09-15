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
/// Hotfix 版本协议消息契约行（vault:C9 S8.5 协议兼容检查的最小可见粒度：消息名 + 消息号）。
/// <para>
/// 维护约束（红线）：同一清单内消息名与消息号均须唯一（登记处校验，<see cref="OnlineHotfixRollbackService"/>）；
/// 同名不同号或同号不同名都会被判 <c>ChangedMessage</c> 不兼容差异（路由错乱）。
/// proto 字段级签名兼容超出本层可见性，属升级路径（见 <see cref="OnlineProtocolCompatibilityChecker"/>）。
/// </para>
/// </summary>
public sealed class OnlineHotfixProtocolMessage
{
    /// <summary>
    /// 获取或设置协议消息名（路由键；同清单内唯一）。
    /// </summary>
    public string MessageName
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置协议消息号（线缆标识；同清单内唯一，必须大于 0）。
    /// </summary>
    public int MessageId
    {
        get;
        set;
    }
}
