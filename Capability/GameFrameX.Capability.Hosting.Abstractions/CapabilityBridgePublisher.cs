// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
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

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 能力桥发布面（激活上下文的 <c>Bridges</c> 属性；发布者身份 = 当前模块 manifest Id，由机制注入）。
/// <para>
/// 维护约束：<see cref="Publish{TContract}"/> 只在能力模块 <c>ActivateAsync</c> 内合法（GFX0018 符号检查）；
/// 同一契约重复发布 = 装配失败（一个契约只有一个发布者）；撤销由宿主停止期按发布记录自动完成，
/// 模块与组合根不得自行撤销。
/// </para>
/// </summary>
public sealed class CapabilityBridgePublisher
{
    /// <summary>
    /// 发布者能力 Id（由机制注入）。
    /// </summary>
    private readonly string _publisherCapabilityId;

    /// <summary>
    /// 撤销凭据收集器（宿主停止期统一撤销）。
    /// </summary>
    private readonly List<IDisposable> _publications;

    /// <summary>
    /// 初始化 <see cref="CapabilityBridgePublisher"/>（仅机制构造）。
    /// </summary>
    /// <param name="publisherCapabilityId">发布者能力 Id。</param>
    /// <param name="publications">撤销凭据收集器（宿主持有）。</param>
    internal CapabilityBridgePublisher(string publisherCapabilityId, List<IDisposable> publications)
    {
        _publisherCapabilityId = publisherCapabilityId;
        _publications = publications;
    }

    /// <summary>
    /// 发布一个契约实现（撤销由宿主停止期自动完成；消费方经 <see cref="CapabilityBridge{TContract}.TryAcquire"/> 取租约）。
    /// </summary>
    /// <typeparam name="TContract">契约类型（每域一个契约接口）。</typeparam>
    /// <param name="contract">契约实现（不可为 null）。</param>
    public void Publish<TContract>(TContract contract) where TContract : class
    {
        if (contract == null)
        {
            throw new ArgumentNullException(nameof(contract));
        }

        var publication = CapabilityBridge<TContract>.Publish(contract, _publisherCapabilityId);
        _publications.Add(publication);
    }
}
