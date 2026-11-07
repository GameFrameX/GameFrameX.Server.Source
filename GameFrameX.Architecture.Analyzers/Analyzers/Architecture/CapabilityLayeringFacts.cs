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


using System.Collections.Generic;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// 框架仓分层事实表（GFX0023/24/25 的单一事实源）：程序集名 → 层级序（整数 = 主层 ×10 + 层内序）。
/// <para>
/// 层级语义：L0 原语 / L1 运行时基座 / L2 Hosting 机制 / L3 Online 能力族 / L4 玩法层（Apps 数据 → Hotfix 逻辑 → 夹具）/
/// L5 组合根与工具。只准高层引低层（严格小于）；层内序承载既有设计内的同层上下游（如 Hotfix→Apps、
/// AdminApi→Hosting.Core、能力族内 Foundation→Abstractions），新增或调整包边界须同步本表。
/// 测试程序集与模板打包工程不参与分层断言。
/// </para>
/// </summary>
internal static class CapabilityLayeringFacts
{
    /// <summary>L0 原语层（零框架语义，可独立发布）。</summary>
    internal const int Layer0 = 0;

    /// <summary>能力契约包判定后缀。</summary>
    private const string ContractSuffix = ".Abstractions";

    /// <summary>
    /// 程序集名 → 层级序（严格递增才合法；未登记的程序集不参与分层断言）。
    /// </summary>
    private static readonly Dictionary<string, int> Ranks = new()
    {
        // L0 原语（层内：本地化键 → 数学/工具 → 网络契约）
        ["GameFrameX.Localization"] = Layer0,
        ["GameFrameX.ProtoBuf.Net"] = Layer0,
        ["GameFrameX.Utility"] = 1,
        ["GameFrameX.Network.Abstractions"] = 2,
        // L1 运行时基座（层内：存储/网络抽象 → 各实现 → Core → 产物配置）
        ["GameFrameX.DataBase"] = 10,
        ["GameFrameX.Network"] = 10,
        ["GameFrameX.DataBase.Mongo"] = 11,
        ["GameFrameX.DataBase.PostgreSql"] = 11,
        ["GameFrameX.DataBase.Sqlite"] = 11,
        ["GameFrameX.Network.HTTP"] = 11,
        ["GameFrameX.Core"] = 12,
        ["GameFrameX.StartUp"] = 13,
        ["GameFrameX.Config"] = 13,
        ["GameFrameX.Proto"] = 13,
        // L2 Hosting 机制（契约 → 机制 → 管理运行面）
        ["GameFrameX.Capability.Hosting.Abstractions"] = 20,
        ["GameFrameX.Capability.Hosting.Core"] = 21,
        ["GameFrameX.Capability.Hosting.AdminApi"] = 22,
        // L3 Online 能力族（脊柱 → 基座域 → 玩法域 → 适配/冻结面）
        ["GameFrameX.Capability.Online.Abstractions"] = 30,
        ["GameFrameX.Capability.Online.Foundation"] = 31,
        ["GameFrameX.Capability.Online.Assets"] = 32,
        ["GameFrameX.Capability.Online.Matchmaking"] = 33,
        ["GameFrameX.Capability.Online.Social"] = 33,
        ["GameFrameX.Capability.Online.Match"] = 34,
        ["GameFrameX.Capability.Online.Leaderboard"] = 35,
        ["GameFrameX.Capability.Online.Persistence"] = 35,
        ["GameFrameX.Capability.Online.Competition"] = 36,
        ["GameFrameX.Capability.Online.Operations"] = 36,
        // L4 玩法层（Apps 数据 → Hotfix 逻辑 / 验收夹具）
        ["GameFrameX.Apps"] = 40,
        ["GameFrameX.Hotfix"] = 41,
        ["GameFrameX.Apps.Sample"] = 41,
        // L5 组合根与工具
        ["GameFrameX.Launcher"] = 50,
        ["GameFrameX.Client"] = 50,
    };

    /// <summary>能力实现包禁引清单（GFX0025）：玩法层两工程、协议产物与胖核心——会话经 Foundation.Session 契约、协议经契约 DTO、玩法经桥契约。</summary>
    private static readonly HashSet<string> CapabilityBannedReferences = new()
    {
        "GameFrameX.Apps",
        "GameFrameX.Hotfix",
        "GameFrameX.Proto",
        "GameFrameX.Core",
    };

    /// <summary>契约包可引用的 L0 原语集合（GFX0024 白名单；外部 NuGet 与其他契约包另判）。</summary>
    private static readonly HashSet<string> ContractPrimitiveWhitelist = new()
    {
        "GameFrameX.Utility",
        "GameFrameX.ProtoBuf.Net",
        "GameFrameX.Network.Abstractions",
        "GameFrameX.Localization",
    };

    /// <summary>尝试取程序集层级序；未登记返回 false（不参与断言）。</summary>
    internal static bool TryGetRank(string name, out int rank)
    {
        return Ranks.TryGetValue(name, out rank);
    }

    /// <summary>契约包判定：GameFrameX.Capability.* 命名空间的 *.Abstractions 工程。</summary>
    internal static bool IsContractPackage(string name)
    {
        return name.StartsWith("GameFrameX.Capability.", System.StringComparison.Ordinal)
               && name.EndsWith(ContractSuffix, System.StringComparison.Ordinal);
    }

    /// <summary>契约包引用白名单判定：其他契约包或 L0 原语。</summary>
    internal static bool IsAllowedContractReference(string name)
    {
        return IsContractPackage(name) || ContractPrimitiveWhitelist.Contains(name);
    }

    /// <summary>能力实现包禁引判定（GFX0025）。</summary>
    internal static bool IsBannedCapabilityReference(string name)
    {
        return CapabilityBannedReferences.Contains(name);
    }

    /// <summary>层级序格式化为可读层级（如 34 → L3.4）。</summary>
    internal static string FormatRank(int rank)
    {
        return "L" + (rank / 10).ToString() + "." + (rank % 10).ToString();
    }
}
