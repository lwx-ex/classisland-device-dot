using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ClassIslandDeviceDot.Services;

/// <summary>设备种类。</summary>
public enum DeviceKind
{
    /// <summary>麦克风</summary>
    Microphone,

    /// <summary>摄像头</summary>
    Camera
}

/// <summary>某一时刻的设备占用状态。</summary>
public readonly record struct DeviceState(bool Busy, string Source, double Seconds)
{
    public static readonly DeviceState Idle = new(false, string.Empty, 0);
}

/// <summary>
/// 读取 Windows 隐私记录判断摄像头 / 麦克风是否正被占用。
///
/// 数据源与任务栏那个"摄像头正在使用"小图标一致：
/// HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\
///     {microphone|webcam}\
///         NonPackaged\&lt;程序路径&gt;\        Win32 程序
///         &lt;PackageFamilyName&gt;\          打包应用（Windows 相机、Teams、Xbox Game Bar 等）
///     每个子键下有 LastUsedTimeStart / LastUsedTimeStop（REG_QWORD，FILETIME）。
/// Start 有值且 Stop 为 0 或早于 Start，即表示正在使用。
///
/// 旧版只扫 NonPackaged，会漏掉全部打包应用；并且对每条记录都做进程名过滤，
/// 一旦进程名对不上（浏览器子进程、服务宿主等）就漏检。现改为：
///   - 两类子键都扫
///   - 5 分钟内的新鲜记录直接信任，只有旧记录才用进程名兜底去残留
/// </summary>
public sealed class DeviceProbe
{
    private const string ConsentRoot =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    private const double MaxAgeHours = 24;    // 挂机会议也要能识别
    private const double FreshMinutes = 5;    // 这个时间内的记录不做进程过滤

    private readonly double _maxAgeHours;

    public DeviceProbe(double maxAgeHours = MaxAgeHours)
    {
        _maxAgeHours = maxAgeHours;
    }

    private static string SubKeyOf(DeviceKind kind) =>
        kind == DeviceKind.Microphone ? "microphone" : "webcam";

    private readonly record struct Record(string Source, long Start, long Stop);

