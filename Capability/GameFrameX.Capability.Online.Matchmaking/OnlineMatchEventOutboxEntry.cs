//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Matchmaking;

using GameFrameX.Capability.Online.Abstractions.Events;

/// <summary>
/// 匹配事件发件箱条目（事件信封 + 投递状态；Delivered 为 false 期间由撮合循环持续重投）。
/// </summary>
public sealed class OnlineMatchEventOutboxEntry
{
    /// <summary>
    /// 获取或设置事件标识（发件箱主键，与 <see cref="OnlineEvent.EventId"/> 一致）。
    /// </summary>
    public string EventId { get; set; }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId { get; set; }

    /// <summary>
    /// 获取或设置 App 标识。
    /// </summary>
    public long AppId { get; set; }

    /// <summary>
    /// 获取或设置事件信封（追加时存档引用，投递时原样发布）。
    /// </summary>
    public OnlineEvent Event { get; set; }

    /// <summary>
    /// 获取或设置是否已投递（false = 待投递，投递失败保留）。
    /// </summary>
    public bool Delivered { get; set; }

    /// <summary>
    /// 获取或设置登记序号（追加时单调递增；发件箱排序用）。
    /// </summary>
    public long SequenceNumber { get; set; }
}
