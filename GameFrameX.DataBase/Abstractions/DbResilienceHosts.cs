// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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


namespace GameFrameX.DataBase.Abstractions;

/// <summary>
/// 内置弹性宿主集合（C253）。
/// </summary>
/// <remarks>
/// Built-in <see cref="IDbResilienceHost"/> implementations shared by providers.
/// </remarks>
public static class DbResilienceHosts
{
    /// <summary>
    /// 恒 Healthy 宿主：无可用性状态机的 provider（SQLite 嵌入式，C222 边界④）使用——
    /// 降级读恒不触发、写门控恒放行、成败记录为 no-op；重试环与三指标照常生效。
    /// </summary>
    /// <remarks>
    /// A constant-healthy host for providers without an availability state machine (the SQLite
    /// embedded engine, C222 boundary ④): degraded reads never trigger, the write gate always
    /// allows, and success/failure bookkeeping is a no-op, while retry loops and operation
    /// metrics remain fully active.
    /// </remarks>
    public static IDbResilienceHost AlwaysHealthy { get; } = new ConstantHealthyResilienceHost();

    /// <summary>
    /// 恒 Healthy 弹性宿主实现（无状态，线程安全）。
    /// </summary>
    private sealed class ConstantHealthyResilienceHost : IDbResilienceHost
    {
        public bool TryReturnDegradedReadFallback<T>(string operationName, Func<T> fallbackValueFactory, out T fallbackValue)
        {
            fallbackValue = default;
            return false;
        }

        public bool IsCoreWriteAllowed(string operationName)
        {
            return true;
        }

        public void RecordOperationSuccess(string operationName, string operationType)
        {
        }

        public void RecordRetryableFailure(string operationName, string operationType, Exception exception)
        {
        }
    }
}
