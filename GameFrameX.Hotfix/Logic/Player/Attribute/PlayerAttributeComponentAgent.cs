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


using GameFrameX.Apps.Common.Event;
using GameFrameX.Apps.Common.EventData;
using GameFrameX.Apps.Player.Attribute;
using GameFrameX.Apps.Player.Attribute.Component;
using GameFrameX.Apps.Player.Attribute.Entity;
using GameFrameX.Hotfix.Common.Events;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Hotfix.Logic.Player.Attribute;

public class PlayerAttributeComponentAgent : StateComponentAgent<PlayerAttributeComponent, PlayerAttributeState>
{
    /// <summary>
    /// 读取玩家属性值，缺失属性默认返回 0。
    /// </summary>
    /// <param name="attributeType">属性编号。</param>
    /// <returns>当前属性值。</returns>
    public long Get(AttributeType attributeType)
    {
        return OwnerComponent.State.GetValue(attributeType);
    }

    /// <summary>
    /// 设置属性值。派生槽会重算最终属性，最终值变化时派发事件。
    /// </summary>
    /// <param name="attributeType">属性编号。</param>
    /// <param name="value">新属性值。</param>
    public Task Set(AttributeType attributeType, long value)
    {
        return Set(attributeType, value, false);
    }

    /// <summary>
    /// 静默设置属性值，适用于初始化或批量装载。
    /// </summary>
    /// <param name="attributeType">属性编号。</param>
    /// <param name="value">新属性值。</param>
    public Task SetSilent(AttributeType attributeType, long value)
    {
        return Set(attributeType, value, true);
    }

    /// <summary>
    /// 累加属性值。派生槽会重算最终属性，最终值变化时派发事件。
    /// </summary>
    /// <param name="attributeType">属性编号。</param>
    /// <param name="value">累加值。</param>
    public Task Add(AttributeType attributeType, long value)
    {
        return Set(attributeType, Get(attributeType) + value, false);
    }

    /// <summary>
    /// 静默初始化多项属性，只写回状态，不派发业务事件。
    /// </summary>
    /// <param name="attributes">属性初始值。</param>
    public async Task InitializeSilent(Dictionary<AttributeType, long> attributes)
    {
        if (attributes == null)
        {
            throw new ArgumentNullException(nameof(attributes));
        }

        var changed = false;
        foreach (var attribute in attributes)
        {
            changed |= PlayerAttributeMutation.ApplyValue(OwnerComponent.State.Values, attribute.Key, attribute.Value, true).StateChanged;
        }

        if (changed)
        {
            await OwnerComponent.WriteStateAsync();
        }
    }

    /// <summary>
    /// 静默补齐玩家第一版基础属性默认值。已有基础属性槽不会被覆盖，避免重复登录重置成长结果。
    /// </summary>
    public async Task InitializeDefaultsSilent()
    {
        if (PlayerInitialAttributeDefaults.ApplyMissing(OwnerComponent.State.Values))
        {
            await OwnerComponent.WriteStateAsync();
        }
    }

    /// <summary>
    /// 构建当前玩家属性的完整快照消息，用于登录后或重连时同步给客户端。
    /// </summary>
    /// <returns>属性快照消息。</returns>
    public NotifyPlayerAttributeSync BuildSyncSnapshot()
    {
        return PlayerAttributeSyncBuilder.BuildSnapshot(OwnerComponent.State);
    }

    private async Task Set(AttributeType attributeType, long value, bool silent)
    {
        var result = PlayerAttributeMutation.ApplyValue(OwnerComponent.State.Values, attributeType, value, silent);
        if (!result.StateChanged)
        {
            return;
        }

        await OwnerComponent.WriteStateAsync();
        if (result.ShouldDispatch)
        {
            this.Dispatch(EventId.AttributeChanged, new AttributeChangedEventArgs(ActorId, result.FinalAttributeType, result.SourceAttributeType, result.OldFinalValue, result.NewFinalValue));
        }
    }

}
