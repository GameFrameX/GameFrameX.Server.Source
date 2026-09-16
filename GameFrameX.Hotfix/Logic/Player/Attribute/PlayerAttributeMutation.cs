// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
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


using System;
using System.Collections.Generic;
using GameFrameX.Apps.Player.Attribute;

namespace GameFrameX.Hotfix.Logic.Player.Attribute;

/// <summary>
/// 玩家属性状态写入与最终值重算逻辑。
/// </summary>
public static class PlayerAttributeMutation
{
    /// <summary>
    /// 将属性值写入状态字典，并返回最终属性变化判定。
    /// </summary>
    /// <param name="values">状态字典。</param>
    /// <param name="attributeType">属性编号。</param>
    /// <param name="value">新属性值。</param>
    /// <param name="silent">是否静默写入。</param>
    /// <returns>属性写入结果。</returns>
    public static AttributeChangeResult ApplyValue(Dictionary<int, long> values, AttributeType attributeType, long value, bool silent)
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        var oldValue = GetValue(values, attributeType);
        if (oldValue == value)
        {
            return AttributeChangeResult.NotChanged(attributeType, value);
        }

        values[(int)attributeType] = value;

        if (AttributeCore.TryGetFinalAttribute(attributeType, out var finalAttribute))
        {
            var oldFinalValue = GetValue(values, finalAttribute);
            var newFinalValue = AttributeCore.RecalculateFinal(ToAttributeDictionary(values), finalAttribute);
            values[(int)finalAttribute] = newFinalValue;
            return new AttributeChangeResult(attributeType, finalAttribute, oldFinalValue, newFinalValue, true, oldFinalValue != newFinalValue && !silent);
        }

        if (AttributeCore.IsFinalAttribute(attributeType))
        {
            return new AttributeChangeResult(attributeType, attributeType, oldValue, value, true, !silent);
        }

        return new AttributeChangeResult(attributeType, AttributeType.None, 0L, 0L, true, false);
    }

    private static long GetValue(Dictionary<int, long> values, AttributeType attributeType)
    {
        long value;
        return values.TryGetValue((int)attributeType, out value) ? value : 0L;
    }

    private static Dictionary<AttributeType, long> ToAttributeDictionary(Dictionary<int, long> values)
    {
        var attributes = new Dictionary<AttributeType, long>();
        foreach (var item in values)
        {
            attributes[(AttributeType)item.Key] = item.Value;
        }

        return attributes;
    }
}

/// <summary>
/// 属性写入后的最终属性变化判定。
/// </summary>
public sealed class AttributeChangeResult
{
    public AttributeType SourceAttributeType { get; }

    public AttributeType FinalAttributeType { get; }

    public long OldFinalValue { get; }

    public long NewFinalValue { get; }

    public bool StateChanged { get; }

    public bool ShouldDispatch { get; }

    public AttributeChangeResult(AttributeType sourceAttributeType, AttributeType finalAttributeType, long oldFinalValue, long newFinalValue, bool stateChanged, bool shouldDispatch)
    {
        SourceAttributeType = sourceAttributeType;
        FinalAttributeType = finalAttributeType;
        OldFinalValue = oldFinalValue;
        NewFinalValue = newFinalValue;
        StateChanged = stateChanged;
        ShouldDispatch = shouldDispatch;
    }

    public static AttributeChangeResult NotChanged(AttributeType sourceAttributeType, long value)
    {
        return new AttributeChangeResult(sourceAttributeType, AttributeType.None, value, value, false, false);
    }
}
