// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others that are prohibited by laws and regulations!
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Online.Assets;

/// <summary>
/// 补偿队列续投结果（一次 Drain 的投递计数）。
/// <para>
/// 维护约束：RemainingCount 持续大于 0 且超过 SLO 即触发超时告警（运维巡检输入）；
/// 结果只反映本次续投，不承诺队列清空。
/// </para>
/// </summary>
public sealed class OnlineGrantCompensationDrainResult
{
    /// <summary>
    /// 获取本次投递成功条数。
    /// </summary>
    public int DeliveredCount
    {
        get;
    }

    /// <summary>
    /// 获取仍滞留条数（含本次投递失败与 SLO 超时条目）。
    /// </summary>
    public int RemainingCount
    {
        get;
    }

    /// <summary>
    /// 构造续投结果。
    /// </summary>
    /// <param name="deliveredCount">投递成功条数。</param>
    /// <param name="remainingCount">仍滞留条数。</param>
    public OnlineGrantCompensationDrainResult(int deliveredCount, int remainingCount)
    {
        DeliveredCount = deliveredCount;
        RemainingCount = remainingCount;
    }
}
