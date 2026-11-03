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

using GameFrameX.StartUp;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// 仅使用生产 WebSocket 装配与证书停机路径的测试启动器。
/// </summary>
/// <remarks>Test startup using the production WebSocket configuration and certificate shutdown paths.</remarks>
internal sealed class WebSocketCertificateTestStartUp : AppStartUpBase
{
    /// <summary>设置测试配置，跳过进程初始化。</summary>
    /// <remarks>Sets test configuration without process initialization.</remarks>
    /// <param name="setting">测试配置 / Test setting</param>
    internal WebSocketCertificateTestStartUp(AppSetting setting)
    {
        Setting = setting;
    }

    /// <summary>测试只单独装配 WebSocket，启动入口保持空实现。</summary>
    /// <remarks>Tests configure WebSocket separately, so the startup entry point does nothing.</remarks>
    /// <returns>已完成的任务 / Completed task</returns>
    public override Task StartAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>在测试宿主停止后释放生产启动器持有的证书。</summary>
    /// <remarks>Releases the startup-owned certificate after the test host stops.</remarks>
    /// <returns>停机任务 / Shutdown task</returns>
    internal Task ShutdownAsync()
    {
        return StopServerAsync();
    }
}
