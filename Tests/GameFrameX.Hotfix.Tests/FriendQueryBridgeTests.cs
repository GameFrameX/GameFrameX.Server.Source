// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and applicable international regulations.
//
//  使用本项目须严格遵守相应法律法规与开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
//
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//
//  因基于本项目二次开发所产生的一切纠纷与责任，
//  Any legal disputes or liabilities arising from secondary development based on this project,
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; developers shall bear the responsibility themselves.
//
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Hotfix.Logic.Player.Friend;
using GameFrameX.Proto.Proto;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Hotfix 好友查询薄适配器测试（C243）：显式开关边界、桥不可用显式失败（不空列表不回落）、
/// 成功映射、可信身份（作用域三元组取 lease、玩家位取 ActorId）、失效 lease 与异常路径。
/// </summary>
public class FriendQueryBridgeTests : IDisposable
{
    /// <summary>授权租户标识（与 lease 发布三元组一致）。</summary>
    private const long TenantId = 7;

    /// <summary>授权应用标识。</summary>
    private const long AppId = 3;

    /// <summary>授权区服标识。</summary>
    private const long ServerId = 2001;

    /// <summary>玩家（请求主体）标识。</summary>
    private const long ActorOne = 3001;

    /// <summary>另一玩家标识（作用域隔离对照）。</summary>
    private const long ActorTwo = 3002;

    private readonly AppSetting _previousSetting = GlobalSettings.CurrentSetting;

    public FriendQueryBridgeTests()
    {
        OnlineFriendBridgeHolder.Retract();
    }

    public void Dispose()
    {
        OnlineFriendBridgeHolder.Retract();
        if (_previousSetting != null)
        {
            GlobalSettings.SetCurrentSetting(_previousSetting);
        }
    }

    /// <summary>
    /// 以指定桥开关构造当前设置并装载。
    /// </summary>
    private static void UseBridgeSetting(bool enabled)
    {
        GlobalSettings.SetCurrentSetting(new AppSetting
        {
            ServerType = "Game",
            ServerId = (int)ServerId,
            Online = new OnlineCapabilityOptions { EnableFriendQueryBridge = enabled },
        });
    }

    /// <summary>
    /// 可编程契约面桩（记录收到的 scope；可预设结果或抛异常）。
    /// </summary>
    private sealed class StubFriendQueryFace : IOnlineFriendQuery
    {
        public OnlineScope LastScope { get; private set; }

        public List<OnlineFriendEntry> Entries { get; } = new();

        public OnlineErrorCode FailWith { get; set; } = OnlineErrorCode.None;

        public bool Throw { get; set; }

        public Task<OnlineResult<IReadOnlyList<OnlineFriendEntry>>> ListFriendsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
        {
            LastScope = scope;
            if (Throw)
            {
                throw new InvalidOperationException("stub failure");
            }

            if (FailWith != OnlineErrorCode.None)
            {
                return Task.FromResult(OnlineResult<IReadOnlyList<OnlineFriendEntry>>.Fail(FailWith, "stub failure"));
            }

            IReadOnlyList<OnlineFriendEntry> entries = Entries;
            return Task.FromResult(OnlineResult<IReadOnlyList<OnlineFriendEntry>>.Ok(entries));
        }
    }

    /// <summary>
    /// 开关关（缺省）：适配器不接管，响应保持原样——旧链行为边界。
    /// </summary>
    [Fact]
    public async Task BridgeDisabled_DoesNotHandle_ResponseUntouched()
    {
        UseBridgeSetting(false);

        var response = new RespFriendList { ErrorCode = -55, Friends = null };
        var handled = await FriendQueryBridge.TryQueryFriendListAsync(ActorOne, response);

        Assert.False(handled);
        Assert.Equal(-55, response.ErrorCode);
        Assert.Null(response.Friends);
    }

    /// <summary>
    /// 开关开但桥未发布（宿主未启/已停/非 Game 进程误配）：显式 DependencyUnavailable 失败，
    /// 不静默空列表、不回落旧链。
    /// </summary>
    [Fact]
    public async Task BridgeEnabled_NotPublished_ReturnsExplicitDependencyUnavailable()
    {
        UseBridgeSetting(true);

        var response = new RespFriendList();
        var handled = await FriendQueryBridge.TryQueryFriendListAsync(ActorOne, response);

        Assert.True(handled);
        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
        Assert.Empty(response.Friends);
    }

