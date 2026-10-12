// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规之开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights or interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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

namespace GameFrameX.Core.Hotfix;

/// <summary>
/// 热更装载参数对象：承载版本、目录与文件名三要素，集中校验并单点拼装装载路径。
/// Hotfix load options: carries the version / directory / file-name triple, centralizing validation and
/// resolving the load path in one place.
/// </summary>
/// <remarks>
/// 取代历史的 <c>dllVersion</c> / <c>dllPath</c> / <c>hotfixDllName</c> 三散参形态：校验与路径拼装收敛到
/// <see cref="ResolveAssemblyFilePath"/> 单点（历史在两个装载入口各拼一遍），后续扩展字段（宽限覆盖、
/// 哈希校验等）不再破坏调用方签名。
/// Replaces the historical <c>dllVersion</c> / <c>dllPath</c> / <c>hotfixDllName</c> loose-parameter shape:
/// validation and path composition converge into <see cref="ResolveAssemblyFilePath"/> (historically duplicated
/// in both load entries), and future fields (grace override, hash verification, ...) won't break caller signatures.
/// <para>
/// 路径语义与历史逐字节一致：<see cref="Version"/> 为空（含 null）时装载
/// <c>{CurrentDirectory}/{AssemblyDirectory}/{AssemblyFileName}</c>；非空时以 Version <b>整体替代</b>
/// 目录段（<c>{CurrentDirectory}/{Version}/{AssemblyFileName}</c>）。
/// Path semantics stay byte-identical to history: with an empty (or null) <see cref="Version"/> the load path is
/// <c>{CurrentDirectory}/{AssemblyDirectory}/{AssemblyFileName}</c>; when non-empty, Version <b>replaces</b> the
/// directory segment as a whole (<c>{CurrentDirectory}/{Version}/{AssemblyFileName}</c>).
/// </para>
/// <para>
/// <see cref="Version"/> 会经 HTTP 热更/回滚请求流入，属路径注入面：<see cref="ResolveAssemblyFilePath"/>
/// 拒绝含路径分隔符或「..」上跳段的版本值（校验为集中化的容错面，属装载入口唯一新增行为）。
/// <see cref="Version"/> flows in via HTTP reload/rollback requests and is a path-injection surface:
/// <see cref="ResolveAssemblyFilePath"/> rejects values containing path separators or the ".." parent segment
/// (this centralized check is the load entry's only newly-added behavior).
/// </para>
/// </remarks>
public sealed class HotfixLoadOptions
{
    /// <summary>
    /// 版本目录名（默认空）：非空时整体替代 <see cref="AssemblyDirectory"/> 作为装载目录段。
    /// The version directory name (empty by default): when non-empty it replaces <see cref="AssemblyDirectory"/> as the load directory segment.
    /// </summary>
    public string Version
    {
        get;
        init;
    } = string.Empty;

    /// <summary>
    /// 装载目录（默认 "hotfix"，相对当前工作目录）。
    /// The load directory (default "hotfix", relative to the current working directory).
    /// </summary>
    public string AssemblyDirectory
    {
        get;
        init;
    } = "hotfix";

    /// <summary>
    /// 热更程序集文件名（默认 "GameFrameX.Hotfix.dll"）。
    /// The hotfix assembly file name (default "GameFrameX.Hotfix.dll").
    /// </summary>
    public string AssemblyFileName
    {
        get;
        init;
    } = "GameFrameX.Hotfix.dll";

    /// <summary>
    /// 版本值禁止出现的路径相关字符（分隔符与卷分隔符）。
    /// Path-relevant characters (separators and the volume separator) forbidden in version values.
    /// </summary>
    private static readonly char[] ForbiddenVersionChars = { '/', '\\', ':' };

    /// <summary>
    /// 解析热更程序集完整装载路径（语义见类备注；含集中校验）。
    /// Resolves the full load path of the hotfix assembly (semantics in class remarks; validation included).
    /// </summary>
    /// <returns>完整装载路径 / The full load path.</returns>
    /// <exception cref="ArgumentException">
    /// <see cref="AssemblyDirectory"/> 或 <see cref="AssemblyFileName"/> 为空；或 <see cref="Version"/> 含路径分隔符 / 「..」段。
    /// <see cref="AssemblyDirectory"/> or <see cref="AssemblyFileName"/> is empty; or <see cref="Version"/> contains path separators / a ".." segment.
    /// </exception>
    public string ResolveAssemblyFilePath()
    {
        ArgumentException.ThrowIfNullOrEmpty(AssemblyDirectory);
        ArgumentException.ThrowIfNullOrEmpty(AssemblyFileName);
        var directory = AssemblyDirectory;
        if (!string.IsNullOrEmpty(Version))
        {
            // 版本值来自 HTTP 回滚流，拒绝路径分隔符与「..」上跳段，防装载路径被注入逃逸
            // The version flows in from HTTP rollback traffic: reject separators and ".." so the load path cannot be escaped
            if (Version.IndexOfAny(ForbiddenVersionChars) >= 0 || Version.Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Version 含非法路径字符: {Version} / Version contains forbidden path characters: {Version}", nameof(Version));
            }

            directory = Version;
        }

        return Path.Combine(Environment.CurrentDirectory, directory, AssemblyFileName);
    }
}
