// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB 仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Hosting.AdminApi;

/// <summary>
/// admin API 监听选项（独立端口与前缀；原 OnlineRuntimeOptions 管理面半边的机制承接位）。
/// <para>
/// 维护约束：admin API 与游戏 API 面隔离，独立端口监听；与业务装配开关正交（管理面可独立启停）。
/// </para>
/// </summary>
public sealed class AdminApiOptions
{
    /// <summary>
    /// 获取或设置 admin API 监听端口（默认 28090）。
    /// </summary>
    public int AdminPort { get; set; } = 28090;

    /// <summary>
    /// 获取或设置 admin API 路由前缀（默认 <c>online/admin</c>；空值非法）。
    /// </summary>
    public string AdminApiPrefix { get; set; } = "online/admin";

    /// <summary>
    /// 获取或设置限流每窗口许可数（按客户端 IP 分区的固定窗口限流；默认 100）。
    /// <para>
    /// 维护约束：&lt;= 0 表示显式关闭限流（不注册限流器，行为与未引入限流前等价）；关闭限流的 admin 面失去登录爆破与请求洪泛闸门，仅应在受信任网络内使用。
    /// </para>
    /// </summary>
    public int RateLimitPermitLimit { get; set; } = 100;

    /// <summary>
    /// 获取或设置限流窗口秒数（默认 10）。
    /// <para>
    /// 维护约束：<see cref="RateLimitPermitLimit"/> &gt; 0 时本值必须 &gt; 0（<see cref="IsValid"/> 校验）。
    /// </para>
    /// </summary>
    public int RateLimitWindowSeconds { get; set; } = 10;

    /// <summary>
    /// 校验选项合法性（端口范围、路由前缀与限流参数组合）。
    /// </summary>
    /// <returns>合法返回 <c>true</c>；否则 <c>false</c>（不应在非法配置下启动 admin 面）。</returns>
    public bool IsValid()
    {
        if (AdminPort <= 0 || AdminPort > 65535)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(AdminApiPrefix))
        {
            return false;
        }

        if (RateLimitPermitLimit > 0 && RateLimitWindowSeconds <= 0)
        {
            return false;
        }

        return true;
    }
}
