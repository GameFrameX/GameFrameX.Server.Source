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
/// <see cref="ServerHeartbeatEntity.LastHeartbeat"/> (native BSON dates keep the
/// cleanup delete's time-range comparison and the read/write time semantics uniform).
/// The route side needs no class map at all — its camelCase
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
    // CA2255 定点压制：BSON 约定必须先于本程序集任何文档首次序列化，而心跳与玩家路由文档可经由
    // Discovery 宿主在任意时点被序列化；本类为 internal，宿主无法显式调用注册，模块初始化器是唯一
    // 能保证该时序的机制。注册内容被限定到这两个发现文档基类，不触碰其他文档。
    // 解除条件：出现可证明先于首次序列化的显式宿主注册点后改为显式注册。
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "BSON 约定必须先于本程序集任何文档首次序列化，而发现文档可经由宿主在任意时点被序列化；internal 类的 [ModuleInitializer] 是唯一能保证该时序的机制。")]
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
            classMap.GetMemberMap(entity => entity.AdditionalAdvertiseEndpoints).SetIgnoreIfNull(true);
        });

        // 具体文档类型（实际被序列化 / 反序列化的派生类型，未声明任何自有成员）必须显式注册：
        // 其驱动自动创建的 class map 不经过 MongoSerializationRegistry 的 EnsureClassMapRegistered，
        // 默认 IgnoreExtraElements=false——携带未知字段的文档（混布窗口内新版本节点写入
        // additionalAdvertiseEndpoints）会被旧读取方反序列化为 FormatException（本机实验证实）。
        // 两个 setters 必须在 AutoMap() 之后调用：AutoMap() 会重放驱动的
        // IgnoreExtraElementsConvention(false)，顺序错了等于没设（参照 MongoSerializationRegistry）。
        // 继承成员（_id 映射、DateTime 序列化器与 SetIgnoreIfNull）全部由上面的基类 map 承载
        //（派生 map 不接受基类声明的成员，EnsureMemberInfoIsForThisClass 拒绝），派生 map 不重复配置。
        // The concrete document type (the derived type actually serialized / deserialized; it declares
        // no members of its own) must be registered explicitly: its driver-auto-created class map never
        // passes through MongoSerializationRegistry.EnsureClassMapRegistered and defaults to
        // IgnoreExtraElements=false — documents carrying unknown fields (a new-version node writing
        // additionalAdvertiseEndpoints inside a mixed-version window) would fail deserialization with a
        // FormatException on old readers (verified by a local experiment). Both setters must run after
        // AutoMap(): AutoMap() replays the driver's IgnoreExtraElementsConvention(false), so the reverse
        // order is a no-op (see MongoSerializationRegistry). The inherited members (the _id mapping, the
        // DateTime serializer, and SetIgnoreIfNull) are all carried by the base map above (the derived
        // map rejects base-declared members — EnsureMemberInfoIsForThisClass), so the derived map adds
        // nothing else.
        BsonClassMap.RegisterClassMap<Discovery.ServerHeartbeatDocument>(classMap =>
        {
            classMap.AutoMap();
            classMap.SetIgnoreExtraElements(true);
            classMap.SetIgnoreExtraElementsIsInherited(true);
        });
    }
}
