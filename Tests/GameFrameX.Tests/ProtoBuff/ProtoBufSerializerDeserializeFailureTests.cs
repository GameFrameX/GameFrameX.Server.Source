// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using GameFrameX.ProtoBuf.Net;
using ProtoBuf;
using Xunit;

namespace GameFrameX.Tests.ProtoBuff;

/// <summary>
/// ProtoBufSerializerHelper.Deserialize 失败可见性回归测试（C356 / GFX-1064）。
/// 失败路径必须抛带类型全名的 InvalidOperationException，不得静默返回 null；
/// TryDeserialize 提供宽松语义；空字节数组保持合法负载语义（auto-create 全默认实例）。
/// </summary>
public class ProtoBufSerializerDeserializeFailureTests
{
    [ProtoContract]
    public class RoundTripPayload
    {
        [ProtoMember(1)]
        public int Identifier { get; set; }

        [ProtoMember(2)]
        public string DisplayName { get; set; }
    }

    [ProtoContract]
    [ProtoInclude(10, typeof(HierarchySubTypeOne))]
    [ProtoInclude(20, typeof(HierarchySubTypeTwo))]
    public class HierarchyBase
    {
    }

    [ProtoContract]
    public class HierarchySubTypeOne : HierarchyBase
    {
        [ProtoMember(1)]
        public int NumericValue { get; set; }
    }

    [ProtoContract]
    public class HierarchySubTypeTwo : HierarchyBase
    {
        [ProtoMember(1)]
        public string TextValue { get; set; }
    }

    [Fact]
    public void Deserialize_Generic_RoundTrip_ShouldSucceed()
    {
        var payload = new RoundTripPayload { Identifier = 42, DisplayName = "answer" };
        var data = ProtoBufSerializerHelper.Serialize(payload);

        var result = ProtoBufSerializerHelper.Deserialize<RoundTripPayload>(data);

        Assert.NotNull(result);
        Assert.Equal(42, result.Identifier);
        Assert.Equal("answer", result.DisplayName);
    }

    [Fact]
    public void Deserialize_NonGeneric_RoundTrip_ShouldSucceed()
    {
        var payload = new RoundTripPayload { Identifier = 7, DisplayName = "seven" };
        var data = ProtoBufSerializerHelper.Serialize(payload);

        var result = Assert.IsType<RoundTripPayload>(ProtoBufSerializerHelper.Deserialize(data, typeof(RoundTripPayload)));

        Assert.Equal(7, result.Identifier);
        Assert.Equal("seven", result.DisplayName);
    }

    [Fact]
    public void Deserialize_Generic_CorruptData_ShouldThrowInvalidOperationExceptionWithTypeAndInnerException()
    {
        // 0xFF*4 是非法 wire-type 前缀 + 截断的 varint，必然反序列化失败
        var corruptData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

        var exception = Assert.Throws<InvalidOperationException>(() => ProtoBufSerializerHelper.Deserialize<RoundTripPayload>(corruptData));

        Assert.NotNull(exception.InnerException);
        Assert.Contains(typeof(RoundTripPayload).FullName, exception.Message);
    }

    [Fact]
    public void Deserialize_NonGeneric_CorruptData_ShouldThrowInvalidOperationExceptionWithTypeAndInnerException()
    {
        var corruptData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x07, 0x01 };

        var exception = Assert.Throws<InvalidOperationException>(() => ProtoBufSerializerHelper.Deserialize(corruptData, typeof(RoundTripPayload)));

        Assert.NotNull(exception.InnerException);
        Assert.Contains(typeof(RoundTripPayload).FullName, exception.Message);
    }

    [Fact]
    public void Deserialize_Generic_SiblingSubTypeMismatch_ShouldThrowWithExpectedAndActualTypeNames()
    {
        // protobuf-net 经基类 ProtoInclude 表会从 SubTypeOne 载荷解出 SubTypeOne 实例；
        // 旧代码 as SubTypeTwo 在此静默返回 null——本用例是该缺陷的判别性锚点（A/B：旧代码必败）。
        var subTypeOneData = ProtoBufSerializerHelper.Serialize(new HierarchySubTypeOne { NumericValue = 5 });

        var exception = Assert.Throws<InvalidOperationException>(() => ProtoBufSerializerHelper.Deserialize<HierarchySubTypeTwo>(subTypeOneData));

        Assert.Contains(typeof(HierarchySubTypeTwo).FullName, exception.Message);
        Assert.Contains(typeof(HierarchySubTypeOne).FullName, exception.Message);
    }

    [Fact]
    public void Deserialize_EmptyArray_ShouldReturnDefaultInstance()
    {
        // 空字节数组是合法负载（全默认字段消息的序列化产物就是空数组），必须返回全默认实例而非抛异常
        var emptyData = ProtoBufSerializerHelper.Serialize(new RoundTripPayload());

        Assert.Empty(emptyData);

        var result = ProtoBufSerializerHelper.Deserialize<RoundTripPayload>(emptyData);
        Assert.NotNull(result);
        Assert.Equal(0, result.Identifier);
        Assert.Null(result.DisplayName);

        var nonGenericResult = Assert.IsType<RoundTripPayload>(ProtoBufSerializerHelper.Deserialize(emptyData, typeof(RoundTripPayload)));
        Assert.Equal(0, nonGenericResult.Identifier);
    }

    [Fact]
    public void Deserialize_NullData_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ProtoBufSerializerHelper.Deserialize<RoundTripPayload>(null));
        Assert.Throws<ArgumentNullException>(() => ProtoBufSerializerHelper.Deserialize(null, typeof(RoundTripPayload)));
        Assert.Throws<ArgumentNullException>(() => ProtoBufSerializerHelper.Deserialize(null, null));
    }

    [Fact]
    public void TryDeserialize_ValidData_ShouldReturnTrueAndDecodedInstance()
    {
        var payload = new RoundTripPayload { Identifier = 99, DisplayName = "lenient" };
        var data = ProtoBufSerializerHelper.Serialize(payload);

        var succeeded = ProtoBufSerializerHelper.TryDeserialize(data, out RoundTripPayload value);

        Assert.True(succeeded);
        Assert.NotNull(value);
        Assert.Equal(99, value.Identifier);
        Assert.Equal("lenient", value.DisplayName);
    }

    [Fact]
    public void TryDeserialize_CorruptData_ShouldReturnFalseAndNull()
    {
        var succeeded = ProtoBufSerializerHelper.TryDeserialize(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, out RoundTripPayload value);

        Assert.False(succeeded);
        Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_TypeMismatch_ShouldReturnFalseAndNull()
    {
        var subTypeOneData = ProtoBufSerializerHelper.Serialize(new HierarchySubTypeOne { NumericValue = 5 });

        var succeeded = ProtoBufSerializerHelper.TryDeserialize(subTypeOneData, out HierarchySubTypeTwo value);

        Assert.False(succeeded);
        Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_NullData_ShouldThrowArgumentNullException()
    {
        // null 输入是编程错误，宽松入口同样不得吞掉
        Assert.Throws<ArgumentNullException>(() => ProtoBufSerializerHelper.TryDeserialize<RoundTripPayload>(null, out _));
    }
}
