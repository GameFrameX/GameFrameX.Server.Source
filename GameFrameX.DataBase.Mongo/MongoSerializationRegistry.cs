// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code.
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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Options;

namespace GameFrameX.DataBase.Mongo;

/// <summary>
/// 字典序列化形态约定：统一使用 <see cref="DictionaryRepresentation.ArrayOfDocuments"/>（存量数据兼容形态）。
/// </summary>
/// <remarks>
/// Dictionary representation convention: forces <see cref="DictionaryRepresentation.ArrayOfDocuments"/>
/// (the legacy-compatible on-disk form).
/// </remarks>
internal sealed class DictionaryRepresentationConvention : ConventionBase, IMemberMapConvention
{
    private readonly DictionaryRepresentation _dictionaryRepresentation;

    /// <summary>
    /// 初始化字典序列化形态约定。
    /// </summary>
    /// <remarks>
    /// Initializes the dictionary representation convention.
    /// </remarks>
    /// <param name="dictionaryRepresentation">字典序列化形态 / Dictionary representation</param>
    public DictionaryRepresentationConvention(DictionaryRepresentation dictionaryRepresentation = DictionaryRepresentation.ArrayOfDocuments)
    {
        _dictionaryRepresentation = dictionaryRepresentation;
    }

    /// <summary>
    /// 应用约定到成员映射。
    /// </summary>
    /// <remarks>
    /// Applies the convention to the member map.
    /// </remarks>
    /// <param name="memberMap">成员映射 / Member map</param>
    public void Apply(BsonMemberMap memberMap)
    {
        var serializer = memberMap.GetSerializer();
        if (serializer is IDictionaryRepresentationConfigurable dictionaryRepresentationConfigurable)
        {
            var reconfiguredSerializer = dictionaryRepresentationConfigurable.WithDictionaryRepresentation(_dictionaryRepresentation);
            memberMap.SetSerializer(reconfiguredSerializer);
        }
    }
}

/// <summary>
/// 空容器跳过约定：空 <see cref="List{T}"/> / 字典成员不写入文档（保持存量文档形态）。
/// </summary>
/// <remarks>
/// Empty-container skipping convention: empty <see cref="List{T}"/> / dictionary members are omitted
/// from the document (preserves the legacy on-disk form).
/// </remarks>
internal sealed class EmptyContainerSerializeMethodConvention : ConventionBase, IMemberMapConvention
{
    /// <summary>
    /// 应用约定到成员映射。
    /// </summary>
    /// <remarks>
    /// Applies the convention to the member map.
    /// </remarks>
    /// <param name="memberMap">成员映射 / Member map</param>
    public void Apply(BsonMemberMap memberMap)
    {
        if (!memberMap.MemberType.IsGenericType)
        {
            return;
        }

        var genType = memberMap.MemberType.GetGenericTypeDefinition();
        if (genType == typeof(List<>))
        {
            SetListShouldSerializeWhenNonEmpty(memberMap);
        }
        else if (genType == typeof(ConcurrentDictionary<,>) || genType == typeof(Dictionary<,>))
        {
            SetDictionaryShouldSerializeWhenNonEmpty(memberMap);
        }
    }

    private static void SetListShouldSerializeWhenNonEmpty(BsonMemberMap memberMap)
    {
        memberMap.SetShouldSerializeMethod(o =>
        {
            var value = memberMap.Getter(o);
            if (value is IList list)
            {
                return list.Count > 0;
            }

            return true;
        });
    }

    private static void SetDictionaryShouldSerializeWhenNonEmpty(BsonMemberMap memberMap)
    {
        memberMap.SetShouldSerializeMethod(o =>
        {
            if (o == null)
            {
                return true;
            }

            var value = memberMap.Getter(o);
            if (value == null)
            {
                return true;
            }

            var countProperty = value.GetType().GetProperty("Count");
            if (countProperty == null)
            {
                return true;
            }

            var count = (int)countProperty.GetValue(value, null);
            return count > 0;
        });
    }
}

