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
//   please see the LICENSE file in the root directory of the source code for full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
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
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using GameFrameX.Capability.Online.Assets;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 账本条目不变量测试（构造函数强制 AmountBefore + Delta == AmountAfter，含溢出拒绝）。
    /// </summary>
    public class OnlineLedgerEntryInvariantTests
    {
        /// <summary>
        /// 构造合法条目（前后值自洽）应成功且字段完整。
        /// </summary>
        [Fact]
        public void ConsistentAmounts_ConstructsSuccessfully()
        {
            var header = new OnlineLedgerHeader { TenantId = 1, AppId = 2, PlayerId = 3, HomeServerId = 4, InitiatingServerId = 5, Source = OnlineAssetChangeSource.SystemCompensation, Operation = OnlineGrantOperation.Grant, Reason = "test", BusinessOrderId = string.Empty, OperatorId = string.Empty, };

            var entry = new OnlineLedgerEntry("led-1", "txn-1", header, OnlineAssetKind.Currency, "gold", 100, -30, 70, string.Empty, 1, 1760000000000);

            Assert.Equal(100, entry.AmountBefore);
            Assert.Equal(-30, entry.Delta);
            Assert.Equal(70, entry.AmountAfter);
        }

        /// <summary>
        /// 前后值不自洽（AmountBefore + Delta != AmountAfter）应抛 ArgumentException。
        /// </summary>
        [Fact]
        public void InconsistentAmounts_ThrowsArgumentException()
        {
            var header = new OnlineLedgerHeader { TenantId = 1, AppId = 2, PlayerId = 3, HomeServerId = 4, InitiatingServerId = 5, Source = OnlineAssetChangeSource.SystemCompensation, Operation = OnlineGrantOperation.Grant, Reason = "test", BusinessOrderId = string.Empty, OperatorId = string.Empty, };

            Assert.Throws<ArgumentException>(() => new OnlineLedgerEntry("led-1", "txn-1", header, OnlineAssetKind.Currency, "gold", 100, 30, 200, string.Empty, 1, 1760000000000));
        }

        /// <summary>
        /// AmountBefore + Delta 溢出 long 范围应抛 ArgumentException（不得回绕后通过校验）。
        /// </summary>
        [Fact]
        public void OverflowingAmounts_ThrowsArgumentException()
        {
            var header = new OnlineLedgerHeader { TenantId = 1, AppId = 2, PlayerId = 3, HomeServerId = 4, InitiatingServerId = 5, Source = OnlineAssetChangeSource.SystemCompensation, Operation = OnlineGrantOperation.Grant, Reason = "test", BusinessOrderId = string.Empty, OperatorId = string.Empty, };

            // 回绕值 long.MinValue 不得被误判为一致。
            Assert.Throws<ArgumentException>(() => new OnlineLedgerEntry("led-1", "txn-1", header, OnlineAssetKind.Currency, "gold", long.MaxValue, 1, long.MinValue, string.Empty, 1, 1760000000000));
        }
    }
}
