// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//
//  使用本项目须严格遵守相应法律法规与开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the source code root directory for the full license text.
//
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legal rights and interests of others, as prohibited by laws and regulations.
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any disputes or liabilities arising from secondary development based on this project
//  本组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  CNB  仓库：https://cnb.cool/GameFrameX
//  CNB Repository:  https://cnb.cool/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Collections.Generic;
using GameFrameX.Foundation.Logger;
using GameFrameX.StartUp;
using GameFrameX.StartUp.Options;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// GameApp Grafana Loki 接线（Foundation 5.0.0 sink 组件化迁移）的单元测试。
/// </summary>
/// <remarks>
/// Unit tests for the GameApp Grafana Loki wiring after the Foundation 5.0.0
/// sink componentization: LogIsGrafanaLoki=false registers no sink
/// configurator, true registers exactly one, and labels are collected from
/// GrafanaLokiLabelTagAttribute-attributed properties.
/// </remarks>
public class GameAppGrafanaLokiWiringTests : IDisposable
{
    private readonly int _sinkConfiguratorCount = LogOptions.Default.SinkConfigurators.Count;
    private readonly bool _isConsole = LogOptions.Default.IsConsole;
    private readonly bool _isWriteToFile = LogOptions.Default.IsWriteToFile;
    private readonly int? _retainedFileCountLimit = LogOptions.Default.RetainedFileCountLimit;
    private readonly bool _isFileSizeLimit = LogOptions.Default.IsFileSizeLimit;
    private readonly int _fileSizeLimitBytes = LogOptions.Default.FileSizeLimitBytes;
    private readonly string _logTagName = LogOptions.Default.LogTagName;

    /// <summary>
    /// 释放：恢复 LogOptions.Default 静态状态（含移除测试注册的 sink 配置项），避免泄漏到其它测试。
    /// </summary>
    public void Dispose()
    {
        while (LogOptions.Default.SinkConfigurators.Count > _sinkConfiguratorCount)
        {
            LogOptions.Default.SinkConfigurators.RemoveAt(LogOptions.Default.SinkConfigurators.Count - 1);
        }

        LogOptions.Default.IsConsole = _isConsole;
        LogOptions.Default.IsWriteToFile = _isWriteToFile;
        LogOptions.Default.RetainedFileCountLimit = _retainedFileCountLimit;
        LogOptions.Default.IsFileSizeLimit = _isFileSizeLimit;
        LogOptions.Default.FileSizeLimitBytes = _fileSizeLimitBytes;
        LogOptions.Default.LogTagName = _logTagName;
    }

    /// <summary>
    /// LogIsGrafanaLoki=false 时不注册任何 Loki sink 配置项（等价 4.1.1 IsGrafanaLoki=false 默认关闭）。
    /// </summary>
    [Fact]
    public void ConfigureLogOptions_LokiDisabled_RegistersNoSinkConfigurator()
    {
        var options = new StartupOptions { LogIsGrafanaLoki = false };

        GameApp.ConfigureLogOptions(options, new Dictionary<string, string>());

        Assert.Equal(_sinkConfiguratorCount, LogOptions.Default.SinkConfigurators.Count);
    }

    /// <summary>
    /// LogIsGrafanaLoki=true 时经 UseGrafanaLoki 恰好注册一个 Loki sink 配置项。
    /// </summary>
    [Fact]
    public void ConfigureLogOptions_LokiEnabled_RegistersExactlyOneSinkConfigurator()
    {
        var options = new StartupOptions
        {
            LogIsGrafanaLoki = true,
            LogGrafanaLokiUrl = "http://loki.example:3100",
            LogGrafanaLokiUserName = "loki-user",
            LogGrafanaLokiPassword = "loki-pass",
        };

        GameApp.ConfigureLogOptions(options, new Dictionary<string, string> { ["env"] = "test" });

        Assert.Equal(_sinkConfiguratorCount + 1, LogOptions.Default.SinkConfigurators.Count);
    }

    /// <summary>
    /// GrafanaLokiLabelTag 标注属性的值被收集为标签（含数值属性的字符串化，与 4.1.1 行为一致）。
    /// </summary>
    [Fact]
    public void CollectGrafanaLokiLabels_CollectsAttributedPropertyValues()
    {
        var options = new StartupOptions
        {
            ServerType = "Game",
            ServerId = 1001,
            TagName = "Release",
        };

        var labels = GameApp.CollectGrafanaLokiLabels(options);

        Assert.Equal("Game", Assert.Contains("ServerType", labels));
        Assert.Equal("1001", Assert.Contains("ServerId", labels));
        Assert.Equal("Release", Assert.Contains("TagName", labels));
    }

    /// <summary>
    /// 未赋值的字符串标注属性不产生标签；数值属性按默认值字符串化（ServerId=0 → "0"，4.1.1 同款语义）。
    /// </summary>
    [Fact]
    public void CollectGrafanaLokiLabels_NullOrEmptyStringValues_AreSkipped()
    {
        var options = new StartupOptions
        {
            ServerType = string.Empty,
            TagName = null,
            Note = string.Empty,
        };

        var labels = GameApp.CollectGrafanaLokiLabels(options);

        Assert.False(labels.ContainsKey("ServerType"));
        Assert.False(labels.ContainsKey("TagName"));
        Assert.False(labels.ContainsKey("Note"));
        Assert.Equal("0", Assert.Contains("ServerId", labels));
    }

    /// <summary>
    /// 启动器选项为 null 时返回空字典。
    /// </summary>
    [Fact]
    public void CollectGrafanaLokiLabels_NullOptions_ReturnsEmptyDictionary()
    {
        var labels = GameApp.CollectGrafanaLokiLabels(null);

        Assert.Empty(labels);
    }
}
