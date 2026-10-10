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
//   or to infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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


namespace GameFrameX.Launcher.StartUp.Scene;

/// <summary>
/// 场景服务器启动入口 —— 「玩法战斗」域标准 Role（骨架 <see cref="AppStartUpStandardServerBase"/>）。
/// </summary>
/// <remarks>
/// The Scene server startup entry — a standard role of the Gameplay domain on the
/// <see cref="AppStartUpStandardServerBase"/> skeleton: the whole startup/stop/package sequence lives
/// in the skeleton, and this class only declares the startup tag (name + domain-segment priority)
/// and the role-level default settings.
/// 启动优先级按域段约定分配（接入 3xx → 社交 4xx → 玩法 5xx → 经济 6xx → 治理 7xx，域内 ×10 递增），
/// 保证全表唯一且多 Role 启动屏障顺序稳定。
/// </remarks>
[StartUpTag(GameServerConst.Scene.Name, 520)]
internal sealed class AppStartUpScene : AppStartUpStandardServerBase
{
    /// <summary>
    /// 创建 Scene 服务器的缺省配置。
    /// </summary>
    /// <remarks>
    /// Creates the Scene role-level default settings.
    /// 端口公式：InnerPort = 20000 + GameServerConst.Scene.Id（7020）= 27020——Id 唯一 ⇒ 端口唯一，
    /// 区间 [25000, 28000] 与既有 Game/Social/Http 端口零碰撞（C188 固化的端口分配约定）。
    /// 数据库连接串优先从环境变量 GAMEFRAMEX_SCENE_DB_URL 读取（对齐 C75 / Sonar S2068 的
    /// GAMEFRAMEX_&lt;SERVER&gt;_DB_URL 命名约定），未设置时回落不含凭证的本地占位地址；
    /// 其余缺省对齐 Social 参照形态。
    /// </remarks>
    /// <returns>Scene 缺省配置 / The Scene default settings</returns>
    protected override AppSetting CreateDefaultSetting()
    {
        return new AppSetting
        {
            ServerType = GameServerConst.Scene.Name,
            ServerId = GameServerConst.Scene.Id,
            InnerPort = 20000 + GameServerConst.Scene.Id,
            HttpIsDevelopment = true,
            IsDebug = true,
            IsDebugSend = true,
            IsDebugReceive = true,
            IsDebugReceiveHeartBeat = false,
            IsDebugSendHeartBeat = false,
            // 数据库连接地址优先从环境变量读取，避免在源码中硬编码凭证（消除 Sonar csharpsquid:S2068）
            DataBaseUrl = Environment.GetEnvironmentVariable("GAMEFRAMEX_SCENE_DB_URL") ?? "mongodb://127.0.0.1:27017/?authSource=admin",
            DataBaseName = "gameframex",
        };
    }
}
