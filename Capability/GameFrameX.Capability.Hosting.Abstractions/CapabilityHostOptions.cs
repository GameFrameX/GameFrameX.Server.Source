// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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

namespace GameFrameX.Capability.Hosting;

/// <summary>
/// 能力宿主装配选项（本进程的运行时授权作用域身份 + 调度壳驱动参数；
/// 原 OnlineRuntimeOptions 作用域半边的机制承接位，admin 监听参数归 AdminApiOptions）。
/// <para>
/// 维护约束：三元组是本进程的服务端权威作用域（请求体作用域字段不可覆盖，跨租户/App/服请求拒绝）。
/// </para>
/// </summary>
public sealed class CapabilityHostOptions
{
    /// <summary>
    /// 获取或设置授权租户标识（必须为正数）。
    /// </summary>
    public long TenantId { get; set; }

    /// <summary>
    /// 获取或设置授权应用标识（必须为正数）。
    /// </summary>
    public long AppId { get; set; }

    /// <summary>
    /// 获取或设置授权区服标识（必须为正数）。
    /// </summary>
    public long ServerId { get; set; }

    /// <summary>
    /// 获取或设置调度壳的驱动间隔毫秒数（默认 1000；小于 100 按 100 钳制）。
    /// </summary>
    public int SchedulerIntervalMilliseconds { get; set; } = 1000;

    /// <summary>
    /// 校验选项合法性（作用域三元组与调度间隔）。
    /// </summary>
    /// <returns>合法返回 <c>true</c>；否则 <c>false</c>（宿主不应在非法配置下启动）。</returns>
    public bool IsValid()
    {
        if (TenantId <= 0 || AppId <= 0 || ServerId <= 0)
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
