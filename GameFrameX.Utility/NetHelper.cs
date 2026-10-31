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

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Utility;

/// <summary>
/// 网络帮助类
/// 提供网络相关的实用工具方法，包括IP地址验证、端口可用性检查、本地IP获取等功能
/// </summary>
/// <remarks>
/// 核心功能：
/// - IP地址格式验证和解析
/// - 端口可用性检测和管理
/// - 本地网络接口信息获取
/// - 网络连接状态查询
/// 
/// 适用场景：
/// - 网络服务器端口分配
/// - 客户端连接配置验证
/// - 网络诊断和监控
/// - 分布式系统节点发现
/// </remarks>
public static class NetHelper
{
    /// <summary>
    /// 异步 DNS 解析内建默认超时（毫秒），约束解析端黑洞不无限悬挂调用方。
    /// </summary>
    private const int DefaultDnsTimeoutMilliseconds = 5000;

    /// <summary>
    /// 端口诊断子进程（lsof / netstat）的退出等待上限（毫秒）。
    /// </summary>
    private const int PortDiagnosticProcessTimeoutMilliseconds = 1500;

    /// <summary>
    /// 强制结束诊断子进程后，等待 stdout/stderr 读取完成的有界预算（毫秒）。
    /// </summary>
    private const int ProcessKillDrainTimeoutMilliseconds = 500;

    /// <summary>
    /// 获取指定端口的占用进程信息（仅监听中的 TCP）。
    /// </summary>
    /// <param name="port">端口号</param>
    /// <param name="maxCount">最大返回条数，默认5</param>
    /// <returns>占用进程文本列表，获取失败或无结果时返回空列表</returns>
    /// <exception cref="ArgumentOutOfRangeException">当端口不在合法范围时抛出</exception>
    public static List<string> GetPortOccupyingProcesses(int port, int maxCount = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);

        if (OperatingSystem.IsWindows())
        {
            return GetPortOccupyingProcessesByNetstat(port, maxCount);
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            return GetPortOccupyingProcessesByLsof(port, maxCount);
        }

