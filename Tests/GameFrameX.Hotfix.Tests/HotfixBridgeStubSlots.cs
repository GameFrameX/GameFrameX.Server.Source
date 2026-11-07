// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or to infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using GameFrameX.Capability.Online.Abstractions.Contracts;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 域桥桩模块的静态槽位（用例在组合前写入要发布的桥契约；全部用例须落
/// <see cref="HotfixBridgeTestCollection"/> 串行集合，避免槽位与进程级桥槽被并行用例交叉覆写）。
/// </summary>
public static class HotfixBridgeStubSlots
{
    /// <summary>要发布的好友域桥契约（null = 不发布）。</summary>
    public static IOnlineFriendDomain FriendDomain;

    /// <summary>要发布的匹配域桥契约（null = 不发布）。</summary>
    public static IOnlineMatchDomain MatchDomain;

    /// <summary>是否发布授权作用域提供者桥（缺省 true；false 用于锚定「域桥在场、作用域提供者缺席」）。</summary>
    public static bool PublishScopeProvider = true;

    /// <summary>
    /// 复位全部槽位（组合辅助在 finally 中调用，保证缺省态不泄漏到后续用例）。
    /// </summary>
    public static void Reset()
    {
        FriendDomain = null;
        MatchDomain = null;
        PublishScopeProvider = true;
    }
}
