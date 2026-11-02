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
//   please see the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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

using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Keys;
using GameFrameX.Capability.Online.Abstractions.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// C462 单键化键形断言（D4 裁决铁律）：全局域键（好友/身份/设备/玩家存储/钱包/账本/库存/资产去重/对局结算）
    /// 键内不得出现 ServerId 位；区服维度由行实体 ServerId 快照列承载；作用域键与守卫只剩区服位。
    /// </summary>
    public class OnlineRowKeyShapeTests
    {
        /// <summary>
        /// 好友无向对键 = friend:{低玩家}:{高玩家}，两位玩家位、无任何区服位（跨服好友天然成立，合服零操作）。
        /// </summary>
        [Fact]
        public void Friendship_GlobalDomainKey_ShouldContainPlayerPairOnly()
        {
            Assert.Equal("friend:100:200", OnlineRowKey.Friendship(100, 200));
            Assert.Equal("friend:200:100", OnlineRowKey.Friendship(200, 100));
        }

        /// <summary>
        /// 身份/设备/玩家存储键 = 主体自身位组成，无区服位。
        /// </summary>
        [Fact]
        public void Identity_Device_Storage_GlobalDomainKeys_ShouldContainNoServerSegment()
        {
            Assert.Equal("3:device-token-abc", OnlineRowKey.Identity(3, "device-token-abc"));
            Assert.Equal("5001:device-xyz", OnlineRowKey.PlayerDevice(5001, "device-xyz"));
            Assert.Equal("5001:inventory:slot-1", OnlineRowKey.PlayerStorage(5001, "inventory", "slot-1"));
        }

        /// <summary>
        /// 钱包/库存/账本序/资产去重键 = 玩家位 + 资产位，无区服位。
        /// </summary>
        [Fact]
        public void Assets_GlobalDomainKeys_ShouldContainNoServerSegment()
        {
            Assert.Equal("5001", OnlineRowKey.LedgerSequence(5001));
            Assert.Equal("5001:gold", OnlineRowKey.WalletAccount(5001, "gold"));
            Assert.Equal("5001:item-sword", OnlineRowKey.InventoryStack(5001, "item-sword"));
            Assert.Equal("5001:c:gold", OnlineRowKey.AssetDedup(5001, true, "gold"));
            Assert.Equal("5001:i:item-sword", OnlineRowKey.AssetDedup(5001, false, "item-sword"));
        }

        /// <summary>
        /// 对局结算键 = 对局标识本身（match-{Guid:N} 全局唯一消解跨服碰撞；区服维度由行实体 ServerId 快照列承载）。
        /// </summary>
        [Fact]
        public void MatchResult_ServerDomainKey_ShouldEqualMatchId()
        {
            Assert.Equal("match-0123456789abcdef0123456789abcdef", OnlineRowKey.MatchResult("match-0123456789abcdef0123456789abcdef"));
        }

        /// <summary>
        /// 键 schema 代际常量可追溯：单键键形首代 = 1（键形演进须递增并走迁移评审）。
        /// </summary>
        [Fact]
        public void RowKeySchemaVersion_ShouldBeFirstGeneration()
        {
            Assert.Equal(1, OnlineRowKey.RowKeySchemaVersion);
        }

        /// <summary>
        /// 作用域幂等绑定键 = online:{区服}[:{玩家}]，无租户位/App 位。
        /// </summary>
        [Fact]
        public void ToScopeKey_ShouldContainServerOnly()
        {
            var scope = new OnlineScope(7);
            Assert.Equal("online:7", scope.ToScopeKey(false));
            Assert.Equal("online:7:9", new OnlineScope(7, 9).ToScopeKey(true));
        }

        /// <summary>
        /// 守卫只剩区服判定：跨服拒绝映射 3004；任一侧缺失映射 3005；不再产生 3002/3003（保留位）。
        /// </summary>
        [Fact]
        public void Guard_ShouldOnlyJudgeServerScope()
        {
            Assert.Null(OnlineScopeGuard.Validate(new OnlineScope(7), new OnlineScope(7, 9)));
            Assert.Equal(OnlineErrorCode.ServerScopeDenied, OnlineScopeGuard.Validate(new OnlineScope(7), new OnlineScope(8)));
            Assert.Equal(OnlineErrorCode.ScopeMissing, OnlineScopeGuard.Validate(new OnlineScope(0), new OnlineScope(7)));
            Assert.Equal(OnlineErrorCode.ScopeMissing, OnlineScopeGuard.Validate(new OnlineScope(7), null));
        }
    }
}