        return new List<string>();
    }

    /// <summary>
    /// 启动外部进程并读取其标准输出，超时后强制结束进程。
    /// </summary>
    /// <remarks>
    /// stdout 与 stderr 必须并发读取：先同步 <c>ReadToEnd()</c> 再 <c>WaitForExit</c>，
    /// 在 RedirectStandardError 为 true 且子进程写满 stderr 管道（4-64KB）时，
    /// 子进程阻塞在写 stderr、父进程阻塞在读 stdout，双方互等形成死锁。
    /// </remarks>
    /// <param name="startInfo">进程启动参数，必须重定向 stdout 与 stderr</param>
    /// <param name="timeoutMilliseconds">等待进程退出的上限（毫秒）</param>
    /// <returns>标准输出文本；进程无法启动时返回空串</returns>
    private static string ReadProcessStandardOutput(ProcessStartInfo startInfo, int timeoutMilliseconds)
    {
        using (var process = Process.Start(startInfo))
        {
            if (process == null)
            {
                return string.Empty;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (process.WaitForExit(timeoutMilliseconds))
            {
                Task.WhenAll(outputTask, errorTask).GetAwaiter().GetResult();
                return outputTask.Result;
            }

            try
            {
                process.Kill(true);
            }
            catch (Exception)
            {
                // 进程已退出或无权结束：仍需按有界预算读取，绝不让调用线程无限阻塞。
            }

            // 强制结束后再给管道读取一个有界预算；kill 被权限/策略拒绝且管道未关闭时，
            // 超时即返回（最多丢失诊断输出），保证 timeoutMilliseconds 的契约成立。
            try
            {
                if (Task.WaitAll(new Task[] { outputTask, errorTask }, ProcessKillDrainTimeoutMilliseconds))
                {
                    return outputTask.Result;
                }
            }
            catch (Exception)
            {
                // 强制结束导致读取任务失败：退化为返回已成功读取的部分。
            }

            // 超时返回后 using 会释放 process，仍在进行的读取任务可能抛异常；
            // 挂 OnlyOnFaulted 续体观察，避免 UnobservedTaskException 噪声。
            ObserveFault(outputTask);
            ObserveFault(errorTask);
            return outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
        }
    }

    /// <summary>
    /// 观察后台任务的异常，避免被回收时产生 <see cref="TaskScheduler.UnobservedTaskException"/>。
    /// </summary>
    /// <param name="task">待观察任务</param>
    private static void ObserveFault(Task task)
    {
        task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// 在 macOS/Linux 下通过 lsof 获取端口占用进程信息。
    /// </summary>
    /// <param name="port">端口号</param>
    /// <param name="maxCount">最大返回条数</param>
    /// <returns>占用进程文本列表</returns>
    private static List<string> GetPortOccupyingProcessesByLsof(int port, int maxCount)
    {
        try
        {
            // Sonar csharpsquid:S4036 要求 ProcessStartInfo.FileName 必须为绝对路径，
            // 避免 PATH 注入。按 OS 选取 lsof 的默认绝对路径。
            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsMacOS() ? "/usr/sbin/lsof" : "/usr/bin/lsof",
                Arguments = $"-nP -iTCP:{port} -sTCP:LISTEN",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var output = ReadProcessStandardOutput(startInfo, PortDiagnosticProcessTimeoutMilliseconds);

            if (string.IsNullOrWhiteSpace(output))
            {
                return new List<string>();
            }

            return output
                   .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(line => !line.StartsWith("COMMAND", StringComparison.OrdinalIgnoreCase))
                   .Take(maxCount)
                   .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 在 Windows 下通过 netstat 获取端口占用进程信息。
    /// </summary>
    /// <param name="port">端口号</param>
    /// <param name="maxCount">最大返回条数</param>
    /// <returns>占用进程文本列表</returns>
    private static List<string> GetPortOccupyingProcessesByNetstat(int port, int maxCount)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netstat.exe"),
                Arguments = "-ano -p tcp",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var output = ReadProcessStandardOutput(startInfo, PortDiagnosticProcessTimeoutMilliseconds);
            if (string.IsNullOrWhiteSpace(output))
            {
                return new List<string>();
            }

            var result = new List<string>();
            var deDuplicate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                var item = TryBuildNetstatPortItem(line, port);
                if (item == null)
                {
                    continue;
                }

                if (!deDuplicate.Add(item))
                {
                    continue;
                }

                result.Add(item);
                if (result.Count >= maxCount)
                {
                    break;
                }
            }

            return result;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 解析单行 netstat 输出，若为监听目标端口的 TCP 行则返回格式化条目，否则返回 null。
    /// </summary>
    /// <param name="line">netstat 单行输出</param>
    /// <param name="port">目标端口号</param>
    /// <returns>命中目标端口时返回格式化条目；不匹配返回 null</returns>
    private static string TryBuildNetstatPortItem(string line, int port)
    {
        if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var columns = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        // 典型格式：TCP LocalAddress ForeignAddress State PID
        if (columns.Length < 5)
        {
            return null;
        }

        var localAddress = columns[1];
        var state = columns[3];
        var pidRaw = columns[4];
        if (!string.Equals(state, "LISTENING", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!(localAddress.EndsWith($":{port}", StringComparison.OrdinalIgnoreCase) ||
              localAddress.EndsWith($"]:{port}", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (!int.TryParse(pidRaw, out var pid))
        {
            return null;
        }

        var processName = "Unknown";
        try
        {
            processName = Process.GetProcessById(pid).ProcessName;
        }
        catch
        {
            // 在部分系统权限下可能无法读取进程名，此时使用 Unknown
        }

        return $"{processName} {pid} {localAddress} (LISTENING)";
    }

    /// <summary>
    /// 解析主机名或IP地址和端口号为EndPoint对象
    /// </summary>
    /// <param name="host">主机名或IP地址字符串</param>
    /// <param name="port">端口号</param>
    /// <returns>解析成功的EndPoint对象；如果解析失败，返回null</returns>
    /// <exception cref="ArgumentException">当host为null或空字符串时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当port小于等于0时抛出此异常</exception>
    public static EndPoint ParseEndPoint(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        EndPoint connectEndPoint = default;
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            connectEndPoint = new IPEndPoint(ipAddress, port);
        }
        else
        {
#pragma warning disable CS0618 // 同步 DNS 解析仅为同步调用方保留，内部互调不触发废弃提示
            var ipHost = GetHostIPv4(host);
            if (IPAddress.TryParse(ipHost, out ipAddress))
            {
                connectEndPoint = new IPEndPoint(ipAddress, port);
            }
            else
            {
                ipHost = GetHostIPv6(host);
                if (IPAddress.TryParse(ipHost, out ipAddress))
                {
                    connectEndPoint = new IPEndPoint(ipAddress, port);
                }
            }
#pragma warning restore CS0618
        }

        return connectEndPoint;
    }

    /// <summary>
    /// 异步解析主机名或IP地址和端口号为EndPoint对象（DNS 解析可取消，不阻塞线程池线程）。
    /// </summary>
    /// <param name="host">主机名或IP地址字符串</param>
    /// <param name="port">端口号</param>
    /// <param name="cancellationToken">取消令牌（约束 DNS 解析等待上限）</param>
    /// <returns>解析成功的EndPoint对象；如果解析失败，返回null</returns>
    /// <exception cref="ArgumentException">当host为null或空字符串时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当port小于等于0时抛出此异常</exception>
    public static async Task<EndPoint> ParseEndPointAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return new IPEndPoint(ipAddress, port);
        }

        var addressList = await ResolveHostAddressesAsync(host, cancellationToken);
        foreach (var address in addressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return new IPEndPoint(address, port);
            }
        }

        foreach (var address in addressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return new IPEndPoint(address, port);
            }
        }

        return null;
    }

    /// <summary>
    /// 判断IP地址是否合法
    /// </summary>
    /// <param name="ipAddress">IP地址字符串</param>
    /// <param name="value">解析成功的IPAddress对象</param>
    /// <returns>如果IP地址合法，返回true；否则返回false</returns>
    /// <exception cref="ArgumentNullException">当ipAddress为null时抛出此异常</exception>
    public static bool IsValidIpAddress(string ipAddress, out IPAddress value)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        value = null;

        // 首先尝试解析IP地址
        if (!IPAddress.TryParse(ipAddress, out var parsedIp))
        {
            return false;
        }

        // 验证解析后的IP地址字符串表示是否与原始输入完全匹配
        // 这可以防止IPAddress.TryParse自动补全不完整的IP地址（如"192.168.1"被解析为"192.168.0.1"）
        if (parsedIp.ToString() != ipAddress)
        {
            return false;
        }

        value = parsedIp;
        return true;
    }

    /// <summary>
    /// 获取第一个可用的端口号
    /// </summary>
    /// <remarks>
    /// 一次性取本机连接快照（<see cref="PortIsUsed"/>）后逐候选做真实 bind 探测，
    /// 避免对每个端口重复枚举全表。快照在循环期间可能过期；返回端口到调用方实际 bind
    /// 之间仍存在竞态，调用方必须处理 bind 失败。
    /// </remarks>
    /// <param name="startPort">起始端口号，默认为667</param>
    /// <param name="maxPort">结束端口号，默认为65535</param>
    /// <returns>第一个可用的端口号，如果没有可用端口号则返回-1</returns>
    /// <exception cref="ArgumentOutOfRangeException">当startPort小于1或大于65535时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当maxPort小于1或大于65535时抛出此异常</exception>
    /// <exception cref="ArgumentException">当startPort大于等于maxPort时抛出此异常</exception>
    public static int GetFirstAvailablePort(int startPort = 667, int maxPort = 65535)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startPort, 65535);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPort, 65535);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startPort, maxPort);

        // 一次性快照 + 逐候选真实 bind：避免对每个端口都重新枚举本机连接表。
        var usedPorts = PortIsUsed().ToHashSet();
        for (var i = startPort; i < maxPort; i++)
        {
            if (usedPorts.Contains(i))
            {
                continue;
            }

            if (!IsPortBindConflict(i))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 获取操作系统已用的端口号
    /// </summary>
    /// <returns>包含已用端口号的列表</returns>
    public static List<int> PortIsUsed()
    {
        // 获取本地计算机的网络连接和通信统计数据的信息
        var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();

        // 返回本地计算机上的所有TCP监听程序
        var ipsTcp = ipGlobalProperties.GetActiveTcpListeners();

        // 返回本地计算机上的所有UDP监听程序
        var ipsUdp = ipGlobalProperties.GetActiveUdpListeners();

        // 返回本地计算机上的Internet协议版本4 (IPV4) 传输控制协议 (TCP) 连接的信息
        var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();

        var allPorts = new List<int>();
        foreach (var ep in ipsTcp)
        {
            allPorts.Add(ep.Port);
        }

        foreach (var ep in ipsUdp)
        {
            allPorts.Add(ep.Port);
        }

        foreach (var conn in tcpConnInfoArray)
        {
            allPorts.Add(conn.LocalEndPoint.Port);
        }

        return allPorts;
    }

    /// <summary>
    /// 检查指定端口是否可用
    /// </summary>
    /// <remarks>
    /// 双重判定：先查本机 TCP/UDP 监听与活动连接快照，未命中再用 TcpListener
    /// 对目标端口做真实 bind 试探（覆盖「其他进程正在 bind 但尚未出现在快照」的窗口）。
    /// bind 探测为 TCP/IPv4 通配（<see cref="IPAddress.Any"/>）：UDP 与仅 IPv6 的占用
    /// 由快照兜底。无权限（如 Unix 上 1024 以下端口）等无法判定的 bind 失败视为可用，
    /// 由调用方实际 bind 时决定。因此返回 true 只表示「未检测到冲突」，
    /// 不保证随后的 bind 一定成功；快照与 bind 之间仍存在 TOCTOU，真正可靠的占用判定
    /// 是直接 bind。
    /// </remarks>
    /// <param name="port">要检查的端口号</param>
    /// <returns>如果端口未被使用，返回true；否则返回false</returns>
    /// <exception cref="ArgumentOutOfRangeException">当port小于1或大于65535时抛出此异常</exception>
    public static bool PortIsAvailable(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        if (PortIsUsed().Contains(port))
        {
            return false;
        }

        return !IsPortBindConflict(port);
    }

    /// <summary>
    /// 用真实 bind 试探目标端口是否被其他进程占用。
    /// </summary>
    /// <remarks>
    /// 仅把 AddressAlreadyInUse 视为占用；无权限、地址族不可用等其他 SocketException
    /// 无法证明占用，返回 false（可用），避免把「本进程无权 bind」误报成「端口被占」。
    /// </remarks>
    /// <param name="port">目标端口号</param>
    /// <returns>真实 bind 命中占用时返回 true</returns>
    private static bool IsPortBindConflict(int port)
    {
        try
        {
            using (var listener = new TcpListener(IPAddress.Any, port))
            {
                listener.Start();
                listener.Stop();
            }

            return false;
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// 获取本地IP地址列表
    /// </summary>
    /// <param name="addressFamily">IP地址类型,默认为IPv4</param>
    /// <returns>本地IP地址列表</returns>
    public static List<string> GetLocalIpList(AddressFamily addressFamily = AddressFamily.InterNetwork)
    {
        var ipList = new List<string>();
        try
        {
            // 获取本地计算机的网络接口信息
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var network in networkInterfaces)
            {
                // 排除非活动状态和环回接口
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }


                // 获取网络接口的IP属性
                var properties = network.GetIPProperties();
                // 跳过没有网关的地址
                var gateways = properties.GatewayAddresses;
                if (gateways == null || gateways.Count == 0)
                {
                    continue;
                }

                foreach (var address in properties.UnicastAddresses)
                {
                    // 根据指定的地址类型筛选IP
                    if (address.Address.AddressFamily == addressFamily)
                    {
                        ipList.Add(address.Address.ToString());
                    }
                }
            }
        }
        catch (Exception)
        {
            // 发生异常时返回空列表
            return new List<string>();
        }

        return ipList;
    }

    /// <summary>
    /// 检测网络连通性
    /// </summary>
    /// <param name="host">目标主机地址</param>
    /// <param name="timeout">超时时间（毫秒），默认5000毫秒</param>
    /// <returns>如果网络连通，返回true；否则返回false</returns>
    /// <exception cref="ArgumentException">当host为null或空字符串时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当timeout小于0时抛出此异常</exception>
    [Obsolete("同步 Ping 会无限期阻塞调用线程，请改用 IsNetworkReachableAsync。Sync Ping blocks the calling thread indefinitely; use IsNetworkReachableAsync instead.", false)]
    public static bool IsNetworkReachable(string host, int timeout = 5000)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentOutOfRangeException.ThrowIfNegative(timeout);

        try
        {
            using (var ping = new Ping())
            {
                var reply = ping.Send(host, timeout);
                return reply.Status == IPStatus.Success;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 异步检测网络连通性（ICMP 等待由 <paramref name="timeout"/> 与取消令牌共同约束，不阻塞线程池线程）。
    /// </summary>
    /// <param name="host">目标主机地址</param>
    /// <param name="timeout">超时时间（毫秒），默认5000毫秒</param>
    /// <param name="cancellationToken">取消令牌（与超时联动约束等待上限）</param>
    /// <returns>如果网络连通，返回true；否则返回false</returns>
    /// <exception cref="ArgumentException">当host为null或空字符串时抛出此异常</exception>
    /// <exception cref="ArgumentOutOfRangeException">当timeout小于0时抛出此异常</exception>
    public static async Task<bool> IsNetworkReachableAsync(string host, int timeout = 5000, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentOutOfRangeException.ThrowIfNegative(timeout);
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        using (var ping = new Ping())
        using (var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCancellationTokenSource.CancelAfter(timeout);
            try
            {
                var reply = await ping.SendPingAsync(host, timeout).WaitAsync(timeoutCancellationTokenSource.Token);
                return reply.Status == IPStatus.Success;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 获取本机MAC地址列表
    /// </summary>
    /// <returns>MAC地址字符串列表</returns>
    public static List<string> GetMacAddresses()
    {
        var macAddresses = new List<string>();
        try
        {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var network in networkInterfaces)
            {
                // 排除环回接口和非活动接口
                if (network.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    network.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                var macAddress = network.GetPhysicalAddress().ToString();
                if (!string.IsNullOrEmpty(macAddress))
                {
                    // 格式化MAC地址为标准格式 (XX:XX:XX:XX:XX:XX)
                    macAddresses.Add(FormatMacAddress(macAddress));
                }
            }
        }
        catch
        {
            // 发生异常时返回空列表
        }

        return macAddresses;
    }

    /// <summary>
    /// 将物理地址字符串格式化为 XX:XX:XX:XX:XX:XX 形式。
    /// </summary>
    /// <remarks>
    /// 奇数长度（部分虚拟/蓝牙网卡返回 5.5 字节形态）左侧补一个 0，
    /// 保证最后一个字节不被静默丢弃。
    /// </remarks>
    /// <param name="macAddress">物理地址原始十六进制字符串</param>
    /// <returns>以冒号分隔的字节序列</returns>
    internal static string FormatMacAddress(string macAddress)
    {
        ArgumentException.ThrowIfNullOrEmpty(macAddress);

        var paddedMacAddress = macAddress.Length % 2 == 0 ? macAddress : "0" + macAddress;
        var groups = new string[paddedMacAddress.Length / 2];
        for (var i = 0; i < groups.Length; i++)
        {
            groups[i] = paddedMacAddress.Substring(i * 2, 2);
        }

        return string.Join(":", groups);
    }

    /// <summary>
    /// 验证IP地址是否在指定的子网内
    /// </summary>
    /// <param name="ipAddress">要验证的IP地址</param>
    /// <param name="networkAddress">网络地址</param>
    /// <param name="subnetMask">子网掩码</param>
    /// <returns>如果IP地址在子网内，返回true；否则返回false</returns>
    /// <exception cref="ArgumentException">当IP地址格式无效时抛出此异常</exception>
    public static bool IsIpInSubnet(string ipAddress, string networkAddress, string subnetMask)
    {
        ArgumentException.ThrowIfNullOrEmpty(ipAddress);
        ArgumentException.ThrowIfNullOrEmpty(networkAddress);
        ArgumentException.ThrowIfNullOrEmpty(subnetMask);

        if (!IPAddress.TryParse(ipAddress, out var ip) ||
            !IPAddress.TryParse(networkAddress, out var network) ||
            !IPAddress.TryParse(subnetMask, out var mask))
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.NetworkModule.InvalidIpAddressFormat));
        }

        var ipBytes = ip.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        if (ipBytes.Length != networkBytes.Length || ipBytes.Length != maskBytes.Length)
        {
            return false;
        }

        for (var i = 0; i < ipBytes.Length; i++)
        {
            if ((ipBytes[i] & maskBytes[i]) != (networkBytes[i] & maskBytes[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 获取指定范围内的可用端口列表
    /// </summary>
    /// <param name="startPort">起始端口号</param>
    /// <param name="endPort">结束端口号</param>
    /// <param name="maxCount">最大返回数量，默认为10</param>
    /// <returns>可用端口号列表</returns>
    /// <exception cref="ArgumentOutOfRangeException">当端口范围无效时抛出此异常</exception>
    public static List<int> GetAvailablePorts(int startPort, int endPort, int maxCount = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startPort, 65535);
        ArgumentOutOfRangeException.ThrowIfLessThan(endPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endPort, 65535);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startPort, endPort);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);

        var availablePorts = new List<int>();
        var usedPorts = PortIsUsed().ToHashSet();

        for (var port = startPort; port < endPort && availablePorts.Count < maxCount; port++)
        {
            if (!usedPorts.Contains(port))
            {
                availablePorts.Add(port);
            }
        }

        return availablePorts;
    }

    /// <summary>
    /// 获取本机的公网IP地址
    /// </summary>
    /// <param name="timeout">超时时间（毫秒），默认10000毫秒</param>
    /// <param name="cancellationToken">取消令牌（与 HttpClient 超时联动约束每次请求）</param>
    /// <returns>公网IP地址字符串，获取失败返回null</returns>
    /// <exception cref="ArgumentOutOfRangeException">当timeout小于0时抛出此异常</exception>
#nullable enable
    public static async Task<string?> GetPublicIpAddressAsync(int timeout = 10000, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeout);

        var services = new[]
        {
            "https://api.ipify.org",
            "https://icanhazip.com",
            "https://ipinfo.io/ip",
        };

        using (var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeout), })
        {

            foreach (var service in services)
            {
                try
                {
                    var response = await httpClient.GetStringAsync(service, cancellationToken);
                    var ip = response.Trim();
                    if (IsValidIpAddress(ip, out _))
                    {
                        return ip;
                    }
                }
                catch
                {
                    // 尝试下一个服务
                    continue;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 获取指定域名的IPv4地址
    /// </summary>
    /// <param name="domainName">域名</param>
    /// <returns>IPv4地址字符串，获取失败返回空字符串</returns>
    [Obsolete("同步 DNS 解析会无限期阻塞调用线程，请改用 GetHostIPv4Async。Sync DNS resolution blocks the calling thread indefinitely; use GetHostIPv4Async instead.", false)]
    public static string GetHostIPv4(string domainName)
    {
        var iPHostEntry = Dns.GetHostEntry(domainName);
        foreach (var address in iPHostEntry.AddressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return address.ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 异步获取指定域名的IPv4地址（DNS 解析受内建超时与取消令牌共同约束，不阻塞线程池线程）。
    /// </summary>
    /// <param name="domainName">域名</param>
    /// <param name="cancellationToken">取消令牌（与内建超时联动约束 DNS 解析等待上限）</param>
    /// <returns>IPv4地址字符串，获取失败返回空字符串</returns>
    public static async Task<string> GetHostIPv4Async(string domainName, CancellationToken cancellationToken = default)
    {
        var addressList = await ResolveHostAddressesAsync(domainName, cancellationToken);
        foreach (var address in addressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return address.ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 获取指定域名的IPv6地址
    /// </summary>
    /// <param name="domainName">域名</param>
    /// <returns>IPv6地址字符串，获取失败返回空字符串</returns>
    [Obsolete("同步 DNS 解析会无限期阻塞调用线程，请改用 GetHostIPv6Async。Sync DNS resolution blocks the calling thread indefinitely; use GetHostIPv6Async instead.", false)]
    public static string GetHostIPv6(string domainName)
    {
        var iPHostEntry = Dns.GetHostEntry(domainName);
        foreach (var address in iPHostEntry.AddressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return address.ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 异步获取指定域名的IPv6地址（DNS 解析受内建超时与取消令牌共同约束，不阻塞线程池线程）。
    /// </summary>
    /// <param name="domainName">域名</param>
    /// <param name="cancellationToken">取消令牌（与内建超时联动约束 DNS 解析等待上限）</param>
    /// <returns>IPv6地址字符串，获取失败返回空字符串</returns>
    public static async Task<string> GetHostIPv6Async(string domainName, CancellationToken cancellationToken = default)
    {
        var addressList = await ResolveHostAddressesAsync(domainName, cancellationToken);
        foreach (var address in addressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return address.ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 以内建默认超时约束的异步 DNS 解析（外部取消令牌与超时联动，解析端黑洞不会无限悬挂调用方）。
    /// </summary>
    /// <param name="host">主机名</param>
    /// <param name="cancellationToken">外部取消令牌</param>
    /// <returns>解析到的地址列表</returns>
    private static async Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken)
    {
        using (var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCancellationTokenSource.CancelAfter(DefaultDnsTimeoutMilliseconds);
            return await Dns.GetHostAddressesAsync(host, timeoutCancellationTokenSource.Token);
        }
    }

    /// <summary>
    /// 判断IP地址是否为私有地址
    /// </summary>
    /// <param name="ipAddress">IP地址字符串</param>
    /// <returns>如果是私有地址，返回true；否则返回false</returns>
    /// <exception cref="ArgumentException">当IP地址格式无效时抛出此异常</exception>
    public static bool IsPrivateIpAddress(string ipAddress)
    {
        ArgumentException.ThrowIfNullOrEmpty(ipAddress);

        if (!IsValidIpAddress(ipAddress, out var ip))
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Utility.NetworkModule.InvalidIpAddressFormat), nameof(ipAddress));
        }

        var bytes = ip.GetAddressBytes();

        // IPv4私有地址范围
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 127.0.0.0/8 (环回地址)
            if (bytes[0] == 127)
            {
                return true;
            }
        }

        return false;
    }
}
