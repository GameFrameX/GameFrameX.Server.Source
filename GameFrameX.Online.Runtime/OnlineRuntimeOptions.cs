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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

namespace GameFrameX.Online.Runtime;

/// <summary>
/// Online Runtime 装配选项（change C122：宿主启动时由 <c>AppSetting</c> 映射，承载运行时授权作用域与 admin API 监听参数）。
/// <para>
/// 维护约束：<see cref="TenantId"/>/<see cref="AppId"/>/<see cref="ServerId"/> 是本进程的运行时授权作用域
/// （vault:C2：以服务端配置为准，请求体作用域字段不可覆盖；跨租户/App/服请求按 3002/3003/3004 拒绝）；
/// admin API 与游戏 API 面隔离，独立端口监听。
/// </para>
/// </summary>
public sealed class OnlineRuntimeOptions
{
    /// <summary>
    /// 获取或设置授权租户标识（必须为正数，否则 admin 请求一律按作用域缺失拒绝）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置授权应用标识（必须为正数）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置授权区服标识（取宿主 <c>Setting.ServerId</c>，必须为正数）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 admin API 监听端口（默认 28090）。
    /// </summary>
    public int AdminPort
    {
        get;
        set;
    } = 28090;

    /// <summary>
    /// 获取或设置 admin API 路由前缀（默认 <c>online/admin</c>，与 Admin 侧 <c>OnlineAdminApiPrefix</c> 一致；空值非法）。
    /// </summary>
    public string AdminApiPrefix
    {
        get;
        set;
    } = "online/admin";

    /// <summary>
    /// 获取或设置后台调度器的驱动间隔毫秒数（默认 1000；对局 Tick / 撮合 / 过期清扫共用）。
    /// </summary>
    public int SchedulerIntervalMilliseconds
    {
        get;
        set;
    } = 1000;

    /// <summary>
    /// 校验选项合法性（作用域三元组与路由前缀）。
    /// </summary>
    /// <returns>合法返回 <c>true</c>；否则 <c>false</c>（宿主不应在非法配置下启动 admin 面）。</returns>
    public bool IsValid()
    {
        if (TenantId <= 0 || AppId <= 0 || ServerId <= 0)
        {
            return false;
        }

        if (AdminPort <= 0 || AdminPort > 65535)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(AdminApiPrefix))
        {
            return false;
        }

        if (SchedulerIntervalMilliseconds <= 0)
        {
            return false;
        }

        return true;
    }
}
