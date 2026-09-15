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

namespace GameFrameX.Online.Identity;

/// <summary>
/// 玩家档案实体（vault:C3 S2.2：GameAccount 下属的 Server/玩法角色层，区别于 User/GameAccount/Device/Identity/Session）。
/// <para>
/// 维护约束：每个 Player 必须有明确的 App/Server 归属（(GameAccountId, AppId, ServerId) 下命名不重）；
/// 设备标识只能用于识别设备，不得直接充当 <see cref="Id"/>；
/// 玩家核心属性、背包、钱包走强类型 CacheState + Actor（边界红线 4），本实体只承载身份层档案字段。
/// </para>
/// </summary>
public sealed class OnlinePlayerProfile
{
    /// <summary>
    /// 获取或设置玩家标识（服务端生成，全局唯一；存量迁移时沿用 PlayerState.Id）。
    /// </summary>
    public long Id
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置归属游戏账号标识。
    /// </summary>
    public long GameAccountId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置应用标识（玩家归属 App）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区服标识（玩家归属 Server）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩家显示名。
    /// </summary>
    public string Name
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时间（Unix 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近更新时间（Unix 毫秒）。
    /// </summary>
    public long UpdatedAtTime
    {
        get;
        set;
    }
}
