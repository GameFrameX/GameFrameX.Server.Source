// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence;

using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Foundation.Storage;
using GameFrameX.Capability.Online.Persistence.Assets;
using GameFrameX.Capability.Online.Persistence.Identity;
using GameFrameX.Capability.Online.Persistence.Storage;
using GameFrameX.Capability.Online.Foundation.Identity;

/// <summary>
/// Online 分级持久化适配包的 hosting 扩展（C249，D3 = 3A）：
/// <see cref="UseOnlineGameDbStores"/> 一次性预置四个一级 store 的 GameDb 实现进共享服务面。
/// <para>
/// 装配语义（对齐 C231 替换缝族）：预置发生在能力模块 <c>Register</c> 之前——Foundation /
/// Assets 模块探测共享面命中即采用，未命中仍构造 InMemory 默认（缺省路径零变化红线）；
/// 预置的 store 实例无进程内状态缓存（读路径恒直达数据库，实例重建即等价进程重启）。
/// 前置约束：调用前 <see cref="GameFrameX.DataBase.GameDb"/> 静态门面必须已完成 Init
///（组合根先初始化数据库再装配 Online 能力；未 Init 时的首次读写按门面契约 fail fast）。
/// Session / Presence 二级易失 store 不在本缝预置（保持 InMemory 默认，Redis 演进位见模块 spec）。
/// </para>
/// </summary>
public static class OnlinePersistenceHostingExtensions
{
    /// <summary>
    /// 预置 Online 一级 store 的 GameDb 实现（Identity / PlayerStorage / 资产账本 / 资产交易）进共享服务面。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineGameDbStores(this CapabilityHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(new GameDbOnlineIdentityStore());
        builder.UseService(new GameDbOnlinePlayerStorageStore());
        builder.UseService(new GameDbOnlineAssetStore());
        builder.UseService(new GameDbOnlineAssetTransactionStore());
        return builder;
    }
}
