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

namespace GameFrameX.Hotfix.WrapperGenerator.Agent;

/// <summary>
/// 包装器生成器的代理信息，包含生成的分部类所需的所有元数据。
/// </summary>
/// <remarks>
/// Agent information for wrapper generator, containing all metadata needed to generate a partial class.
/// </remarks>
public class AgentInfo
{
    /// <summary>
    /// 获取或设置命名空间。
    /// </summary>
    /// <remarks>
    /// Gets or sets the namespace.
    /// </remarks>
    /// <value>命名空间 / Namespace</value>
    public string Space { get; set; }

    /// <summary>
    /// 获取或设置分部类修饰符。
    /// </summary>
    /// <remarks>
    /// Gets or sets the partial class modifier.
    /// </remarks>
    /// <value>分部类修饰符 / Partial class modifier</value>
    public string Partial { get; set; } = "";

    /// <summary>
    /// 获取或设置生成的包装器类名。
    /// </summary>
    /// <remarks>
    /// Gets or sets the generated wrapper class name.
    /// </remarks>
    /// <value>包装器类名 / Wrapper class name</value>
    public string Name { get; set; }

    /// <summary>
    /// 获取或设置父类（被包装的代理类）名称。
    /// </summary>
    /// <remarks>
    /// Gets or sets the parent (wrapped agent) class name.
    /// </remarks>
    /// <value>父类名称 / Parent class name</value>
    public string Super { get; set; }

    /// <summary>
    /// 获取或设置需要包装的方法列表。
    /// </summary>
    /// <remarks>
    /// Gets or sets the list of methods to wrap.
    /// </remarks>
    /// <value>方法列表 / Method list</value>
    public List<MethodInfoData> Methods { get; set; } = new();

    /// <summary>
    /// 获取或设置生成代码中需要引入的命名空间列表。
    /// </summary>
    /// <remarks>
    /// Gets or sets the list of namespaces to import in the generated code.
    /// </remarks>
    /// <value>命名空间列表 / Namespace list</value>
    public List<string> UsingSpaces { get; set; } = new();
}