/// <summary>
/// Mongo 序列化准备注册器：把全部 BSON 序列化指令（ConventionPack 与 ClassMap）内聚在 Mongo Provider 模块内，
/// 业务状态对象零 Mongo 特性、零 Mongo 基类即可获得与旧 <c>CacheState</c> 子类一致的序列化行为。
/// </summary>
/// <remarks>
/// Mongo serialization preparation registry: keeps all BSON serialization directives
/// (ConventionPack and ClassMap registration) inside the Mongo provider module, so business state
/// objects need no Mongo attributes and no Mongo base class while keeping the exact serialization
/// behavior previously provided by the <c>CacheState</c> subclass.
/// </remarks>
internal static class MongoSerializationRegistry
{
    private const int NotRegistered = 0;
    private const int Registered = 1;

    private static readonly object RegistrationLock = new();
    private static int _conventionsRegistered;

    /// <summary>
    /// 确保全局 ConventionPack 已注册（幂等、线程安全）。
    /// 必须在任何 ClassMap <c>AutoMap</c> 之前完成，否则字典形态会退回 Dynamic，破坏存量数据读写。
    /// </summary>
    /// <remarks>
    /// Ensures the global ConventionPack is registered (idempotent, thread-safe).
    /// This MUST run before any ClassMap <c>AutoMap</c>; otherwise dictionary representation falls back
    /// to Dynamic and breaks legacy data read/write.
    /// </remarks>
    public static void EnsureConventionsRegistered()
    {
        if (Volatile.Read(ref _conventionsRegistered) == Registered)
        {
            return;
        }

        lock (RegistrationLock)
        {
            if (Volatile.Read(ref _conventionsRegistered) == Registered)
            {
                return;
            }

            ConventionRegistry.Register(nameof(DictionaryRepresentationConvention),
                                        new ConventionPack { new DictionaryRepresentationConvention(), }, _ => true);

            ConventionRegistry.Register(nameof(EmptyContainerSerializeMethodConvention),
                                        new ConventionPack { new EmptyContainerSerializeMethodConvention(), }, _ => true);

            Volatile.Write(ref _conventionsRegistered, Registered);
        }
    }

    /// <summary>
    /// 确保指定文档类型的 ClassMap 已注册（懒注册、防重、线程安全）。
    /// 等价于旧 <c>[BsonIgnoreExtraElements(true, Inherited = true)]</c> 基类特性：忽略存量文档中的多余元素，
    /// 从而在字段删除后旧文档仍可反序列化。
    /// </summary>
    /// <remarks>
    /// Ensures the ClassMap of the document type is registered (lazy, duplicate-safe, thread-safe).
    /// Equivalent to the legacy <c>[BsonIgnoreExtraElements(true, Inherited = true)]</c> base attribute:
    /// extra elements in legacy documents are ignored so documents survive field removal.
    /// </remarks>
    /// <typeparam name="T">文档类型 / Document type</typeparam>
    public static void EnsureClassMapRegistered<T>()
    {
        EnsureClassMapRegistered(typeof(T));
    }

    /// <summary>
    /// 确保指定文档类型的 ClassMap 已注册（懒注册、防重、线程安全）。
    /// </summary>
    /// <remarks>
    /// Ensures the ClassMap of the document type is registered (lazy, duplicate-safe, thread-safe).
    /// </remarks>
    /// <param name="type">文档类型 / Document type</param>
    public static void EnsureClassMapRegistered(Type type)
    {
        if (BsonClassMap.IsClassMapRegistered(type))
        {
            return;
        }

        // 防御性时序检查：ConventionPack 必须先于本类型的首次 AutoMap（见 EnsureConventionsRegistered 备注）。
        // Defensive ordering check: the ConventionPack must precede this type's first AutoMap (see EnsureConventionsRegistered remarks).
        EnsureConventionsRegistered();

        lock (RegistrationLock)
        {
            if (BsonClassMap.IsClassMapRegistered(type))
            {
                return;
            }

            var bsonClassMap = new BsonClassMap(type);
            bsonClassMap.AutoMap();
            bsonClassMap.SetIgnoreExtraElements(true);
            bsonClassMap.SetIgnoreExtraElementsIsInherited(true);
            BsonClassMap.RegisterClassMap(bsonClassMap);
        }
    }
}
