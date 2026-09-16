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


namespace GameFrameX.Apps.Player.Attribute;

/// <summary>
/// 玩家属性编号。最终值使用稳定主编号，派生槽使用固定偏移。
/// </summary>
public enum AttributeType
{
    /// <summary>
    /// 未知属性。
    /// </summary>
    None = 0,

    /// <summary>
    /// 生命最终值。
    /// </summary>
    Life = 1,

    /// <summary>
    /// 物理攻击最终值。
    /// </summary>
    PhysicalAttack = 2,

    /// <summary>
    /// 魔法攻击最终值。
    /// </summary>
    MagicAttack = 3,

    /// <summary>
    /// 物理防御最终值。
    /// </summary>
    PhysicalDefense = 4,

    /// <summary>
    /// 魔法防御最终值。
    /// </summary>
    MagicDefense = 5,

    /// <summary>
    /// 暴击最终值。
    /// </summary>
    Critical = 6,

    /// <summary>
    /// 爆伤最终值。
    /// </summary>
    CriticalDamage = 7,

    /// <summary>
    /// 精准最终值。
    /// </summary>
    Precision = 8,

    /// <summary>
    /// 格挡最终值。
    /// </summary>
    Block = 9,

    LifeBase = 10001,
    PhysicalAttackBase = 10002,
    MagicAttackBase = 10003,
    PhysicalDefenseBase = 10004,
    MagicDefenseBase = 10005,
    CriticalBase = 10006,
    CriticalDamageBase = 10007,
    PrecisionBase = 10008,
    BlockBase = 10009,

    LifeAdd = 20001,
    PhysicalAttackAdd = 20002,
    MagicAttackAdd = 20003,
    PhysicalDefenseAdd = 20004,
    MagicDefenseAdd = 20005,
    CriticalAdd = 20006,
    CriticalDamageAdd = 20007,
    PrecisionAdd = 20008,
    BlockAdd = 20009,

    LifePct = 30001,
    PhysicalAttackPct = 30002,
    MagicAttackPct = 30003,
    PhysicalDefensePct = 30004,
    MagicDefensePct = 30005,
    CriticalPct = 30006,
    CriticalDamagePct = 30007,
    PrecisionPct = 30008,
    BlockPct = 30009,

    LifeFinalAdd = 40001,
    PhysicalAttackFinalAdd = 40002,
    MagicAttackFinalAdd = 40003,
    PhysicalDefenseFinalAdd = 40004,
    MagicDefenseFinalAdd = 40005,
    CriticalFinalAdd = 40006,
    CriticalDamageFinalAdd = 40007,
    PrecisionFinalAdd = 40008,
    BlockFinalAdd = 40009,

    LifeFinalPct = 50001,
    PhysicalAttackFinalPct = 50002,
    MagicAttackFinalPct = 50003,
    PhysicalDefenseFinalPct = 50004,
    MagicDefenseFinalPct = 50005,
    CriticalFinalPct = 50006,
    CriticalDamageFinalPct = 50007,
    PrecisionFinalPct = 50008,
    BlockFinalPct = 50009
}
