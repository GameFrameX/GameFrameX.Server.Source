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

using System.Reflection;
using GameFrameX.Apps.Player.Player.Component;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Utility.Setting;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Events;
using GameFrameX.Core.Hotfix;
using GameFrameX.Hotfix.Logic.Player.Login;
using GameFrameX.Hotfix.Logic.Server;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Utility;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 共享 <see cref="HotfixManager" /> / <see cref="ComponentRegister" /> 进程级静态状态的测试禁并行 Collection
///（<c>ComponentAgentEventDispatchTests</c> / <c>EventBindingTests</c> / <c>HotfixResolverInjectionTests</c> 串行，
/// 与主套件 <c>OnlineAdminHotfixDispatcherTests</c> 同名 Collection 隔离口径一致）。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HotfixManagerStaticStateCollection
{
    /// <summary>Collection 名。</summary>
    public const string Name = "HotfixManagerStaticState";
}
