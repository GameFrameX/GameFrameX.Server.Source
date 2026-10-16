// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text.RegularExpressions;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 能力包结构门禁测试（C228 P1：C223 目录矩阵的包级升维）——包间 ProjectReference 方向断言
    /// （精确引用集合 = 无多引无漏引）+ Hosting 三包对能力包引用恒零（机制开放红线）+
    /// 脊柱包零上行引用 + 工程引用图无环。
    /// </summary>
    public class CapabilityPackageStructureTests
    {
        /// <summary>
        /// 期望的 ProjectReference 精确集合（工程 → 允许引用的工程名集合；C228 P1 终态 + C231 P3 增量：三叶子包 + 脊柱 admin 线缆契约边 + 单体/壳叶子引用）。
        private static readonly Dictionary<string, string[]> ExpectedProjectReferences = new()
        {
            ["GameFrameX.Capability.Hosting.Abstractions"] = Array.Empty<string>(),
            ["GameFrameX.Capability.Hosting.Core"] = new[] { "GameFrameX.Capability.Hosting.Abstractions", "GameFrameX.Localization" },
            ["GameFrameX.Capability.Hosting.AdminApi"] = new[] { "GameFrameX.Capability.Hosting.Abstractions", "GameFrameX.Localization" },
            ["GameFrameX.Capability.Online.Assets"] = new[] { "GameFrameX.Capability.Online.Abstractions", "GameFrameX.Capability.Hosting.Core" },
            ["GameFrameX.Capability.Online.Social"] = new[] { "GameFrameX.Capability.Online.Abstractions", "GameFrameX.Capability.Online.Foundation", "GameFrameX.Capability.Hosting.Core" },
            ["GameFrameX.Capability.Online.Matchmaking"] = new[] { "GameFrameX.Capability.Online.Abstractions", "GameFrameX.Capability.Hosting.Core" },
            ["GameFrameX.Capability.Online.Match"] = new[] { "GameFrameX.Capability.Online.Abstractions", "GameFrameX.Capability.Online.Assets", "GameFrameX.Capability.Online.Matchmaking", "GameFrameX.Capability.Hosting.Core" },
            ["GameFrameX.Capability.Online.Competition"] = new[] { "GameFrameX.Capability.Online.Abstractions", "GameFrameX.Capability.Online.Assets", "GameFrameX.Capability.Online.Match", "GameFrameX.Capability.Hosting.Core" },
            ["GameFrameX.Capability.Online.Operations"] = new[] { "GameFrameX.Capability.Online.Abstractions", "GameFrameX.Capability.Online.Foundation", "GameFrameX.Capability.Online.Assets", "GameFrameX.Capability.Online.Social", "GameFrameX.Capability.Online.Matchmaking", "GameFrameX.Capability.Online.Match", "GameFrameX.Capability.Hosting.Core" },
            ["GameFrameX.Capability.Online.Abstractions"] = new[] { "GameFrameX.Localization", "GameFrameX.Capability.Hosting.AdminApi" },
            ["GameFrameX.Capability.Online.Foundation"] = new[] { "GameFrameX.Capability.Online.Abstractions", "GameFrameX.Capability.Hosting.Core" },
            ["GameFrameX.Capability.Online.Persistence"] = new[] { "GameFrameX.Capability.Online.Abstractions", "GameFrameX.Capability.Online.Foundation", "GameFrameX.Capability.Online.Assets", "GameFrameX.Capability.Hosting.Core", "GameFrameX.DataBase" },
        };

        /// <summary>
        /// Hosting 机制包名集合（对能力包引用恒零的红线主体）。
        /// </summary>
        private static readonly string[] HostingPackages =
        {
            "GameFrameX.Capability.Hosting.Abstractions",
            "GameFrameX.Capability.Hosting.Core",
            "GameFrameX.Capability.Hosting.AdminApi",
        };

        /// <summary>
        /// 能力包名前缀（Hosting 三包不得引用的目标域）。
        /// </summary>
        private static readonly string[] CapabilityNamePrefixes =
        {
            "GameFrameX.Capability.Online",
            "GameFrameX.Capability.World",
            "GameFrameX.Online",
        };

        /// <summary>
        /// 断言各工程 ProjectReference 精确匹配期望集合（无多引无漏引 = 方向 DAG 的强形式）。
        /// </summary>
        [Fact]
        public void ProjectReferences_MatchDeclaredPackageGraph()
        {
            var violations = new List<string>();
            foreach (var (project, expected) in ExpectedProjectReferences)
            {
                var actual = ReadProjectReferences(project);
                var missing = expected.Except(actual).ToList();
                var extra = actual.Except(expected).ToList();
                if (missing.Count > 0)
                {
                    violations.Add($"{project} 缺少引用：{string.Join(", ", missing)}");
                }

                if (extra.Count > 0)
                {
                    violations.Add($"{project} 多出引用：{string.Join(", ", extra)}");
                }
            }

            Assert.True(violations.Count == 0, "包引用图偏离声明拓扑：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        /// <summary>
        /// 断言组合根（Launcher）直引全部 Online 能力包（C232 P6 终态：单体与 Runtime 壳退役，
        /// 编排回归 Launcher 组合根 + BuildHost 真扫描生产采用——扫描面 = Launcher 程序集引用面）。
        /// </summary>
        [Fact]
        public void Launcher_ReferencesOnlineCapabilityPackages_Directly()
        {
            var actual = ReadProjectReferences("GameFrameX.Launcher");
            var capabilityRelated = actual.Where(reference => CapabilityNamePrefixes.Any(prefix => reference.StartsWith(prefix, StringComparison.Ordinal)))
                .OrderBy(reference => reference, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(new[]
            {
                "GameFrameX.Capability.Online.Abstractions",
                "GameFrameX.Capability.Online.Assets",
                "GameFrameX.Capability.Online.Competition",
                "GameFrameX.Capability.Online.Foundation",
                "GameFrameX.Capability.Online.Match",
                "GameFrameX.Capability.Online.Matchmaking",
                "GameFrameX.Capability.Online.Operations",
                "GameFrameX.Capability.Online.Persistence",
                "GameFrameX.Capability.Online.Social",
            }, capabilityRelated);
        }

        /// <summary>
        /// 结构性断言：Hosting 三包对全部能力包（Online 肢体/聚合、单体、World 族）的 ProjectReference = 0
        /// （C227 §6 裁定 4 机制开放红线的门禁化——新增能力 = 新 csproj + manifest，机制零改动）。
        /// </summary>
        [Fact]
        public void HostingPackages_HaveZeroCapabilityProjectReferences()
        {
            var violations = new List<string>();
            foreach (var hostingPackage in HostingPackages)
            {
                foreach (var reference in ReadProjectReferences(hostingPackage))
                {
                    if (CapabilityNamePrefixes.Any(prefix => reference.StartsWith(prefix, StringComparison.Ordinal)))
                    {
                        violations.Add($"{hostingPackage} -> {reference}");
                    }
                }
            }

            Assert.True(violations.Count == 0, "机制包出现能力包引用（机制开放红线）：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        /// <summary>
        /// 断言叶子能力包（Assets / Social / Matchmaking(+Party)）横向互引恒为零（C231 P3 终态的包级红线；
        /// 源级扫描 using 指令与限定名，包间 ProjectReference 面由 CapabilityPackageStructureTests 精确集合断言）。
        /// </summary>
        [Fact]
        public void LeafCapabilityPackages_HaveNoCrossPackageReferences()
        {
            var packageRoots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GameFrameX.Capability.Online.Assets"] = "Assets",
                ["GameFrameX.Capability.Online.Social"] = "Social",
                ["GameFrameX.Capability.Online.Matchmaking"] = "Matchmaking",
            };
            var usingDirective = new Regex(@"^[ \t]*using\s+(?:static\s+)?([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;", RegexOptions.Multiline);
            var violations = new List<string>();
            foreach (var (package, leaf) in packageRoots)
            {
                foreach (var file in EnumeratePackageSourceFiles(package))
                {
                    var code = StripCommentsAndStrings(File.ReadAllText(file));
                    foreach (Match match in usingDirective.Matches(code))
                    {
                        var target = match.Groups[1].Value;
                        foreach (var (otherPackage, otherLeaf) in packageRoots)
                        {
                            if (!string.Equals(otherLeaf, leaf, StringComparison.Ordinal)
                                && (target.StartsWith(otherPackage + ".", StringComparison.Ordinal) || string.Equals(target, otherPackage, StringComparison.Ordinal)))
                            {
                                violations.Add($"{Path.GetFileName(file)}: {target}");
                            }
                        }
                    }

                    foreach (var (otherPackage, otherLeaf) in packageRoots)
                    {
                        if (!string.Equals(otherLeaf, leaf, StringComparison.Ordinal) && code.Contains(otherPackage + ".", StringComparison.Ordinal))
                        {
                            violations.Add($"{Path.GetFileName(file)}: {otherPackage}.*（限定名）");
                        }
                    }
                }
            }

            Assert.True(violations.Count == 0, "叶子能力包横向互引必须为零：" + Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct()));
        }


        /// <summary>
        /// 断言工程引用图无环（Kahn 拓扑消耗全部节点）。
        /// </summary>
        [Fact]
        public void ProjectReferenceGraph_IsAcyclic()
        {
            var graph = ExpectedProjectReferences.Keys.ToDictionary(name => name, _ => new List<string>());
            foreach (var (project, references) in ExpectedProjectReferences)
            {
                foreach (var reference in references)
                {
                    if (graph.ContainsKey(reference))
                    {
                        graph[project].Add(reference);
                    }
                }
            }

            // graph[project] = project 的在集依赖集合；按「依赖全部落定即可消耗」做 Kahn。
            var pending = graph.Keys.ToDictionary(name => name, name => graph[name].Count);
            var ready = new Queue<string>(graph.Keys.Where(name => pending[name] == 0).OrderBy(name => name, StringComparer.Ordinal));
            var consumed = 0;
            while (ready.Count > 0)
            {
                var name = ready.Dequeue();
                consumed++;
                foreach (var dependent in graph.Where(pair => pair.Value.Contains(name)).Select(pair => pair.Key))
                {
                    pending[dependent]--;
                    if (pending[dependent] == 0)
                    {
                        ready.Enqueue(dependent);
                    }
                }
            }

            Assert.True(consumed == graph.Count, "工程引用图存在环：" + string.Join(", ", graph.Keys.Where(name => pending[name] > 0)));
        }

        /// <summary>
        /// 断言脊柱包（Capability.Online.Abstractions）对地基/域命名空间零引用
        /// （包内方向规则：只允许脊柱内部向下；地基与肢体命名空间出现在 Abstractions 源码即违规，
        /// 含 using 之外的限定名形态——C223 Events 零域引用断言的包级升维）。
        /// </summary>
        [Fact]
        public void SpinePackage_HasNoUpwardOrDomainTypeReferences()
        {
            var upwardPattern = new Regex(@"GameFrameX\.Capability\.Online\.Foundation\.(Identity|Session|Presence|Storage)\.[A-Z]|GameFrameX\.Online\.(Assets|Audit|GameEvents|HotfixRollback|Leaderboard|Match|Matchmaking|Overview|Party|Season|Social|Timeline|Tournament)\.[A-Z]");
            var violations = new List<string>();
            foreach (var file in EnumeratePackageSourceFiles("GameFrameX.Capability.Online.Abstractions"))
            {
                var code = StripCommentsAndStrings(File.ReadAllText(file));
                foreach (Match match in upwardPattern.Matches(code))
                {
                    violations.Add($"{Path.GetFileName(file)}: {match.Value}");
                }
            }

            Assert.True(violations.Count == 0, "脊柱包残留上行/域类型引用：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        /// <summary>
        /// 断言地基包（Capability.Online.Foundation）内目录依赖为簇内向下
        /// （Session→Identity 允许；Identity/Presence/Storage 互引与对 Session 引用恒零）。
        /// </summary>
        [Fact]
        public void FoundationPackage_IntraDirectoryEdges_RemainWithinClusterRules()
        {
            var usingDirective = new Regex(@"^[ \t]*using\s+(?:static\s+)?([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;", RegexOptions.Multiline);
            var namespaceDirective = new Regex(@"namespace\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)");
            var allowedEdges = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Identity"] = Array.Empty<string>(),
                ["Session"] = new[] { "Identity" },
                ["Presence"] = Array.Empty<string>(),
                ["Storage"] = Array.Empty<string>(),
            };
            var violations = new List<string>();
            foreach (var file in EnumeratePackageSourceFiles("GameFrameX.Capability.Online.Foundation"))
            {
                var from = Path.GetDirectoryName(file)!.Split(Path.DirectorySeparatorChar).Last();
                if (!allowedEdges.ContainsKey(from))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                var namespaceMatch = namespaceDirective.Match(text);
                foreach (Match match in usingDirective.Matches(text))
                {
                    var target = match.Groups[1].Value;
                    string to;
                    if (target.StartsWith("GameFrameX.Capability.Online.Foundation.", StringComparison.Ordinal))
                    {
                        to = target.Substring("GameFrameX.Capability.Online.Foundation.".Length).Split('.')[0];
                    }
                    else if (namespaceMatch.Success && namespaceMatch.Groups[1].Value.StartsWith("GameFrameX.Capability.Online", StringComparison.Ordinal) && !target.Contains('.'))
                    {
                        to = target;
                    }
                    else
                    {
                        continue;
                    }

                    if (allowedEdges.ContainsKey(to) && to != from && !allowedEdges[from].Contains(to))
                    {
                        violations.Add($"{from} -> {to}（{Path.GetFileName(file)}）");
                    }
                }
            }

            Assert.True(violations.Count == 0, "地基包内目录依赖违规：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        /// <summary>
        /// 读取工程 csproj 的 ProjectReference 目标工程名集合。
        /// </summary>
        /// <param name="projectName">工程名。</param>
        /// <returns>被引用工程名集合。</returns>
        private static HashSet<string> ReadProjectReferences(string projectName)
        {
            var repositoryRoot = FindRepositoryRoot();
            var projectFile = Path.Combine(repositoryRoot, "Capability", projectName, projectName + ".csproj");
            if (!File.Exists(projectFile))
            {
                projectFile = Path.Combine(repositoryRoot, projectName, projectName + ".csproj");
            }
            var includePattern = new Regex(@"<ProjectReference\s+Include=""([^""]+)""", RegexOptions.IgnoreCase);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in includePattern.Matches(File.ReadAllText(projectFile)))
            {
                var include = match.Groups[1].Value.Replace('\\', '/');
                names.Add(include.Split('/')[^1].Replace(".csproj", string.Empty));
            }

            return names;
        }

        /// <summary>
        /// 枚举能力包全部源码文件（排除 bin/obj 产物）。
        /// </summary>
        /// <param name="packageName">包目录名。</param>
        /// <returns>源码文件路径集合。</returns>
        private static IEnumerable<string> EnumeratePackageSourceFiles(string packageName)
        {
            var root = Path.Combine(FindRepositoryRoot(), "Capability", packageName);
            Assert.True(Directory.Exists(root), "未找到包目录：" + root);
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("bin") && !path.Split(Path.DirectorySeparatorChar).Contains("obj"));
        }

        /// <summary>
        /// 从测试程序集位置向上定位仓库根（含 Server.slnx 的目录）。
        /// </summary>
        /// <returns>仓库根路径。</returns>
        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Server.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            Assert.Fail("未定位到仓库根（测试须在仓库内运行）");
            return string.Empty;
        }

        /// <summary>
        /// 剥离注释与字符串字面量（限定名守卫扫描前预处理，避免误报常量与注释）。
        /// </summary>
        /// <param name="text">源码文本。</param>
        /// <returns>预处理后文本。</returns>
        private static string StripCommentsAndStrings(string text)
        {
            text = new Regex(@"@""(?:[^""]|"""")*""").Replace(text, string.Empty);
            text = new Regex(@"""(?:[^""\\\n]|\\.)*""").Replace(text, string.Empty);
            text = new Regex(@"//[^\n]*").Replace(text, string.Empty);
            text = new Regex(@"/\*.*?\*/", RegexOptions.Singleline).Replace(text, string.Empty);
            return text;
        }
    }
}
