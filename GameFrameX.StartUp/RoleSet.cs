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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Collections;

namespace GameFrameX.StartUp;

/// <summary>
/// 本进程 Role 集合不可变快照（C143b D1）。
/// </summary>
/// <remarks>
/// Immutable snapshot of the roles hosted by the current process (C143b D1).
/// A role is identified by its server type name (the <see cref="StartUpTagAttribute.ServerType"/> value);
/// membership queries ("target role belongs to this process") power the role routing seam (C143c RoleRouter).
/// The snapshot is defensively copied at construction: later changes to <see cref="StartUpTypeRegistry"/>
/// never affect an existing instance, and the type exposes no mutation API at all.
/// <see cref="Current"/> is published by <see cref="GameApp"/> once at launch time and must not change afterwards.
/// </remarks>
public sealed class RoleSet : IReadOnlySet<string>
{
    /// <summary>
    /// 当前进程的 Role 快照（缺省为空集合，由 <see cref="GameApp"/> 拉起时设置）。
    /// </summary>
    /// <remarks>
    /// The role snapshot of the current process (defaults to the empty set; assigned by <see cref="GameApp"/> at launch).
    /// </remarks>
    private static RoleSet _current = Empty;

    /// <summary>
    /// Role 名集合（ServerType 名）。
    /// </summary>
    /// <remarks>
    /// The set of role names (server type names).
    /// </remarks>
    private readonly HashSet<string> _roleNames;

    /// <summary>
    /// 初始化 <see cref="RoleSet"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="RoleSet"/> class.
    /// Defensively copies the provided startup types; the snapshot is immutable after construction,
    /// which makes publication to other threads safe without extra synchronization.
    /// </remarks>
    /// <param name="startUpTypes">按优先级排序的启动类型快照 / The priority-ordered startup types snapshot</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="startUpTypes"/> 为 null 时抛出 / Thrown when <paramref name="startUpTypes"/> is null</exception>
    internal RoleSet(IEnumerable<KeyValuePair<Type, StartUpTagAttribute>> startUpTypes)
    {
        ArgumentNullException.ThrowIfNull(startUpTypes, nameof(startUpTypes));

        StartUpTypes = startUpTypes.ToList();
        _roleNames = new HashSet<string>(StartUpTypes.Select(pair => pair.Value.ServerType));
    }

    /// <summary>
    /// 获取空 Role 快照。
    /// </summary>
    /// <remarks>
    /// Gets the empty role snapshot.
    /// </remarks>
    public static RoleSet Empty { get; } = new RoleSet(Array.Empty<KeyValuePair<Type, StartUpTagAttribute>>());

    /// <summary>
    /// 获取或设置当前进程的 Role 快照。
    /// </summary>
    /// <remarks>
    /// Gets or sets the role snapshot of the current process.
    /// Written once by the launch flow before any role starts; reads are lock-free.
    /// </remarks>
    /// <value>当前进程 Role 快照，未拉起时为 <see cref="Empty"/> / The current role snapshot, or <see cref="Empty"/> before launch</value>
    public static RoleSet Current
    {
        get { return Volatile.Read(ref _current); }
        internal set { Volatile.Write(ref _current, value); }
    }

    /// <summary>
    /// 获取按优先级排序的启动类型快照（类型 × 启动标签）。
    /// </summary>
    /// <remarks>
    /// Gets the priority-ordered startup types snapshot (type × startup tag).
    /// The launch order is this order; the shutdown order is its reverse.
    /// </remarks>
    /// <value>启动类型快照 / The startup types snapshot</value>
    public IReadOnlyList<KeyValuePair<Type, StartUpTagAttribute>> StartUpTypes { get; }

    /// <summary>
    /// 获取 Role 数量。
    /// </summary>
    /// <remarks>
    /// Gets the number of roles.
    /// </remarks>
    /// <value>Role 数量 / The number of roles</value>
    public int Count
    {
        get { return _roleNames.Count; }
    }

    /// <summary>
    /// 判定指定 Role 是否属于本进程。
    /// </summary>
    /// <remarks>
    /// Determines whether the specified role belongs to this process.
    /// </remarks>
    /// <param name="serverType">Role 的服务器类型名 / The server type name of the role</param>
    /// <returns>属于本进程则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if the role belongs to this process; otherwise, <c>false</c></returns>
    public bool Contains(string serverType)
    {
        return serverType != null && _roleNames.Contains(serverType);
    }

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator()
    {
        return _roleNames.GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public bool IsSubsetOf(IEnumerable<string> other)
    {
        return _roleNames.IsSubsetOf(other);
    }

    /// <inheritdoc />
    public bool IsSupersetOf(IEnumerable<string> other)
    {
        return _roleNames.IsSupersetOf(other);
    }

    /// <inheritdoc />
    public bool IsProperSubsetOf(IEnumerable<string> other)
    {
        return _roleNames.IsProperSubsetOf(other);
    }

    /// <inheritdoc />
    public bool IsProperSupersetOf(IEnumerable<string> other)
    {
        return _roleNames.IsProperSupersetOf(other);
    }

    /// <inheritdoc />
    public bool Overlaps(IEnumerable<string> other)
    {
        return _roleNames.Overlaps(other);
    }

    /// <inheritdoc />
    public bool SetEquals(IEnumerable<string> other)
    {
        return _roleNames.SetEquals(other);
    }
}
