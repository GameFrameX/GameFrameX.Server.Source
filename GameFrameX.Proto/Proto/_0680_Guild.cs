// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System;
using ProtoBuf;
using System.Collections.Generic;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Messages;

namespace GameFrameX.Proto.Proto
{
	/// <summary>
	/// 公会服务器（Guild Role，公会管理，域号 680）业务错误码（6 位 = 模块ID 680 + 3 位编号）。
	/// </summary>
	[System.ComponentModel.Description("公会服务器（Guild Role，公会管理，域号 680）业务错误码（6 位 = 模块ID 680 + 3 位编号）。")]
	public enum GuildErrorCode
	{
		/// <summary>
		/// 会话未绑定玩家（未登录）
		/// </summary>
		[System.ComponentModel.Description("会话未绑定玩家（未登录）")]
		NotLoggedIn = 680001,

		/// <summary>
		/// 公会名称非法（长度区间 2-16）
		/// </summary>
		[System.ComponentModel.Description("公会名称非法（长度区间 2-16）")]
		NameInvalid = 680002,

		/// <summary>
		/// 公会名称已被占用（全局唯一）
		/// </summary>
		[System.ComponentModel.Description("公会名称已被占用（全局唯一）")]
		GuildNameExists = 680003,

		/// <summary>
		/// 玩家已加入其他公会（一人一公会）
		/// </summary>
		[System.ComponentModel.Description("玩家已加入其他公会（一人一公会）")]
		AlreadyInGuild = 680004,

		/// <summary>
		/// 公会不存在
		/// </summary>
		[System.ComponentModel.Description("公会不存在")]
		GuildNotFound = 680005,

		/// <summary>
		/// 仅会长可执行该操作（或会长不可离会，须解散）
		/// </summary>
		[System.ComponentModel.Description("仅会长可执行该操作（或会长不可离会，须解散）")]
		NotLeader = 680006,

		/// <summary>
		/// 玩家不是该公会成员
		/// </summary>
		[System.ComponentModel.Description("玩家不是该公会成员")]
		NotMember = 680007,

		/// <summary>
		/// 目标玩家不在申请列表中
		/// </summary>
		[System.ComponentModel.Description("目标玩家不在申请列表中")]
		NotApplicant = 680008,
	}

