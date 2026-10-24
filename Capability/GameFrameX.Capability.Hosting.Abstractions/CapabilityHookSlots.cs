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
/// 钩子槽位审计登记（跨契约的声明 / 挂载记账；装配期「已挂载但无能力声明」Info 审计的数据面）。
/// <para>
/// 维护约束：仅机制使用——声明记录来自模块 Register 期的 DeclareHook，挂载记录来自 Hotfix 装载期的
/// <see cref="CapabilityHook{TContract}.Attach"/>；先挂后声明是常态（热更装载在组合之前）。
/// </para>
/// </summary>
internal static class CapabilityHookSlots
{
    /// <summary>
    /// 登记锁。
    /// </summary>
    private static readonly object Sync = new object();

    /// <summary>
    /// 已声明的钩子契约类型 → 未释放的声明次数（能力 Register 期 +1、声明方宿主释放时 -1，归零移除）。
    /// <para>维护约束：必须按次数记账而不是集合——旧宿主释放与新宿主重新声明可能交错到达，
    /// 集合语义下迟到的释放会抹掉新声明，使「已挂载但无能力声明」审计误报。</para>
    /// </summary>
    private static readonly Dictionary<Type, int> DeclarationCountsByContract = new Dictionary<Type, int>();

    /// <summary>
    /// 已挂载的钩子契约类型集合（Hotfix 装载期；挂载后摘下仍保留记录——审计关注「出现过挂载」）。
    /// </summary>
    private static readonly HashSet<Type> AttachedContracts = new HashSet<Type>();

    /// <summary>
    /// 登记一次钩子声明（模块 Register 期；声明次数 +1）。
    /// </summary>
    /// <param name="contract">钩子契约类型。</param>
    internal static void MarkDeclared(Type contract)
    {
        lock (Sync)
        {
            DeclarationCountsByContract.TryGetValue(contract, out var count);
            DeclarationCountsByContract[contract] = count + 1;
        }
    }

    /// <summary>
    /// 撤销一次钩子声明登记（声明方宿主释放声明时调用；与 <see cref="MarkDeclared"/> 成对，次数 -1，归零移除）。
    /// </summary>
    /// <param name="contract">钩子契约类型。</param>
    internal static void MarkUndeclared(Type contract)
    {
        lock (Sync)
        {
            if (!DeclarationCountsByContract.TryGetValue(contract, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                DeclarationCountsByContract.Remove(contract);
            }
            else
            {
                DeclarationCountsByContract[contract] = count - 1;
            }
        }
    }

    /// <summary>
    /// 登记一次钩子挂载（Hotfix 装载期）。
    /// </summary>
    /// <param name="contract">钩子契约类型。</param>
    internal static void MarkAttached(Type contract)
    {
        lock (Sync)
        {
            AttachedContracts.Add(contract);
        }
    }

    /// <summary>
    /// 获取已挂载但没有任何能力声明的钩子契约集合（对应能力未装配或未声明该钩子；装配期打 Info）。
    /// </summary>
    /// <returns>未被声明的已挂载契约类型集合。</returns>
    internal static IReadOnlyList<Type> GetAttachedWithoutDeclaration()
    {
        lock (Sync)
        {
            var result = new List<Type>();
            foreach (var contract in AttachedContracts)
            {
                if (!DeclarationCountsByContract.ContainsKey(contract))
                {
                    result.Add(contract);
                }
            }

            return result;
        }
    }
}
