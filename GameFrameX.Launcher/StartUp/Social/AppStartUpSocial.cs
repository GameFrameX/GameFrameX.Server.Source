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


namespace GameFrameX.Launcher.StartUp.Social;

/// <summary>
/// 社交服务器启动入口 —— 标准模板 <see cref="AppStartUpGameServerBase"/> 的参照实例。
/// </summary>
/// <remarks>
/// The Social server startup entry — the reference instance of the standard template
/// <see cref="AppStartUpGameServerBase"/>: the whole startup/stop/package sequence lives in
/// the template, and this class only declares the startup tag and the role-level default settings.
/// </remarks>
// 显式优先级修复与 Game 同为缺省 1000 的冲突——Social 在主服务 Game 之后启动（值越小优先级越高）
[StartUpTag(GameServerConst.Social.Name, 200)]
internal sealed class AppStartUpSocial : AppStartUpGameServerBase
{
    /// <summary>
    /// 创建 Social 服务器的缺省配置。
    /// </summary>
    /// <remarks>
    /// Creates the Social role-level default settings.
    /// 数据库连接地址优先从环境变量读取，避免在源码中硬编码凭证（消除 Sonar csharpsquid:S2068）。
    /// </remarks>
    /// <returns>Social 缺省配置 / The Social default settings</returns>
    protected override AppSetting CreateDefaultSetting()
    {
        return new AppSetting
        {
            ServerType = GameServerConst.Social.Name,
            ServerId = GameServerConst.Social.Id,
            InnerPort = 29400,
            HttpIsDevelopment = true,
            IsDebug = true,
            IsDebugSend = true,
            IsDebugReceive = true,
            IsDebugReceiveHeartBeat = false,
            IsDebugSendHeartBeat = false,
            // 数据库连接地址优先从环境变量读取，避免在源码中硬编码凭证（消除 Sonar csharpsquid:S2068）
            DataBaseUrl = Environment.GetEnvironmentVariable("GAMEFRAMEX_SOCIAL_DB_URL") ?? "mongodb://127.0.0.1:27017/?authSource=admin",
            DataBaseName = "gameframex",
        };
    }
}
