// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//   This project is dual-licensed under the MIT License and Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等违法行为！
//   or infringe upon the legitimate rights and interest of others, as prohibited by laws and regulations!
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

using GameFrameX.Core.Idempotency;

namespace GameFrameX.Core.Abstractions.Attribute;

/// <summary>
/// 标记 Player 处理器启用业务级幂等检查。
/// 仅对 PlayerComponentHandler / PlayerRpcComponentHandler 生效，
/// 全局处理器标记此属性无效。请求消息需实现 IIdempotentRequest 接口。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IdempotentAttribute : System.Attribute
{
    /// <summary>
    /// 幂等记录生存时间（秒），0 表示使用全局默认值
    /// </summary>
    public int TtlSeconds { get; set; }

    /// <summary>
    /// 缓存策略，控制是否缓存异常结果
    /// </summary>
    public IdempotentCachePolicy CachePolicy { get; set; } = IdempotentCachePolicy.AllOutcomes;

    public IdempotentAttribute() { }

    public IdempotentAttribute(int ttlSeconds)
    {
        TtlSeconds = ttlSeconds;
    }
}
