//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Foundation.Presence;

/// <summary>
/// 清扫索引项（按时间升序；RecordKey 作唯一性去重键）。
/// </summary>
internal readonly struct PresenceSweepIndexEntry : IComparable<PresenceSweepIndexEntry>
{
    /// <summary>排序时间（Reconnecting 为重连截止时刻；Online 为最近心跳时刻）。</summary>
    public readonly long Time;

    /// <summary>对应记录键（保证集合内唯一）。</summary>
    public readonly string RecordKey;

    /// <summary>记录键字典序上限哨兵（供 SortedSet 区间上界用；键为数字冒号拼接，任何实际键均不大于它）。</summary>
    public const string MaximumRecordKey = "9223372036854775807:9223372036854775807:9223372036854775807";

    /// <summary>
    /// 初始化 <see cref="PresenceSweepIndexEntry"/>。
    /// </summary>
    /// <param name="time">排序时间。</param>
    /// <param name="recordKey">记录键。</param>
    public PresenceSweepIndexEntry(long time, string recordKey)
    {
        Time = time;
        RecordKey = recordKey;
    }

    /// <summary>
    /// 按时间升序比较，时间相同按记录键字典序保证全序。
    /// </summary>
    /// <param name="other">另一索引项。</param>
    /// <returns>比较结果。</returns>
    public int CompareTo(PresenceSweepIndexEntry other)
    {
        var comparison = Time.CompareTo(other.Time);
        return comparison != 0 ? comparison : string.CompareOrdinal(RecordKey, other.RecordKey);
    }
}
