// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//   This project is dual-licensed under the MIT License and Apache License 2.0,
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

using System.Text;

namespace GameFrameX.Hotfix.WrapperGenerator.Agent;

/// <summary>
/// 方法元数据，描述代理类中需要包装的方法信息。
/// </summary>
/// <remarks>
/// Method metadata describing the method information to wrap in the agent class.
/// </remarks>
public sealed class MethodInfoData
{
    /// <summary>
    /// 获取或设置方法名。
    /// </summary>
    /// <remarks>
    /// Gets or sets the method name.
    /// </remarks>
    /// <value>方法名 / Method name</value>
    public string Name { get; set; }

    /// <summary>
    /// 获取或设置返回类型。
    /// </summary>
    /// <remarks>
    /// Gets or sets the return type.
    /// </remarks>
    /// <value>返回类型 / Return type</value>
    public string ReturnType { get; set; }

    /// <summary>
    /// 获取完整的方法签名声明字符串。
    /// </summary>
    /// <remarks>
    /// Gets the complete method signature declaration string.
    /// </remarks>
    /// <value>方法签名 / Method signature</value>
    public string Declare
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Modify);
            sb.Append(ReturnType);
            sb.Append(" ");
            sb.Append(Name);
            sb.Append(Typeparams);
            sb.Append(ParamDeclare);
            return sb.ToString();
        }
    }

    /// <summary>
    /// 获取或设置方法是否标记为 Api 接口。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the method is marked as an API interface.
    /// </remarks>
    /// <value>如果标记为 Api 则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if marked as API; otherwise <c>false</c></value>
    public bool IsApi { get; set; }

    /// <summary>
    /// 获取或设置方法修饰符字符串（如 override、public 等）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the method modifier string (e.g., override, public).
    /// </remarks>
    /// <value>修饰符字符串 / Modifier string</value>
    public string Modify { get; set; }

    /// <summary>
    /// 获取或设置方法是否为公开方法。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the method is public.
    /// </remarks>
    /// <value>如果为公开方法则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if public; otherwise <c>false</c></value>
    public bool IsPublic { get; set; }

    /// <summary>
    /// 获取或设置方法是否为静态方法。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the method is static.
    /// </remarks>
    /// <value>如果为静态方法则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if static; otherwise <c>false</c></value>
    public bool IsStatic { get; set; }

    /// <summary>
    /// 获取或设置方法是否为虚方法。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the method is virtual.
    /// </remarks>
    /// <value>如果为虚方法则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if virtual; otherwise <c>false</c></value>
    public bool IsVirtual { get; set; }

    /// <summary>
    /// 获取或设置方法是否为异步方法。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the method is async.
    /// </remarks>
    /// <value>如果为异步方法则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if async; otherwise <c>false</c></value>
    public bool IsAsync { get; set; }

    /// <summary>
    /// 获取方法参数名列表。
    /// </summary>
    /// <remarks>
    /// Gets the list of method parameter names.
    /// </remarks>
    /// <value>参数名列表 / Parameter name list</value>
    public List<string> Params { get; } = new();

    /// <summary>
    /// 获取方法上的属性标记列表。
    /// </summary>
    /// <remarks>
    /// Gets the list of attribute annotations on the method.
    /// </remarks>
    /// <value>属性标记列表 / Attribute list</value>
    public List<string> AttributeList { get; private set; } = new();

    /// <summary>
    /// 获取或设置方法是否标记为丢弃返回值（即不等待异步完成）。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the method is marked to discard the return value (i.e., not awaiting async completion).
    /// </remarks>
    /// <value>如果丢弃返回值则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if discarding return value; otherwise <c>false</c></value>
    public bool Discard { get; set; }

    /// <summary>
    /// 获取或设置方法是否设置了超时时间。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the method has a timeout configured.
    /// </remarks>
    /// <value>如果设置了超时则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if timeout is set; otherwise <c>false</c></value>
    public bool HasTimeout { get; set; }

    /// <summary>
    /// 获取或设置超时时间（毫秒）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the timeout duration in milliseconds.
    /// </remarks>
    /// <value>超时时间，默认为 <see cref="int.MaxValue"/> / Timeout duration, defaults to <see cref="int.MaxValue"/></value>
    public int TimeOut { get; set; } = int.MaxValue;

    /// <summary>
    /// 获取或设置方法是否标记为线程安全。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the method is marked as thread-safe.
    /// </remarks>
    /// <value>如果线程安全则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if thread-safe; otherwise <c>false</c></value>
    public bool IsThreadSafe { get; set; }

    /// <summary>
    /// 获取或设置泛型约束字符串。
    /// </summary>
    /// <remarks>
    /// Gets or sets the generic constraint string.
    /// </remarks>
    /// <value>泛型约束 / Generic constraint</value>
    public string Constraint { get; set; }

    /// <summary>
    /// 获取或设置泛型参数声明字符串。
    /// </summary>
    /// <remarks>
    /// Gets or sets the generic type parameter declaration string.
    /// </remarks>
    /// <value>泛型参数 / Generic type parameters</value>
    public string Typeparams { get; set; }

    /// <summary>
    /// 获取或设置参数声明字符串（包含类型和名称）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the parameter declaration string (including types and names).
    /// </remarks>
    /// <value>参数声明 / Parameter declaration</value>
    public string ParamDeclare { get; set; }

    /// <summary>
    /// 获取以逗号分隔的参数名字符串，用于方法调用。
    /// </summary>
    /// <remarks>
    /// Gets the comma-separated parameter name string used for method invocation.
    /// </remarks>
    /// <value>参数字符串 / Parameter string</value>
    public string ParamString
    {
        get
        {
            if (Params.Count <= 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < Params.Count; i++)
            {
                sb.Append(Params[i]);
                if (i != Params.Count - 1)
                {
                    sb.Append(",");
                }
            }

            return sb.ToString();
        }
    }
}