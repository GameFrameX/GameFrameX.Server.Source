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
//   Any disputes or liabilities arising from secondary development based on this project
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
//  ==========================================================================================

using System.Collections.Generic;

namespace GameFrameX.Online.HotfixRollback;

/// <summary>
/// Hotfix 版本协议清单（vault:C9 S8.5：一个 Hotfix 程序集版本对外承诺的协议面快照；
/// 协议兼容检查以「当前活跃清单 vs 目标回滚清单」比对）。
/// <para>
/// 维护约束（红线）：
/// ① 清单是 <b>App 级资产</b>——作用域锚定 <see cref="TenantId"/>/<see cref="AppId"/> 两键，
/// ServerId 固定 0（对齐 C105 统一审计「远程配置等 App 级操作」口径）；
/// ② 清单一经登记<b>不可变更</b>（存储按版本号判重，重复登记幂等回执、不覆盖既有行——
/// 回滚兼容判定的前提是历史清单稳定）；版本号须与 Hotfix 程序集版本（<c>HotfixManager.dllVersion</c> 契约）一致；
/// ③ 版本号禁止包含竖线 <c>|</c>（幂等回放令牌与规范化请求文本的分隔符，防止回放令牌损坏）。
/// </para>
/// </summary>
public sealed class OnlineHotfixProtocolManifest
{
    /// <summary>
    /// 获取或设置版本号（与 Hotfix 程序集版本一致；同作用域内唯一，禁止包含 <c>|</c>）。
    /// </summary>
    public string Version
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（必填，必须大于 0）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（必填，必须大于 0）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置协议消息契约行集合（同清单内消息名与消息号均唯一；可为空集合，表示该版本无对外协议面）。
    /// </summary>
    public IReadOnlyList<OnlineHotfixProtocolMessage> Messages
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置登记时刻（UTC 毫秒；发布流水线簿记字段）。
    /// </summary>
    public long RegisteredTime
    {
        get;
        set;
    }
}
