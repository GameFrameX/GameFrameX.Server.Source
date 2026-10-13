// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of citizens as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织不承担任何责任，开发者应自行承担。
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

using GameFrameX.Capability.Hosting.Core;

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 社交能力包的 hosting 扩展（C231 P3）：<see cref="AddOnlineSocialRuntime"/> 装配入口（回调式，注册自装配模块）
/// 与六个存储替换缝（<see cref="UseOnlineChatStore"/> 等 UseOnlineSocial*Store 缝族）。
/// <para>
/// 维护约束：options 经共享服务面预置（模块无参构造，显式链式与 P4 真扫描共用同一模块类）；
/// 替换缝在模块 <c>Register</c> 期探测命中，未命中构造 InMemory 默认。
/// </para>
/// </summary>
/// <remarks>
/// Hosting extensions of the social capability package (C231 P3): the <see cref="AddOnlineSocialRuntime"/>
/// assembly entry (callback style, registering the self-assembling module) plus six store replacement seams
/// (the <c>UseOnlineSocial*Store</c> family). Options are pre-seeded through the shared service face; the
/// replacement seams are probed at module <c>Register</c> time with in-memory fallbacks.
/// </remarks>
public static class OnlineSocialHostingExtensions
{
    /// <summary>
    /// 装配社交能力运行时（注册自装配模块；<c>options => ...</c> 回调承载包级编排参数）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="configure">包级选项回调（可空 = 全默认）。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder AddOnlineSocialRuntime(this CapabilityHostBuilder builder, Action<OnlineSocialOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new OnlineSocialOptions();
        configure?.Invoke(options);
        builder.UseService(options);
        return builder.AddModule(new OnlineSocialCapabilityModule());
    }

    /// <summary>
    /// 替换聊天存储（频道消息正文与参与者）（默认 InMemory，生产持久化实现经本缝注入）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="store">存储实现。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineChatStore(this CapabilityHostBuilder builder, IOnlineChatStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(store);
        return builder;
    }
    /// <summary>
    /// 替换好友存储（默认 InMemory，生产持久化实现经本缝注入）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="store">存储实现。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineFriendStore(this CapabilityHostBuilder builder, IOnlineFriendStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(store);
        return builder;
    }
    /// <summary>
    /// 替换群组存储（默认 InMemory，生产持久化实现经本缝注入）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="store">存储实现。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineGroupStore(this CapabilityHostBuilder builder, IOnlineGroupStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(store);
        return builder;
    }
    /// <summary>
    /// 替换通知存储（默认 InMemory，生产持久化实现经本缝注入）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="store">存储实现。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineNotificationStore(this CapabilityHostBuilder builder, IOnlineNotificationStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(store);
        return builder;
    }
    /// <summary>
    /// 替换举报存储（默认 InMemory，生产持久化实现经本缝注入）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="store">存储实现。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineReportStore(this CapabilityHostBuilder builder, IOnlineReportStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(store);
        return builder;
    }
    /// <summary>
    /// 替换社交关系存储（处罚读取面）（默认 InMemory，生产持久化实现经本缝注入）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="store">存储实现。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineSocialGraphStore(this CapabilityHostBuilder builder, IOnlineSocialGraphStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(store);
        return builder;
    }
}
