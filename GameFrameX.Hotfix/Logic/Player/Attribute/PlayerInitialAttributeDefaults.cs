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
/// 玩家登录初始化使用的第一版基础属性默认值。
/// </summary>
internal static class PlayerInitialAttributeDefaults
{
    // ponytail: 当前仓库没有玩家初始属性配置表；配置链路落地后把这里替换为配置读取。
    private static readonly IReadOnlyDictionary<AttributeType, long> DefaultBaseValues = new Dictionary<AttributeType, long>
    {
        [AttributeType.LifeBase] = 1000,
        [AttributeType.PhysicalAttackBase] = 100,
        [AttributeType.MagicAttackBase] = 100,
        [AttributeType.PhysicalDefenseBase] = 50,
        [AttributeType.MagicDefenseBase] = 50,
        [AttributeType.CriticalBase] = 0,
        [AttributeType.CriticalDamageBase] = 15000,
        [AttributeType.PrecisionBase] = 0,
        [AttributeType.BlockBase] = 0
    };

    /// <summary>
    /// 只补齐缺失的基础属性槽，并静默重算对应最终属性。
    /// </summary>
    /// <param name="values">玩家属性状态字典。</param>
    /// <returns>如果状态发生变化则返回 true。</returns>
    public static bool ApplyMissing(Dictionary<int, long> values)
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        var changed = false;
        foreach (var attribute in DefaultBaseValues)
        {
            if (values.ContainsKey((int)attribute.Key))
            {
                continue;
            }

            changed |= PlayerAttributeMutation.ApplyValue(values, attribute.Key, attribute.Value, true).StateChanged;
        }

        return changed;
    }
}
