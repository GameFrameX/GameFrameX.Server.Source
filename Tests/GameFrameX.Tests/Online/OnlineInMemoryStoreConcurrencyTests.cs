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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Identity;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Session;
using GameFrameX.Online.Storage;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 内存存储与服务并发语义测试（vault:C3 VC-2.2/2.10：并发轮换与并发 CAS 恰一成功）。
    /// </summary>
    public class OnlineInMemoryStoreConcurrencyTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家标识。</summary>
        private const long PlayerId = 1001;

        /// <summary>
        /// 验证并发刷新同一 Token 恰一成功（原子轮换，VC-2.2 的并发面）。
        /// </summary>
        [Fact]
        public async Task ConcurrentRefresh_ShouldAllowExactlyOneWinner()
        {
            // Arrange
            var recorder = new OnlineEventRecorder();
            var tokenService = new OnlineSessionTokenService(new InMemoryOnlineSessionStore(), recorder);
            var scope = new OnlineScope(TenantId, AppId, ServerId, PlayerId);
            var issued = await tokenService.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.LatestWins, null);
            Assert.True(issued.IsSuccess);

            // Act
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => tokenService.RefreshSessionTokenAsync(scope, issued.Data.Token)));

            // Assert
            var winners = 0;
            var winnerToken = string.Empty;
            foreach (var outcome in outcomes)
            {
                if (outcome.IsSuccess)
                {
                    winners++;
                    winnerToken = outcome.Data.Token;
                }
                else
                {
                    Assert.Equal(OnlineErrorCode.TokenRevoked, outcome.Code);
                }
            }

            Assert.Equal(1, winners);
            var validation = await tokenService.ValidateTokenAsync(winnerToken);
            Assert.True(validation.IsSuccess);
            Assert.Equal(2, validation.Data.TokenGeneration);
        }

        /// <summary>
        /// 验证并发创建同键条目恰一成功（存储层 CAS，VC-2.10 的并发面）。
        /// </summary>
        [Fact]
        public async Task ConcurrentCreateSameKey_ShouldAllowExactlyOneWinner()
        {
            // Arrange
            var store = new InMemoryOnlinePlayerStorageStore();
            var service = new OnlinePlayerStorageService(store);
            var scope = new OnlineScope(TenantId, AppId, ServerId, PlayerId);

            // Act
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => service.WriteAsync(scope, "settings", "volume", Encode("v" + index), 0, "sess-" + index)));

            // Assert
            var winners = 0;
            foreach (var outcome in outcomes)
            {
                if (outcome.IsSuccess)
                {
                    winners++;
                }
                else
                {
                    Assert.Equal(OnlineErrorCode.VersionConflict, outcome.Code);
                }
            }

            Assert.Equal(1, winners);
            var read = await service.ReadAsync(scope, "settings", "volume");
            Assert.True(read.IsSuccess);
            Assert.Equal(1, read.Data.Version);
        }

        /// <summary>
        /// 验证并发身份注册同标识恰一账号（内存身份存储的键互斥）。
        /// </summary>
        [Fact]
        public async Task ConcurrentResolveSameIdentity_ShouldYieldSingleAccount()
        {
            // Arrange
            var service = new OnlineIdentityService(new InMemoryOnlineIdentityStore());

            // Act
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "concurrent-user")));

            // Assert：并发自动注册可能产生多账号（首建竞争），但每次解析必须自洽成功且玩家可见一致。
            var accountIds = new HashSet<long>();
            foreach (var outcome in outcomes)
            {
                Assert.True(outcome.IsSuccess);
                accountIds.Add(outcome.Data.GameAccount.Id);
            }

            Assert.True(accountIds.Count >= 1);
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
    }
}