    /// <summary>
    /// 开关开且桥已发布：成功路径映射条目与错误码（OnlineFriendEntry → FriendInfo）。
    /// </summary>
    [Fact]
    public async Task BridgeEnabled_Published_MapsEntriesAndSuccessCode()
    {
        UseBridgeSetting(true);
        var face = new StubFriendQueryFace();
        face.Entries.Add(new OnlineFriendEntry { PlayerId = 42, Name = "alice", Online = true, ServerId = ServerId, EstablishedAtTime = 12345 });
        OnlineFriendBridgeHolder.Publish(face, new OnlineScope(TenantId, AppId, ServerId));

        var response = new RespFriendList();
        var handled = await FriendQueryBridge.TryQueryFriendListAsync(ActorOne, response);

        Assert.True(handled);
        Assert.Equal(0, response.ErrorCode);
        var friend = Assert.Single(response.Friends);
        Assert.Equal(42, friend.PlayerId);
        Assert.Equal("alice", friend.PlayerName);
    }

    /// <summary>
    /// 可信身份：传给契约面的作用域三元组 = lease 授权三元组（服务端配置），
    /// 玩家位 = 请求主体 ActorId——客户端/请求体不可影响。
    /// </summary>
    [Fact]
    public async Task BridgeEnabled_DerivesScopeFromLeaseAndActorIdOnly()
    {
        UseBridgeSetting(true);
        var face = new StubFriendQueryFace();
        OnlineFriendBridgeHolder.Publish(face, new OnlineScope(TenantId, AppId, ServerId));

        await FriendQueryBridge.TryQueryFriendListAsync(ActorTwo, new RespFriendList());

        Assert.NotNull(face.LastScope);
        Assert.Equal(TenantId, face.LastScope.TenantId);
        Assert.Equal(AppId, face.LastScope.AppId);
        Assert.Equal(ServerId, face.LastScope.ServerId);
        Assert.Equal(ActorTwo, face.LastScope.PlayerId);
    }

    /// <summary>
    /// 服务段失败：OnlineErrorCode 映射为 -(int)code（与旧链 -1..-99 段不重叠）。
    /// </summary>
    [Fact]
    public async Task BridgeEnabled_ServiceFailure_MapsNegativeOnlineErrorCode()
    {
        UseBridgeSetting(true);
        var face = new StubFriendQueryFace { FailWith = OnlineErrorCode.ScopeDenied };
        OnlineFriendBridgeHolder.Publish(face, new OnlineScope(TenantId, AppId, ServerId));

        var response = new RespFriendList();
        var handled = await FriendQueryBridge.TryQueryFriendListAsync(ActorOne, response);

        Assert.True(handled);
        Assert.Equal(-(int)OnlineErrorCode.ScopeDenied, response.ErrorCode);
        Assert.Empty(response.Friends);
    }

    /// <summary>
    /// lease 已撤销（宿主停止后旧引用）：显式失败，不使用失效服务。
    /// </summary>
    [Fact]
    public async Task BridgeEnabled_RetractedLease_ReturnsExplicitFailure()
    {
        UseBridgeSetting(true);
        var face = new StubFriendQueryFace();
        OnlineFriendBridgeHolder.Publish(face, new OnlineScope(TenantId, AppId, ServerId));
        OnlineFriendBridgeHolder.Retract();

        var response = new RespFriendList();
        var handled = await FriendQueryBridge.TryQueryFriendListAsync(ActorOne, response);

        Assert.True(handled);
        Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, response.ErrorCode);
    }

    /// <summary>
    /// 桥调用抛异常：按旧链异常码 -2 显式失败（不吞、不空列表）。
    /// </summary>
    [Fact]
    public async Task BridgeEnabled_ThrownException_MapsLegacyExceptionCode()
    {
        UseBridgeSetting(true);
        var face = new StubFriendQueryFace { Throw = true };
        OnlineFriendBridgeHolder.Publish(face, new OnlineScope(TenantId, AppId, ServerId));

        var response = new RespFriendList();
        var handled = await FriendQueryBridge.TryQueryFriendListAsync(ActorOne, response);

        Assert.True(handled);
        Assert.Equal(-2, response.ErrorCode);
        Assert.Empty(response.Friends);
    }
}
