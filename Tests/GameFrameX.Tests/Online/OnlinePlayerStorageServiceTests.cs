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
// ==========================================================================================

using System;
using System.Text;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Storage;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 玩家云存储服务测试（vault:C3 VC-2.7/2.8/2.9/2.10/2.11：作用域隔离、乐观锁、限制、分页、软删、过期）。
    /// </summary>
    public class OnlinePlayerStorageServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用玩家一标识。</summary>
        private const long PlayerOne = 1001;

        /// <summary>测试用玩家二标识。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>
        /// 验证三向隔离：跨玩家/跨 App/跨租户读取一律同构 ResourceNotFound（防存在性探测，VC-2.7/2.8/2.9）。
        /// </summary>
        [Fact]
        public async Task ReadAsync_CrossScope_ShouldReturnUniformNotFound()
        {
            // Arrange
            var service = CreateService();
            var scope = CreateScope(PlayerOne);
            await service.WriteAsync(scope, "settings", "volume", Encode("42"), 0, "sess-1");

            // Act
            var own = await service.ReadAsync(scope, "settings", "volume");
            var crossPlayer = await service.ReadAsync(CreateScope(PlayerTwo), "settings", "volume");
            var crossApp = await service.ReadAsync(new OnlineScope(TenantId, AppId + 1, 100, PlayerOne), "settings", "volume");
            var crossTenant = await service.ReadAsync(new OnlineScope(TenantId + 1, AppId, 100, PlayerOne), "settings", "volume");

            // Assert
            Assert.True(own.IsSuccess);
            Assert.Equal("42", Decode(own.Data.Payload));
            Assert.False(crossPlayer.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, crossPlayer.Code);
            Assert.False(crossApp.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, crossApp.Code);
            Assert.False(crossTenant.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, crossTenant.Code);
        }

        /// <summary>
        /// 验证同玩家跨区服数据随身（区服不参与隔离）。
        /// </summary>
        [Fact]
        public async Task ReadAsync_SamePlayerDifferentServer_ShouldShareData()
        {
            // Arrange
            var service = CreateService();
            await service.WriteAsync(CreateScope(PlayerOne), "settings", "volume", Encode("42"), 0, "sess-1");

            // Act
            var otherServer = await service.ReadAsync(new OnlineScope(TenantId, AppId, 200, PlayerOne), "settings", "volume");

            // Assert
            Assert.True(otherServer.IsSuccess);
        }

        /// <summary>
        /// 验证乐观锁：重复创建冲突、版本不匹配冲突、正确版本更新成功且版本递增（VC-2.10）。
        /// </summary>
        [Fact]
        public async Task WriteAsync_VersionSemantics_ShouldEnforceOptimisticLock()
        {
            // Arrange
            var service = CreateService();
            var scope = CreateScope(PlayerOne);

            // Act
            var create = await service.WriteAsync(scope, "settings", "volume", Encode("1"), 0, "sess-1");
            var duplicateCreate = await service.WriteAsync(scope, "settings", "volume", Encode("2"), 0, "sess-1");
            var firstUpdate = await service.WriteAsync(scope, "settings", "volume", Encode("3"), 1, "sess-1");
            var staleReplay = await service.WriteAsync(scope, "settings", "volume", Encode("4"), 1, "sess-1");
            var updateMissing = await service.WriteAsync(scope, "settings", "lang", Encode("zh"), 7, "sess-1");
            var validUpdate = await service.WriteAsync(scope, "settings", "volume", Encode("5"), 2, "sess-1");

            // Assert
            Assert.True(create.IsSuccess);
            Assert.Equal(1, create.Data.Version);
            Assert.False(duplicateCreate.IsSuccess);
            Assert.Equal(OnlineErrorCode.VersionConflict, duplicateCreate.Code);
            Assert.True(firstUpdate.IsSuccess);
            Assert.Equal(2, firstUpdate.Data.Version);
            Assert.False(staleReplay.IsSuccess);
            Assert.Equal(OnlineErrorCode.VersionConflict, staleReplay.Code);
            Assert.False(updateMissing.IsSuccess);
            Assert.Equal(OnlineErrorCode.VersionConflict, updateMissing.Code);
            Assert.True(validUpdate.IsSuccess);
            Assert.Equal(3, validUpdate.Data.Version);
            Assert.Equal("sess-1", validUpdate.Data.CreatedBy);
        }

        /// <summary>
        /// 验证写入限制：负载超限、标识格式非法、单集合键数上限（VC-2.11）。
        /// </summary>
        [Fact]
        public async Task WriteAsync_Limits_ShouldBeRejected()
        {
            // Arrange
            var options = new OnlinePlayerStorageOptions
            {
                MaxPayloadBytes = 8,
                MaxKeysPerCollection = 2,
            };
            var service = new OnlinePlayerStorageService(new InMemoryOnlinePlayerStorageStore(), options);
            var scope = CreateScope(PlayerOne);

            // Act
            var oversize = await service.WriteAsync(scope, "settings", "big", new byte[9], 0, "sess-1");
            var badCollection = await service.WriteAsync(scope, "非法", "key", Encode("x"), 0, "sess-1");
            var badKey = await service.WriteAsync(scope, "settings", "bad key!", Encode("x"), 0, "sess-1");
            await service.WriteAsync(scope, "settings", "k1", Encode("1"), 0, "sess-1");
            await service.WriteAsync(scope, "settings", "k2", Encode("2"), 0, "sess-1");
            var overKeys = await service.WriteAsync(scope, "settings", "k3", Encode("3"), 0, "sess-1");

            // Assert
            Assert.False(oversize.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, oversize.Code);
            Assert.False(badCollection.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, badCollection.Code);
            Assert.False(badKey.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, badKey.Code);
            Assert.False(overKeys.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, overKeys.Code);
        }

        /// <summary>
        /// 验证游标分页：按键字典序翻页、页大小钳制、末页 HasMore=false。
        /// </summary>
        [Fact]
        public async Task ListAsync_ShouldPageByKeyOrder()
        {
            // Arrange
            var service = CreateService();
            var scope = CreateScope(PlayerOne);
            for (var i = 0; i < 5; i++)
            {
                await service.WriteAsync(scope, "archive", "k" + i, Encode("v" + i), 0, "sess-1");
            }

            // Act
            var pageOne = await service.ListAsync(scope, "archive", null, 2);
            var pageTwo = await service.ListAsync(scope, "archive", pageOne.Data.Cursor, 2);
            var pageThree = await service.ListAsync(scope, "archive", pageTwo.Data.Cursor, 2);

            // Assert
            Assert.True(pageOne.IsSuccess);
            Assert.Equal(2, pageOne.Data.Entries.Count);
            Assert.Equal("k0", pageOne.Data.Entries[0].Key);
            Assert.Equal("k1", pageOne.Data.Entries[1].Key);
            Assert.True(pageOne.Data.HasMore);
            Assert.Equal("k1", pageOne.Data.Cursor);
            Assert.Equal(2, pageTwo.Data.Entries.Count);
            Assert.Equal("k2", pageTwo.Data.Entries[0].Key);
            Assert.Single(pageThree.Data.Entries);
            Assert.Equal("k4", pageThree.Data.Entries[0].Key);
            Assert.False(pageThree.Data.HasMore);
            Assert.Equal(string.Empty, pageThree.Data.Cursor);
        }

        /// <summary>
        /// 验证软删后读取与列举不可见，重复删除报不存在，软删键复活按创建语义重新计时。
        /// </summary>
        [Fact]
        public async Task DeleteAsync_ShouldHideEntryAndAllowResurrect()
        {
            // Arrange
            var service = CreateService();
            var scope = CreateScope(PlayerOne);
            await service.WriteAsync(scope, "archive", "k1", Encode("v1"), 0, "sess-1");

            // Act
            var deleted = await service.DeleteAsync(scope, "archive", "k1");
            var readAfterDelete = await service.ReadAsync(scope, "archive", "k1");
            var deleteAgain = await service.DeleteAsync(scope, "archive", "k1");
            var listed = await service.ListAsync(scope, "archive", null, 10);
            var resurrected = await service.WriteAsync(scope, "archive", "k1", Encode("v2"), 0, "sess-2");
            var readAfterResurrect = await service.ReadAsync(scope, "archive", "k1");

            // Assert
            Assert.True(deleted.IsSuccess);
            Assert.False(readAfterDelete.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, readAfterDelete.Code);
            Assert.False(deleteAgain.IsSuccess);
            Assert.Empty(listed.Data.Entries);
            Assert.True(resurrected.IsSuccess);
            Assert.Equal(1, resurrected.Data.Version);
            Assert.True(readAfterResurrect.IsSuccess);
            Assert.Equal("v2", Decode(readAfterResurrect.Data.Payload));
        }

        /// <summary>
        /// 验证过期条目：读取即时不可见（判定即过期）、清理任务批量软删。
        /// </summary>
        [Fact]
        public async Task Expiry_ShouldHideOnReadAndSweepToDeleted()
        {
            // Arrange：写入即已过期的条目（ExpiresAtTime 在过去）。
            var service = CreateService();
            var scope = CreateScope(PlayerOne);
            var pastExpiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1_000;
            await service.WriteAsync(scope, "cache", "c1", Encode("v1"), 0, "sess-1", pastExpiry);

            // Act
            var expiredRead = await service.ReadAsync(scope, "cache", "c1");
            var swept = await service.SweepExpiredAsync();
            var listed = await service.ListAsync(scope, "cache", null, 10);

            // Assert
            Assert.False(expiredRead.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, expiredRead.Code);
            Assert.Equal(1, swept);
            Assert.Empty(listed.Data.Entries);
        }

        /// <summary>
        /// 验证缺玩家主体位被拒绝（存储是玩家作用域数据）。
        /// </summary>
        [Fact]
        public async Task WriteAsync_WithoutPlayerScope_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService();
            var scope = new OnlineScope(TenantId, AppId, 100);

            // Act
            var outcome = await service.WriteAsync(scope, "settings", "volume", Encode("42"), 0, "sess-1");

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeMissing, outcome.Code);
        }

        /// <summary>
        /// 构造被测服务。
        /// </summary>
        /// <param name="options">限制选项（可空 = 默认）。</param>
        /// <returns>玩家云存储服务实例。</returns>
        private static OnlinePlayerStorageService CreateService(OnlinePlayerStorageOptions options = null)
        {
            return new OnlinePlayerStorageService(new InMemoryOnlinePlayerStorageStore(), options);
        }

        /// <summary>
        /// 构造玩家作用域。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>作用域实例。</returns>
        private static OnlineScope CreateScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, 100, playerId);
        }

        /// <summary>
        /// 编码 UTF-8 文本。
        /// </summary>
        /// <param name="text">文本。</param>
        /// <returns>字节数组。</returns>
        private static byte[] Encode(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        /// <summary>
        /// 解码 UTF-8 文本。
        /// </summary>
        /// <param name="payload">字节数组。</param>
        /// <returns>文本。</returns>
        private static string Decode(byte[] payload)
        {
            return Encoding.UTF8.GetString(payload);
        }
    }
}