	/// <summary>
	/// 请求创建公会（创建者为会长）
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("请求创建公会（创建者为会长）")]
	[MessageTypeHandler(((680) << 16) + 10)]
	public sealed class ReqGuildCreate : MessageObject, IRequestMessage
	{
		/// <summary>
		/// 公会名称（2-16 字符，全局唯一）
		/// </summary>
		[ProtoMember(1)]
		[System.ComponentModel.Description("公会名称（2-16 字符，全局唯一）")]
		public string Name { get; set; }

		public override void Clear()
		{
			Name = default;
		}
	}

	/// <summary>
	/// 返回创建公会结果
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("返回创建公会结果")]
	[MessageTypeHandler(((680) << 16) + 11)]
	public sealed class RespGuildCreate : MessageObject, IResponseMessage
	{
		/// <summary>
		/// 分配的公会ID
		/// </summary>
		[ProtoMember(1)]
		[System.ComponentModel.Description("分配的公会ID")]
		public long GuildId { get; set; }

		/// <summary>
		/// 返回的错误码
		/// </summary>
		[ProtoMember(2047)]
		[System.ComponentModel.Description("返回的错误码")]
		public int ErrorCode { get; set; }

		public override void Clear()
		{
			GuildId = default;
			ErrorCode = default;
		}
	}

	/// <summary>
	/// 请求申请加入指定公会
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("请求申请加入指定公会")]
	[MessageTypeHandler(((680) << 16) + 12)]
	public sealed class ReqGuildApply : MessageObject, IRequestMessage
	{
		/// <summary>
		/// 公会ID
		/// </summary>
		[ProtoMember(1)]
		[System.ComponentModel.Description("公会ID")]
		public long GuildId { get; set; }

		public override void Clear()
		{
			GuildId = default;
		}
	}

	/// <summary>
	/// 返回申请加入公会结果
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("返回申请加入公会结果")]
	[MessageTypeHandler(((680) << 16) + 13)]
	public sealed class RespGuildApply : MessageObject, IResponseMessage
	{
		/// <summary>
		/// 返回的错误码
		/// </summary>
		[ProtoMember(2047)]
		[System.ComponentModel.Description("返回的错误码")]
		public int ErrorCode { get; set; }

		public override void Clear()
		{
			ErrorCode = default;
		}
	}

	/// <summary>
	/// 请求会长审批通过指定申请人的入会申请
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("请求会长审批通过指定申请人的入会申请")]
	[MessageTypeHandler(((680) << 16) + 14)]
	public sealed class ReqGuildApprove : MessageObject, IRequestMessage
	{
		/// <summary>
		/// 申请人玩家ID
		/// </summary>
		[ProtoMember(1)]
		[System.ComponentModel.Description("申请人玩家ID")]
		public long ApplicantPlayerId { get; set; }

		public override void Clear()
		{
			ApplicantPlayerId = default;
		}
	}

	/// <summary>
	/// 返回审批结果（含审批后公会最新等级 / 经验 / 成员数）
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("返回审批结果（含审批后公会最新等级 / 经验 / 成员数）")]
	[MessageTypeHandler(((680) << 16) + 15)]
	public sealed class RespGuildApprove : MessageObject, IResponseMessage
	{
		/// <summary>
		/// 公会等级
		/// </summary>
		[ProtoMember(1)]
		[System.ComponentModel.Description("公会等级")]
		public int Level { get; set; }

		/// <summary>
		/// 公会经验
		/// </summary>
		[ProtoMember(2)]
		[System.ComponentModel.Description("公会经验")]
		public long Exp { get; set; }

		/// <summary>
		/// 公会成员数
		/// </summary>
		[ProtoMember(3)]
		[System.ComponentModel.Description("公会成员数")]
		public int MemberCount { get; set; }

		/// <summary>
		/// 返回的错误码
		/// </summary>
		[ProtoMember(2047)]
		[System.ComponentModel.Description("返回的错误码")]
		public int ErrorCode { get; set; }

		public override void Clear()
		{
			Level = default;
			Exp = default;
			MemberCount = default;
			ErrorCode = default;
		}
	}

	/// <summary>
	/// 请求离开当前公会（会长不可离会，须解散）
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("请求离开当前公会（会长不可离会，须解散）")]
	[MessageTypeHandler(((680) << 16) + 16)]
	public sealed class ReqGuildLeave : MessageObject, IRequestMessage
	{

		public override void Clear()
		{
		}
	}

	/// <summary>
	/// 返回离开公会结果
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("返回离开公会结果")]
	[MessageTypeHandler(((680) << 16) + 17)]
	public sealed class RespGuildLeave : MessageObject, IResponseMessage
	{
		/// <summary>
		/// 返回的错误码
		/// </summary>
		[ProtoMember(2047)]
		[System.ComponentModel.Description("返回的错误码")]
		public int ErrorCode { get; set; }

		public override void Clear()
		{
			ErrorCode = default;
		}
	}

	/// <summary>
	/// 请求会长移除指定成员（不含会长本人）
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("请求会长移除指定成员（不含会长本人）")]
	[MessageTypeHandler(((680) << 16) + 18)]
	public sealed class ReqGuildKick : MessageObject, IRequestMessage
	{
		/// <summary>
		/// 要移除的成员玩家ID
		/// </summary>
		[ProtoMember(1)]
		[System.ComponentModel.Description("要移除的成员玩家ID")]
		public long MemberPlayerId { get; set; }

		public override void Clear()
		{
			MemberPlayerId = default;
		}
	}

	/// <summary>
	/// 返回移除成员结果
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("返回移除成员结果")]
	[MessageTypeHandler(((680) << 16) + 19)]
	public sealed class RespGuildKick : MessageObject, IResponseMessage
	{
		/// <summary>
		/// 返回的错误码
		/// </summary>
		[ProtoMember(2047)]
		[System.ComponentModel.Description("返回的错误码")]
		public int ErrorCode { get; set; }

		public override void Clear()
		{
			ErrorCode = default;
		}
	}

	/// <summary>
	/// 请求会长解散当前公会
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("请求会长解散当前公会")]
	[MessageTypeHandler(((680) << 16) + 20)]
	public sealed class ReqGuildDisband : MessageObject, IRequestMessage
	{

		public override void Clear()
		{
		}
	}

	/// <summary>
	/// 返回解散公会结果
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("返回解散公会结果")]
	[MessageTypeHandler(((680) << 16) + 21)]
	public sealed class RespGuildDisband : MessageObject, IResponseMessage
	{
		/// <summary>
		/// 返回的错误码
		/// </summary>
		[ProtoMember(2047)]
		[System.ComponentModel.Description("返回的错误码")]
		public int ErrorCode { get; set; }

		public override void Clear()
		{
			ErrorCode = default;
		}
	}

	/// <summary>
	/// 请求查询指定公会档案
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("请求查询指定公会档案")]
	[MessageTypeHandler(((680) << 16) + 22)]
	public sealed class ReqGuildQuery : MessageObject, IRequestMessage
	{
		/// <summary>
		/// 公会ID
		/// </summary>
		[ProtoMember(1)]
		[System.ComponentModel.Description("公会ID")]
		public long GuildId { get; set; }

		public override void Clear()
		{
			GuildId = default;
		}
	}

	/// <summary>
	/// 返回公会档案（含待审批申请列表）
	/// </summary>
	[ProtoContract]
	[System.ComponentModel.Description("返回公会档案（含待审批申请列表）")]
	[MessageTypeHandler(((680) << 16) + 23)]
	public sealed class RespGuildQuery : MessageObject, IResponseMessage
	{
		/// <summary>
		/// 公会名称
		/// </summary>
		[ProtoMember(1)]
		[System.ComponentModel.Description("公会名称")]
		public string Name { get; set; }

		/// <summary>
		/// 会长玩家ID
		/// </summary>
		[ProtoMember(2)]
		[System.ComponentModel.Description("会长玩家ID")]
		public long LeaderId { get; set; }

		/// <summary>
		/// 公会等级
		/// </summary>
		[ProtoMember(3)]
		[System.ComponentModel.Description("公会等级")]
		public int Level { get; set; }

		/// <summary>
		/// 公会经验
		/// </summary>
		[ProtoMember(4)]
		[System.ComponentModel.Description("公会经验")]
		public long Exp { get; set; }

		/// <summary>
		/// 公会成员数
		/// </summary>
		[ProtoMember(5)]
		[System.ComponentModel.Description("公会成员数")]
		public int MemberCount { get; set; }

		/// <summary>
		/// 待审批申请列表（玩家ID）
		/// </summary>
		[ProtoMember(6)]
		[System.ComponentModel.Description("待审批申请列表（玩家ID）")]
		public List<long> Applications { get; set; } = new List<long>();

		/// <summary>
		/// 返回的错误码
		/// </summary>
		[ProtoMember(2047)]
		[System.ComponentModel.Description("返回的错误码")]
		public int ErrorCode { get; set; }

		public override void Clear()
		{
			Name = default;
			LeaderId = default;
			Level = default;
			Exp = default;
			MemberCount = default;
			Applications.Clear();
			ErrorCode = default;
		}
	}

}
