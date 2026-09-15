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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using System;
using System.Collections.Generic;

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 单个标准事件的登记项（事件名 → 分类 + 当前版本 + 必需载荷字段）。
/// <para>
/// 维护约束（红线）：
/// (1) <see cref="CurrentVersion"/> 只增不减——破坏性变更必须升版本（新旧版本并存期由登记表同时声明），
/// 非破坏性变更（新增可选字段）不升版本；
/// (2) <see cref="RequiredFields"/> 是 **L0 校验的唯一依据**，字段名必须与投影方写入的载荷字段一一对应；
/// 新增必需字段 = 对既有投递方的破坏性变更，必须同时升版本；
/// (3) 登记项一经发布即冻结，实例构造后只读（集合以只读视图暴露）。
/// </para>
/// </summary>
public sealed class OnlineGameEventDescriptor
{
    /// <summary>必需字段的只读快照（构造后不可变）。</summary>
    private readonly List<string> _requiredFields;

    /// <summary>
    /// 初始化 <see cref="OnlineGameEventDescriptor"/>。
    /// </summary>
    /// <param name="name">事件名（取 <see cref="OnlineGameEventName"/> 常量）。</param>
    /// <param name="category">事件分类。</param>
    /// <param name="currentVersion">当前版本（从 1 起，只增不减）。</param>
    /// <param name="requiredFields">必需载荷字段（缺一即拒绝投递）。</param>
    public OnlineGameEventDescriptor(string name, OnlineGameEventCategory category, int currentVersion, params string[] requiredFields)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("事件名不能为空", nameof(name));
        }

        if (currentVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(currentVersion), "事件版本必须从 1 起");
        }

        Name = name;
        Category = category;
        CurrentVersion = currentVersion;
        _requiredFields = requiredFields == null ? new List<string>() : new List<string>(requiredFields);
    }

    /// <summary>
    /// 获取事件名（跨仓契约标识）。
    /// </summary>
    public string Name
    {
        get;
    }

    /// <summary>
    /// 获取事件分类。
    /// </summary>
    public OnlineGameEventCategory Category
    {
        get;
    }

    /// <summary>
    /// 获取当前版本（投递版本必须落在 1..<see cref="CurrentVersion"/> 内）。
    /// </summary>
    public int CurrentVersion
    {
        get;
    }

    /// <summary>
    /// 获取必需载荷字段（只读视图；缺任一字段即拒绝投递）。
    /// </summary>
    public IReadOnlyList<string> RequiredFields
    {
        get { return _requiredFields; }
    }
}