    /// <summary>查询指定设备当前是否被占用。</summary>
    /// <param name="running">运行中的进程名集合（含 .exe），仅用于过滤陈旧残留记录。</param>
    public DeviceState Check(DeviceKind kind, IReadOnlyCollection<string>? running)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                $@"{ConsentRoot}\{SubKeyOf(kind)}");
            if (root is null)
            {
                return DeviceState.Idle;
            }

            var now = DateTime.UtcNow;

            // 1) 打包应用（UWP / MSIX）：直属子键，跳过容器键
            foreach (var rec in Enumerate(root, packaged: true))
            {
                if (IsBusy(rec, now, running, out var state))
                {
                    return state;
                }
            }

            // 2) 传统 Win32 程序：NonPackaged 子键
            using var np = root.OpenSubKey("NonPackaged");
            if (np is not null)
            {
                foreach (var rec in Enumerate(np, packaged: false))
                {
                    if (IsBusy(rec, now, running, out var state))
                    {
                        return state;
                    }
                }
            }
        }
        catch
        {
            // 读不到隐私记录就当空闲，不要因为探测失败去打扰用户
        }

        // 兜底：部分 Win11（尤其预览版）不再把相机使用写入 ConsentStore，
        // 但相机开流时 Windows Camera Frame Server 服务一定在运行。
        // Win10 1903 起也有这个服务；更老的 Win10 没有它，查询返回 false，
        // 行为退化为只看 ConsentStore，与旧版一致。
        if (kind == DeviceKind.Camera && IsServiceRunning("FrameServer"))
        {
            return new DeviceState(true, "FrameServer 服务运行中", 0);
        }

        return DeviceState.Idle;
    }

    // ------------------------------------------------------------ 服务查询
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const int SERVICE_RUNNING = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public int dwServiceType;
        public int dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scManager, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, out SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    /// <summary>查 Windows 服务是否在运行；服务不存在或查询失败都返回 false。</summary>
    private static bool IsServiceRunning(string serviceName)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var svc = OpenService(scm, serviceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return QueryServiceStatus(svc, out var st) && st.dwCurrentState == SERVICE_RUNNING;
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    /// <summary>
    /// 输出该设备下所有记录的原始值与判定，用于排查"检测不了"。
    /// 写到诊断日志里，用户可以直接把日志发回来定位。
    /// </summary>
    public string Diagnose(DeviceKind kind)
    {
        var sb = new StringBuilder();
        sb.Append(SubKeyOf(kind)).Append(':');
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                $@"{ConsentRoot}\{SubKeyOf(kind)}");
            if (root is null)
            {
                return sb.Append(" <根键打不开>").ToString();
            }

            var now = DateTime.UtcNow;
            var all = new List<Record>();
            all.AddRange(Enumerate(root, packaged: true));
            using var np = root.OpenSubKey("NonPackaged");
            if (np is not null)
            {
                all.AddRange(Enumerate(np, packaged: false));
            }

            sb.Append(" 记录=").Append(all.Count);
            foreach (var r in all)
            {
                var busy = IsBusy(r, now, null, out _);
                sb.Append(" | ")
                  .Append(r.Source).Append(" S=").Append(r.Start)
                  .Append(" E=").Append(r.Stop)
                  .Append(busy ? " [占用]" : " [结束]");
            }
        }
        catch (Exception e)
        {
            sb.Append(" <异常 ").Append(e.GetType().Name).Append('>');
        }

        return sb.ToString();
    }

    private static IEnumerable<Record> Enumerate(RegistryKey root, bool packaged)
    {
        foreach (var name in root.GetSubKeyNames())
        {
            if (packaged && (name == "NonPackaged" || name == "ConsentV2Unverified"))
            {
                continue;
            }

            using var sub = root.OpenSubKey(name);
            if (sub is null)
            {
                continue;
            }

            var start = AsLong(sub.GetValue("LastUsedTimeStart"));
            var stop = AsLong(sub.GetValue("LastUsedTimeStop"));
            if (start == 0 && stop == 0)
            {
                continue;
            }

            yield return new Record(packaged ? "PKG:" + name : name.Replace('#', '\\'),
                                   start, stop);
        }
    }

    private bool IsBusy(
        Record rec,
        DateTime now,
        IReadOnlyCollection<string>? running,
        out DeviceState state)
    {
        state = DeviceState.Idle;
        if (rec.Start <= 0)
        {
            return false;
        }

        // Stop 存在且不早于 Start => 这次使用已经结束
        if (rec.Stop > 0 && rec.Stop >= rec.Start)
        {
            return false;
        }

        DateTime since;
        try
        {
            since = DateTime.FromFileTimeUtc(rec.Start);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var ageHours = (now - since).TotalHours;
        // 时钟异常（未来时间）或太陈旧，跳过
        if (ageHours < -0.05 || ageHours > _maxAgeHours)
        {
            return false;
        }

        // 只有陈旧记录才用进程名过滤，防止程序崩溃后 Stop 残留为 0 造成"永远占用"。
        // 新鲜记录一律信任：浏览器/会议软件常由子进程实际占用，进程名未必对得上。
        if (ageHours * 60 > FreshMinutes && running is { Count: > 0 })
        {
            var exe = ExeOf(rec.Source);
            if (!string.IsNullOrEmpty(exe) && !Contains(running, exe))
            {
                return false;
            }
        }

        state = new DeviceState(true, rec.Source, ageHours * 3600);
        return true;
    }

    private static string ExeOf(string source)
    {
        var cut = source.LastIndexOf('\\');
        var exe = cut >= 0 ? source[(cut + 1)..] : source;
        return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : string.Empty;
    }

    private static bool Contains(IReadOnlyCollection<string> set, string exe)
    {
        foreach (var item in set)
        {
            if (string.Equals(item, exe, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>当前所有进程的可执行文件名（带扩展名）。</summary>
    public static HashSet<string> RunningProcessNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    set.Add(process.ProcessName + ".exe");
                }
                catch
                {
                    // 受保护进程取不到名字，跳过
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // 枚举失败时返回空集合，调用方会跳过进程校验
        }

        return set;
    }

    private static long AsLong(object? value) => value switch
    {
        long l => l,
        int i => i,
        uint u => u,
        ulong ul when ul <= long.MaxValue => (long)ul,
        byte[] b when b.Length == 8 => BitConverter.ToInt64(b, 0),
        string s when long.TryParse(s, out var v) => v,
        _ => 0L
    };
}
