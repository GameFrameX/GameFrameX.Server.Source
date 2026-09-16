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
using GameFrameX.Apps.Player.Attribute;
using GameFrameX.Apps.Player.Attribute.Entity;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Hotfix.Logic.Player.Attribute;

/// <summary>
/// 玩家属性同步消息构建器。把玩家属性状态和最终值变化转换为面向客户端的同步消息。
/// </summary>
public static class PlayerAttributeSyncBuilder
{
    /// <summary>
    /// 根据玩家属性状态构建完整属性快照消息，包含全部最终值槽及其 Base/Add/Pct 调试字段。
    /// </summary>
    /// <param name="state">玩家属性状态。</param>
    /// <returns>属性快照消息。</returns>
    public static NotifyPlayerAttributeSync BuildSnapshot(PlayerAttributeState state)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var message = new NotifyPlayerAttributeSync();
        foreach (AttributeType attributeType in Enum.GetValues(typeof(AttributeType)))
        {
            if (attributeType == AttributeType.None)
            {
                continue;
            }

            if (!AttributeCore.IsFinalAttribute(attributeType))
            {
                continue;
            }

            message.Attributes.Add(BuildEntry(state, attributeType));
        }

        return message;
    }

    /// <summary>
    /// 根据最终属性变化构建增量同步消息。
    /// </summary>
    /// <param name="attributeType">发生变化的最终属性编号。</param>
    /// <param name="newValue">新的最终值。</param>
    /// <returns>属性增量消息。</returns>
    public static NotifyPlayerAttributeChanged BuildChanged(AttributeType attributeType, long newValue)
    {
        return new NotifyPlayerAttributeChanged
        {
            Type = (int)attributeType,
            Value = newValue,
        };
    }

    private static PlayerAttributeEntry BuildEntry(PlayerAttributeState state, AttributeType finalAttribute)
    {
        return new PlayerAttributeEntry
        {
            Type = (int)finalAttribute,
            Value = state.GetValue(finalAttribute),
            Base = state.GetValue(AttributeCore.GetSlotAttribute(finalAttribute, AttributeSlotKind.Base)),
            Add = state.GetValue(AttributeCore.GetSlotAttribute(finalAttribute, AttributeSlotKind.Add)),
            Pct = state.GetValue(AttributeCore.GetSlotAttribute(finalAttribute, AttributeSlotKind.Pct)),
        };
    }
}
