// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
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
/// 单条资产变更行（一次统一交易内对一个资产类别的带符号数额变更）。
/// <para>
/// 维护约束：数额为带符号整数——正数增加资产、负数减少资产（方向由调用方按操作语义给出，
/// 存储层只做应用后下限 0 校验）；同一交易内同一 (类别, 资产标识) 只允许出现一行
/// （多行合并由调用方先行聚合，保证账本前后值链式可推）；数额为 0 的行非法
/// （无意义的账本污染）。
/// </para>
/// </summary>
public sealed class OnlineAssetChangeLine
{
    /// <summary>
    /// 获取资产类别。
    /// </summary>
    public OnlineAssetKind AssetKind
    {
        get;
    }

    /// <summary>
    /// 获取资产标识（货币代码或道具 ID；类别内唯一）。
    /// </summary>
    public string AssetId
    {
        get;
    }

    /// <summary>
    /// 获取带符号变更数额（正 = 增加，负 = 减少；不允许 0）。
    /// </summary>
    public long Amount
    {
        get;
    }

    /// <summary>
    /// 构造变更行。
    /// </summary>
    /// <param name="assetKind">资产类别。</param>
    /// <param name="assetId">资产标识。</param>
    /// <param name="amount">带符号数额（非 0）。</param>
    public OnlineAssetChangeLine(OnlineAssetKind assetKind, string assetId, long amount)
    {
        AssetKind = assetKind;
        AssetId = assetId ?? throw new ArgumentNullException(nameof(assetId));
        Amount = amount;
    }
}
