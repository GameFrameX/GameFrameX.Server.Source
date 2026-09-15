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

using GameFrameX.Online.Contracts;

/// <summary>
/// 统一资产入口执行结果（承载交易标识、终态与落账条目；重复请求回放首次结果的载荷）。
/// <para>
/// 维护约束：<see cref="IsReplay"/> 为真表示本次为幂等回放（未产生新账本条目，
/// <see cref="Entries"/> 从账本按交易标识重取——账本不可变，重取即首次事实，VC-3.1～3.4）；
/// 失败经由 <see cref="OnlineResult{TData}"/> 段位化返回，本类型只承载成功负载。
/// </para>
/// </summary>
public sealed class OnlineGrantResult
{
    /// <summary>
    /// 获取或设置交易标识（幂等边界主键；审计与反查入口）。
    /// </summary>
    public string TransactionId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置交易终态。
    /// </summary>
    public OnlineAssetTransactionState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否为幂等回放（重复业务意图命中首次结果）。
    /// </summary>
    public bool IsReplay
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置落账条目（含全部变更行；回放时从账本重取）。
    /// </summary>
    public IReadOnlyList<OnlineLedgerEntry> Entries
    {
        get;
        set;
    }
}
