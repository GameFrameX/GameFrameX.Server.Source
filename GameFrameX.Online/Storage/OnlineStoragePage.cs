//  ==========================================================================================
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
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Storage;

/// <summary>
/// 玩家云存储分页结果（vault:C3 S2.6：键字典序游标分页）。
/// <para>
/// 维护约束：<see cref="Cursor"/> 为本页最后一条键（透明游标，下一页从此键之后列举）；
/// <see cref="HasMore"/> 为 false 时 Cursor 为空字符串，调用方不得继续翻页。
/// </para>
/// </summary>
public sealed class OnlineStoragePage
{
    /// <summary>
    /// 获取或设置本页条目（按键字典序）。
    /// </summary>
    public IReadOnlyList<OnlinePlayerStorageEntry> Entries
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置翻页游标（本页最后一条键；无更多页时为空字符串）。
    /// </summary>
    public string Cursor
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否还有更多页。
    /// </summary>
    public bool HasMore
    {
        get;
        set;
    }
}
