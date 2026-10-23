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

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 好友域可配置项（C294 自 <see cref="OnlineFriendService"/> 构造参数默认值收编：默认值唯一来源 = 本类型字段初始值）。
/// <para>
/// 维护约束：服务构造收本类型必填参数，不再携带参数默认值或非正数回退——配置错误由
/// <see cref="OnlineSocialOptions.Validate"/> 在装配期 fail-fast 报出，而不是运行期静默回退推荐值。
/// </para>
/// </summary>
public sealed class OnlineFriendOptions
{
    /// <summary>
    /// 获取或设置待答复请求存活时长（秒；默认 604800 即 7 天，超期由清扫滴答 job 收敛为过期态）。
    /// </summary>
    public long RequestTimeToLiveSeconds { get; set; } = 604800;

    /// <summary>
    /// 获取或设置搜索返回条数上限（默认 20）。
    /// </summary>
    public int SearchLimit { get; set; } = 20;

    /// <summary>
    /// 获取或设置好友列表返回条数上限（默认 200；同时也是好友上限裁决依据）。
    /// </summary>
    public int FriendLimit { get; set; } = 200;

    /// <summary>
    /// 获取或设置是否在模块激活期执行旧好友数据一次性迁移（默认 false；C294 收编原族级
    /// <c>Online.MigrateLegacyFriendData</c> 开关，配置路径 <c>Online.Settings.online-social.Friend.MigrateLegacyFriendData</c>）。
    /// <para>
    /// 维护约束：迁移只读单向复制旧表（域 120 FriendRelationState / 域 660 FriendState），旧数据零改动；
    /// 幂等可重复执行；详见 <see cref="OnlineFriendLegacyMigration"/>。
    /// </para>
    /// </summary>
    public bool MigrateLegacyFriendData { get; set; }
}
