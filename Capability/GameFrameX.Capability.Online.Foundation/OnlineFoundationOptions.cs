// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://github.com/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Foundation;

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Foundation.Storage;

/// <summary>
/// 平台地基能力包级选项（C294 充实：聚合玩家 KV 存储限制参数并参与机制三层合并）。
/// <para>
/// 维护约束：本类型是 online-foundation 能力唯一的 Options 根类型（默认值唯一来源 = 字段初始值，
/// 经机制配置段 <c>Online.Settings.online-foundation</c> 与代码 Configure 逐层覆盖后统一
/// <see cref="Validate"/>）；玩家 KV 存储域服务本模块仍不装配（壳时代死装配），此处仅披露其参数面，
/// 消费方出现时随装配演进。
/// </para>
/// </summary>
/// <remarks>
/// Package-level options of the foundation capability (enriched by C294: aggregates the player
/// key-value storage limits and joins the mechanism's three-layer options merge). This type is the
/// single options root of the capability (defaults originate solely from field initializers); the
/// player storage domain service stays unassembled — the parameters are disclosed here only.
/// </remarks>
public sealed class OnlineFoundationOptions : ICapabilityOptions
{
    /// <summary>
    /// 获取或设置玩家 KV 存储限制参数（Storage 域服务本模块不装配，参数面先行披露）。
    /// </summary>
    public OnlinePlayerStorageOptions PlayerStorage { get; set; } = new OnlinePlayerStorageOptions();

    /// <summary>
    /// 校验玩家键值存储限制参数：最大负载字节数、单集合最大键数与最大分页大小均须为正数。
    /// </summary>
    /// <remarks>
    /// Validates the player key-value storage limits: maximum payload bytes, maximum keys per collection and maximum page size must
    /// all be positive. Failures are recorded on the validation collector.
    /// </remarks>
    /// <param name="validation">选项校验结果收集器 / Options validation collector</param>
    public void Validate(CapabilityOptionsValidation validation)
    {
        validation.RequirePositive("PlayerStorage.MaxPayloadBytes", PlayerStorage.MaxPayloadBytes);
        validation.RequirePositive("PlayerStorage.MaxKeysPerCollection", PlayerStorage.MaxKeysPerCollection);
        validation.RequirePositive("PlayerStorage.MaxPageSize", PlayerStorage.MaxPageSize);
    }
}
