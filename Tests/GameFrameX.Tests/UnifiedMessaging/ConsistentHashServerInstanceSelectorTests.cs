// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Network.RemoteMessaging.Unified;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.Messages;
using System.Collections;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.UnifiedMessaging;

public class ConsistentHashServerInstanceSelectorTests
{
    [Fact]
    public void Select_WithSameRouteKey_ReturnsSameInstance()
    {
        var selector = new ConsistentHashServerInstanceSelector();
        selector.RefreshInstances("TestService", new List<string> { "instance-1", "instance-2", "instance-3" });

        var result1 = selector.Select("TestService", "player-123");
        var result2 = selector.Select("TestService", "player-123");

        Assert.True(result1.HasInstance);
        Assert.True(result2.HasInstance);
        Assert.Equal(result1.InstanceId, result2.InstanceId);
    }

    [Fact]
    public void Select_DifferentRouteKeys_MayReturnDifferentInstances()
    {
        var selector = new ConsistentHashServerInstanceSelector();
        selector.RefreshInstances("TestService", new List<string> { "instance-1", "instance-2", "instance-3" });

        var results = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            var result = selector.Select("TestService", $"player-{i}");
            if (result.HasInstance)
            {
                results.Add(result.InstanceId);
            }
        }

        // With 100 keys and 3 instances, we should hit at least 2 different instances
        Assert.True(results.Count >= 2, $"Expected at least 2 different instances, got {results.Count}");
    }

    [Fact]
    public void Select_WithoutRouteKey_UsesRoundRobin()
    {
        var selector = new ConsistentHashServerInstanceSelector();
        selector.RefreshInstances("TestService", new List<string> { "instance-1", "instance-2", "instance-3" });

        var results = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            var result = selector.Select("TestService");
            results.Add(result.InstanceId);
        }

        // Round-robin should cycle through instances
        Assert.True(results.Distinct().Count() > 1, "Round-robin should return different instances");
    }

    [Fact]
    public void Select_NoInstances_ReturnsNone()
    {
        var selector = new ConsistentHashServerInstanceSelector();

        var result = selector.Select("NonExistentService", "key1");

        Assert.False(result.HasInstance);
    }

    [Fact]
    public void RefreshInstances_RemovesService_WhenEmptyList()
    {
        var selector = new ConsistentHashServerInstanceSelector();
        selector.RefreshInstances("TestService", new List<string> { "instance-1" });

        var resultBefore = selector.Select("TestService", "key1");
        Assert.True(resultBefore.HasInstance);

        selector.RefreshInstances("TestService", new List<string>());

        var resultAfter = selector.Select("TestService", "key1");
        Assert.False(resultAfter.HasInstance);
    }

    [Fact]
    public void Select_InstanceRemoval_OnlyRemapsSubset()
    {
        var selector = new ConsistentHashServerInstanceSelector();
        var allInstances = new List<string> { "instance-1", "instance-2", "instance-3", "instance-4", "instance-5" };
        selector.RefreshInstances("TestService", allInstances);

        // Record mappings for many keys
        var originalMappings = new Dictionary<string, string>();
        for (int i = 0; i < 1000; i++)
        {
            var key = $"player-{i}";
            var result = selector.Select("TestService", key);
            originalMappings[key] = result.InstanceId;
        }

        // Remove some instances
        var remainingInstances = new List<string> { "instance-1", "instance-3", "instance-5" };
        selector.RefreshInstances("TestService", remainingInstances);

        // Count how many keys were remapped vs stayed
        var remappedCount = 0;
        var stayedCount = 0;
        foreach (var kvp in originalMappings)
        {
            var newResult = selector.Select("TestService", kvp.Key);
            if (newResult.InstanceId != kvp.Value)
            {
                remappedCount++;
            }
            else
            {
                stayedCount++;
            }
        }

        // Some keys should be remapped (those on removed instances)
        Assert.True(remappedCount > 0, $"Expected some keys to be remapped, but none were. Remapped: {remappedCount}");
        // Most keys on remaining instances should stay
        Assert.True(stayedCount > 0, $"Expected some keys to stay, but none did. Stayed: {stayedCount}");
        // Not all keys should be remapped
        Assert.True(remappedCount < originalMappings.Count, "Not all keys should be remapped");
    }

    [Fact]
    public void Select_EmptyServiceName_ReturnsNone()
    {
        var selector = new ConsistentHashServerInstanceSelector();

        var result = selector.Select("", "key1");
        Assert.False(result.HasInstance);
    }

    [Fact]
    public void Select_NullServiceName_ReturnsNone()
    {
        var selector = new ConsistentHashServerInstanceSelector();

        var result = selector.Select(null, "key1");
        Assert.False(result.HasInstance);
    }
}
