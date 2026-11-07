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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;

namespace GameFrameX.Capability.Online.Operations.HotfixRollback;

/// <summary>
/// 登记期制品获取参数（定值缺省：单次尝试超时 30 秒、失败重试 2 次、下载大小上限 64 MiB）。
/// <para>
/// 重试范围 = 网络异常 / HTTP 5xx / 408（瞬时失败面）；其余 4xx 与校验不符立即失败——
/// 永久性错误重试只会放大故障窗口。
/// </para>
/// </summary>
public sealed class OnlineHotfixArtifactFetchOptions
{
    /// <summary>
    /// 获取或设置单次下载尝试的超时（覆盖响应头与响应体全程；缺省 30 秒）。
    /// </summary>
    public TimeSpan AttemptTimeout
    {
        get;
        set;
    } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 获取或设置失败重试次数（总尝试数 = 1 + 本值；缺省 2，即最多尝试 3 次；仅网络异常 / 5xx / 408 重试）。
    /// </summary>
    public int MaxRetryCount
    {
        get;
        set;
    } = 2;

    /// <summary>
    /// 获取或设置单制品下载大小上限（字节；缺省 64 MiB。Content-Length 预检 + 流式计数双闸，超限即断不落盘）。
    /// </summary>
    public long MaxArtifactBytes
    {
        get;
        set;
    } = 64L * 1024 * 1024;
}
