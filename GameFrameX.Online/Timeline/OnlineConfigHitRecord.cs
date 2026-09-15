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

namespace GameFrameX.Online.Timeline;

/// <summary>
/// 配置命中记录（配置域经 <see cref="IOnlineConfigHitProbe"/> 进入玩家时间线的只读行数据）。
/// <para>
/// 维护约束：本类型是**跨域投影载体**，不是配置域实体——配置数据的权威归属在配置 / 运营域（本仓不持有其存储），
/// 因此各字段由探针实现方按自身语义填全，时间线服务不做默认值补全、不做字段推断。
/// <see cref="OccurredAt"/> 单位是 **Unix 秒**（与时间线行形状一致），实现方需自行由毫秒换算。
/// </para>
/// </summary>
public sealed class OnlineConfigHitRecord
{
    /// <summary>
    /// 获取或设置行标识（必须跨腿全局唯一；建议带来源域前缀，避免与其它腿的行标识撞号而破坏分页全序）。
    /// </summary>
    public string EventId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件类型（配置域自有类型值；本仓不解析、不映射）。
    /// </summary>
    public string EventType
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源域标识（如 <c>online-season</c>；为空时时间线服务回落到 <c>online-config</c>）。
    /// </summary>
    public string Source
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置发生时刻（Unix 秒）。
    /// </summary>
    public long OccurredAt
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关联标识（配置记录标识，供按行定位到原始记录）。
    /// </summary>
    public string CorrelationId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置载荷摘要（不得含令牌、指纹、设备标识等敏感值；脱敏责任在探针实现方）。
    /// </summary>
    public string PayloadSummary
    {
        get;
        set;
    }
}
