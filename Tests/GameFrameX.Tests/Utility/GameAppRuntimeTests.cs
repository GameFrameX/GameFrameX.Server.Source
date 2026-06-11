// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//  
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//  
//  本项目采用 Apache License 2.0 单协议分发，
//  This project is licensed solely under the Apache License 2.0,
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

using GameFrameX.Utility.Runtime;

namespace GameFrameX.Tests.Utility;

/// <summary>
/// GameAppRuntime 相关测试集合。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class GameAppRuntimeCollection
{
    public const string Name = "GameAppRuntime";
}

/// <summary>
/// GameAppRuntime 运行期状态测试。
/// </summary>
[Collection(GameAppRuntimeCollection.Name)]
public class GameAppRuntimeTests
{
    /// <summary>
    /// 测试应用程序启动、停止和退出原因传递。
    /// </summary>
    [Fact]
    public async Task Lifecycle_ShouldUpdateStateAndCompleteExitToken()
    {
        // Arrange
        var launchTime = DateTime.UtcNow;
        const string exitReason = "maintenance";

        // Act
        GameAppRuntime.MarkStarted(launchTime);
        var exitToken = GameAppRuntime.AppExitToken;
        GameAppRuntime.MarkStopping();

        // Assert
        Assert.Equal(launchTime, GameAppRuntime.LaunchTime);
        Assert.False(GameAppRuntime.IsRunning);
        Assert.False(exitToken.IsCompleted);

        // Act
        var completed = GameAppRuntime.MarkStopped(exitReason);
        var duplicateCompleted = GameAppRuntime.MarkStopped("duplicate");

        // Assert
        Assert.True(completed);
        Assert.False(duplicateCompleted);
        Assert.Equal(exitReason, await exitToken);
        Assert.Equal(exitReason, GameAppRuntime.ExitReason);
    }
}
