// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes and liabilities arising from secondary development,
//   本组织不承担任何责任，开发者须自行承担。
//   The organization shall not be responsible for any disputes; developers bear the responsibility themselves.
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 匹配域登记表清扫参数（assignment ↔ 数值对局标识登记项的过期收割面）。
/// <para>
/// 维护约束：分配登记生命周期必须**大于单局全生命周期上界**（等待超时 + 运行超时 + 结算重投 + 结束态保留，
/// 默认配置下为分钟级）——清扫只允许回收对局早已终态释放的登记项；配置得比单局上界还短会导致
/// 活跃对局的数值标识映射被移除、懒登记重建分配新标识，破坏双方入场同一 Actor 的收敛语义。
/// </para>
/// </summary>
public sealed class OnlineMatchRegistryOptions
{
    /// <summary>分配登记生命周期的默认值（秒；2 小时，远大于默认配置下分钟级的单局全生命周期上界）。</summary>
    public const int DefaultAssignmentRegistrationLifetimeSeconds = 7200;

    /// <summary>登记表清扫节流间隔的默认值（秒）。</summary>
    public const int DefaultRegistrationSweepIntervalSeconds = 60;

    /// <summary>
    /// 获取或设置分配登记生命周期（秒；默认 7200）。登记项自分配时刻起超过该时长即被清扫移除
    ///（此时对局必然已终态释放——生命周期必须大于单局全生命周期上界，见类约束）。
    /// <para>过期项移除后，票据仍处已匹配态的玩家再查询会经懒登记重建并分配**新**数值标识（与进程重启后语义一致）。</para>
    /// </summary>
    public int AssignmentRegistrationLifetimeSeconds { get; set; } = DefaultAssignmentRegistrationLifetimeSeconds;

    /// <summary>
    /// 获取或设置登记表清扫节流间隔（秒；默认 60）。登记表访问（查找 / 登记）距上次清扫不足该间隔时跳过本轮清扫；
    /// 0 表示禁用节流（每次登记表访问都执行清扫，测试与诊断用）。
    /// </summary>
    public int RegistrationSweepIntervalSeconds { get; set; } = DefaultRegistrationSweepIntervalSeconds;
}
