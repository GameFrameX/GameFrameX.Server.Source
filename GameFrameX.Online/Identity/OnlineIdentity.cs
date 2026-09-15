//  ==========================================================================================
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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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

namespace GameFrameX.Online.Identity;

/// <summary>
/// 外源身份实体（vault:C3 S2.2：邮箱/设备/渠道/第三方身份，绑定明确的 TenantId + AppId）。
/// <para>
/// 维护约束：唯一性键 = (TenantId, AppId, Kind, Identifier)，四元组重复即同一身份；
/// 凭证材料（口令散列、第三方 token）由装配层凭据组件持有，本实体只承载身份指向；
/// 解绑后 <see cref="UnboundAtTime"/> 置位，同一 Identifier 可被重新绑定（换绑显式操作）。
/// </para>
/// </summary>
public sealed class OnlineIdentity
{
    /// <summary>
    /// 获取或设置身份标识（服务端生成，全局唯一）。
    /// </summary>
    public string Id
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（身份必须绑定明确租户）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置应用标识（身份必须绑定明确应用）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置身份类型。
    /// </summary>
    public OnlineIdentityKind Kind
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置身份标识串（按 <see cref="Kind"/> 语义解释：用户名/邮箱/设备号/渠道标识/第三方 OpenId）。
    /// </summary>
    public string Identifier
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置归属游戏账号标识。
    /// </summary>
    public long GameAccountId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时间（Unix 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近更新时间（Unix 毫秒）。
    /// </summary>
    public long UpdatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置解绑时间（Unix 毫秒；0 = 绑定中）。
    /// </summary>
    public long UnboundAtTime
    {
        get;
        set;
    }
}
