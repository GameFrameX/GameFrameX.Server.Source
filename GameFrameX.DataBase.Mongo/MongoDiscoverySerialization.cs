// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
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
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Runtime.CompilerServices;
using GameFrameX.Discovery;
using GameFrameX.Discovery.Routing;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace GameFrameX.DataBase.Mongo;

/// <summary>
/// 发现层文档的统一序列化规则：scoped camelCase 元素名约定 + 心跳文档的两处显式例外。
/// </summary>
/// <remarks>
/// The unified serialization rule of the discovery-layer documents: a scoped
/// camelCase element-name convention covering every inherited property of both
/// entity bases (the single naming rule — no per-field element literals), plus the
/// two heartbeat specifics that cannot be expressed by a naming rule:
/// <see cref="ServerHeartbeatEntity.InstanceId"/> as the document <c>_id</c> key
/// (<c>MapIdMember</c>) and the BSON DateTime representation of
/// <see cref="ServerHeartbeatEntity.LastHeartbeat"/> (the TTL index only fires on
/// native BSON dates). The route side needs no class map at all — its camelCase
/// mapping comes entirely from the convention. Element names are byte-identical to
/// the former attribute form, so existing control databases need no migration.
/// The module initializer registers the convention before any first use of the
/// documents in this assembly; the pack is filtered to the two discovery bases, so
/// it never touches other documents (and never interacts with the lazily
/// registered global packs of <c>MongoSerializationRegistry</c>).
/// </remarks>
internal static class MongoDiscoverySerialization
{
    /// <summary>
    /// 注册统一规则（模块加载时执行，先于任何文档序列化）。
    /// </summary>
    /// <remarks>
    /// Registers the unified rule (runs at module load, before any document
    /// serialization).
    /// </remarks>
    [ModuleInitializer]
    internal static void Register()
    {
        var camelCasePack = new ConventionPack { new CamelCaseElementNameConvention(), };
        ConventionRegistry.Register("GameFrameXDiscoveryCamelCase", camelCasePack, static type => type.IsAssignableTo(typeof(ServerHeartbeatEntity)) || type.IsAssignableTo(typeof(PlayerRouteEntity)));

        BsonClassMap.RegisterClassMap<ServerHeartbeatEntity>(classMap =>
        {
            classMap.AutoMap();
            classMap.MapIdMember(entity => entity.InstanceId);
            classMap.GetMemberMap(entity => entity.LastHeartbeat).SetSerializer(new DateTimeSerializer(BsonType.DateTime));
        });
    }
}
