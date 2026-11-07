using ProtoBuf;
using ProtoBuf.Meta;

namespace GameFrameX.ProtoBuf.Net;

/// <summary>
/// 消息序列化帮助类
/// </summary>
public static class ProtoBufSerializerHelper
{
    /// <summary>
    /// 序列化对象
    /// </summary>
    /// <param name="value"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static byte[] Serialize<T>(T value)
    {
        using (var memoryStream = new MemoryStream())
        {
            Serializer.Serialize(memoryStream, value);
            return memoryStream.ToArray();
        }
    }

    /// <summary>
    /// 反序列化数据对象。失败时抛出异常，绝不返回 null。
    /// </summary>
    /// <param name="data">数据</param>
    /// <param name="type">对象类型</param>
    /// <returns>反序列化得到的对象，永不为 null</returns>
    /// <exception cref="ArgumentNullException">当 data 或 type 为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">当反序列化失败（脏数据 / 截断 / 类型不支持）时抛出，消息含目标类型全名，原始异常作为 InnerException。</exception>
    /// <remarks>
    /// 失败语义：底层反序列化异常会被包装为 <see cref="InvalidOperationException"/>（含目标类型全名）重新抛出；
    /// 反序列化结果为 null 时同样抛出 <see cref="InvalidOperationException"/>。
    /// 空字节数组是合法负载：protobuf-net 会自动创建并返回全默认字段实例，不视为失败。
    /// </remarks>
    public static object Deserialize(byte[] data, Type type)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(type);

        using (var memoryStream = new MemoryStream(data))
        {
            object result;
            try
            {
                result = Serializer.Deserialize(type, memoryStream);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Failed to deserialize the payload into type '{type.FullName}'.", exception);
            }

            if (result == null)
            {
                throw new InvalidOperationException($"Deserializing the payload into type '{type.FullName}' returned null; the payload is not decodable into this type.");
            }

            return result;
        }
    }

    /// <summary>
    /// 将字节数组反序列化并合并到已有实例（合并模式）
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="data">待反序列化的字节数组</param>
    /// <param name="instance">目标对象实例，反序列化数据将合并到该实例中。若实例为 null，将抛出 ArgumentNullException。</param>
    /// <returns>合并完成后的实例（与参数 instance 为同一对象）。若输入字节数组为空，则直接返回原实例。</returns>
    /// <exception cref="ArgumentNullException">当 instance 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 data 为 null 或长度为零时抛出。</exception>
    /// <remarks>
    /// 该方法使用 ProtoBuf 的合并模式，将字节流中的数据字段合并到已有对象中。
    /// 适用于需要增量更新或复用对象的场景，可避免频繁创建新实例带来的性能开销。
    /// 注意：合并过程中，现有字段值可能被覆盖，具体行为取决于 ProtoBuf 字段规则。
    /// </remarks>
    public static T Deserialize<T>(byte[] data, T instance)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(instance);

        if (data.Length == 0)
        {
            return instance;
        }

        using (var memoryStream = new MemoryStream(data))
        {
            return Serializer.Merge(memoryStream, instance);
        }
    }

    /// <summary>
    /// 反序列化数据对象。失败时抛出异常，绝不返回 null。
    /// </summary>
    /// <param name="data">数据内容</param>
    /// <typeparam name="T">目标类型</typeparam>
    /// <returns>反序列化得到的 T 实例，永不为 null</returns>
    /// <exception cref="ArgumentNullException">当 data 为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">当反序列化失败（脏数据 / 截断）或解码结果与 T 类型不匹配（如 ProtoInclude 兄弟子类型错配）时抛出；类型不匹配时消息注明期望类型与实际类型全名。</exception>
    /// <remarks>
    /// 失败语义：底层反序列化异常会被包装为 <see cref="InvalidOperationException"/>（含目标类型全名）重新抛出；
    /// 解码结果无法转换为 T 时抛出 <see cref="InvalidOperationException"/>（含期望与实际类型全名）。
    /// 空字节数组是合法负载：protobuf-net 会自动创建并返回全默认字段实例，不视为失败。
    /// 需要宽松语义（失败返回 false 而非抛异常）的调用方请使用 <see cref="TryDeserialize{T}"/>。
    /// </remarks>
    public static T Deserialize<T>(byte[] data) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(data);

        using (var memoryStream = new MemoryStream(data))
        {
            object result;
            try
            {
                result = Serializer.Deserialize(typeof(T), memoryStream);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Failed to deserialize the payload into type '{typeof(T).FullName}'.", exception);
            }

            if (result == null)
            {
                throw new InvalidOperationException($"Deserializing the payload into type '{typeof(T).FullName}' returned null; the payload is not decodable into this type.");
            }

            if (result is T typedResult)
            {
                return typedResult;
            }

            throw new InvalidOperationException($"Deserialization type mismatch: expected '{typeof(T).FullName}' but the payload decoded to '{result.GetType().FullName}'.");
        }
    }

    /// <summary>
    /// 尝试将字节数组反序列化为指定类型的对象（宽松语义）。失败返回 false 而非抛出异常。
    /// </summary>
    /// <param name="data">数据内容</param>
    /// <param name="value">成功时输出反序列化得到的实例；失败时输出 null</param>
    /// <typeparam name="T">目标类型</typeparam>
    /// <returns>成功返回 true；反序列化失败（脏数据 / 截断 / 类型不匹配）返回 false</returns>
    /// <exception cref="ArgumentNullException">当 data 为 null 时抛出（编程错误不属于反序列化失败）。</exception>
    /// <remarks>
    /// 供确需宽松失败语义的调用方使用：内部捕获 <see cref="Deserialize{T}(byte[])"/> 抛出的
    /// <see cref="InvalidOperationException"/> 并转为 false + null 返回。
    /// </remarks>
    public static bool TryDeserialize<T>(byte[] data, out T value) where T : class, new()
    {
        try
        {
            value = Deserialize<T>(data);
            return true;
        }
        catch (InvalidOperationException)
        {
            value = null;
            return false;
        }
    }
}
