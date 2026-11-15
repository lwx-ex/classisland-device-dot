using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ClassIslandDeviceDot.Services;

namespace ClassIslandDeviceDot.Controls;

/// <summary>
/// 课表栏用的迷你设备指示器：竖排两个位置，上麦克风、下摄像头。
///
///   - 不占用时**完全隐藏**，什么都不画
///   - 设备刚被占用：对应圆点从中心弹出，带高清黑色图标，放大再回落
///   - 弹出结束（持续占用）就缩回去，等下次再被占用才又弹一下
///
/// 诊断：每次状态变化都写到 %TEMP%\classisland-device-dot.log。
/// 若在 %TEMP% 下建一个空的 classisland-device-dot-test.txt，组件会强制
/// 认为两个设备都被占用并弹出一次，用来验证显示链路是否正常。
/// </summary>
public sealed class DeviceDotControl : Control
{
    // 竖排：上面麦克风，下面摄像头。
    // 真机实测课表行可视高度约 40px，组件若高于它，居中后顶部会被父容器裁掉
    // （50 高时橙点顶部被削平）。所以整体压到 38，并把两颗都收进可视区：
    // 静止半径 6.5、弹跳峰值 8.71，中心 y=10/28，
    // 上边缘 1.29、下边缘 36.71，两圆中心距 18 > 峰值直径 17.42 不重叠。
    public const double BoxWidth = 26;
    public const double BoxHeight = 38;

    private const double DotRadius = 6.5;
    private const double CenterX = BoxWidth / 2;
    private const double MicY = 10.0;
    private const double CamY = 28.0;
    private const double IconFit = 10.0;

    // 0.55 秒太短，用户几乎来不及看见；延长到 1.6 秒仍然保持"持续占用缩回"的语义
    private const double PulseSeconds = 1.6;

    private static readonly Color MicDotColor = Color.Parse("#FF8A1F");
    private static readonly Color CamDotColor = Color.Parse("#22C55E");

    private const string MicAsset = "avares://ClassIslandDeviceDot/Assets/mic.png";
    private const string CamAsset = "avares://ClassIslandDeviceDot/Assets/cam.png";

    private static readonly string TempDir = Path.GetTempPath();
    private static readonly string LogPath = Path.Combine(TempDir, "classisland-device-dot.log");
    private static readonly string TestFlag = Path.Combine(TempDir, "classisland-device-dot-test.txt");

    private readonly DeviceProbe _probe = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _animTimer;

    private bool _micBusy;
    private bool _camBusy;
    private bool _polling;
    private bool _diagnosed;

    private double _micGlow;
    private double _camGlow;

    private double _micPulse = -1;   // <0 表示当前没有在弹
    private double _camPulse = -1;

    private DateTime _lastTick;

    private IImage? _micImage;
    private IImage? _camImage;
    private bool _micAssetTried;
    private bool _camAssetTried;

    public DeviceDotControl()
    {
        ClipToBounds = false;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _pollTimer.Tick += (_, _) => OnPollTick();

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += (_, _) => Tick();
    }

    private static IImage? LoadAsset(string uri)
    {
        try
        {
            // Bitmap(string) 会当成磁盘路径，avares 资源必须先开流
            using var stream = AssetLoader.Open(new Uri(uri));
            return new Bitmap(stream);
        }
        catch (Exception e)
        {
            Log("资源加载失败 " + uri + " : " + e.GetType().Name + " " + e.Message);
            return null;
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
        }
        catch
        {
            // 日志失败绝不能影响界面
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(BoxWidth, BoxHeight);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Log("组件已挂载到主界面，开始轮询（日志: " + LogPath + "）");
        OnPollTick();
        _pollTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _pollTimer.Stop();
        _animTimer.Stop();
        Log("组件已从主界面移除，停止轮询");
        base.OnDetachedFromVisualTree(e);
    }

    // ------------------------------------------------------------ 探测
    // 枚举进程 + 读几十个注册表键要 20~30ms，放 UI 线程会把主界面卡一下
    private async void OnPollTick()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        try
        {
            var (micBusy, camBusy, diagMic, diagCam) = await Task.Run(() =>
            {
                var running = DeviceProbe.RunningProcessNames();
                var mic = _probe.Check(DeviceKind.Microphone, running);
                var cam = _probe.Check(DeviceKind.Camera, running);
                return (mic.Busy, cam.Busy,
                        _probe.Diagnose(DeviceKind.Microphone),
                        _probe.Diagnose(DeviceKind.Camera));
            });

            var force = File.Exists(TestFlag);
            if (force)
            {
                micBusy = true;
                camBusy = true;
            }

            if (!_diagnosed)
            {
                _diagnosed = true;
                Log("首次探测原始记录: " + diagMic);
                Log("首次探测原始记录: " + diagCam);
                Log("测试标记: " + (force ? "已开启（强制弹一次）" : "未开启"));
            }

            var micEdge = micBusy && !_micBusy;
            var camEdge = camBusy && !_camBusy;

            if (micEdge || camEdge || (micBusy != _micBusy) || (camBusy != _camBusy))
            {
                Log($"状态 mic={micBusy} cam={camBusy} (edge mic={micEdge} cam={camEdge})");
            }

            _micBusy = micBusy;
            _camBusy = camBusy;

            // 刚开始占用时弹一下，持续占用不重复弹
            if (micEdge)
            {
                _micPulse = 0;
            }

            if (camEdge)
            {
                _camPulse = 0;
            }

            if (micEdge || camEdge || _micGlow > 0.01 || _camGlow > 0.01)
            {
                StartAnim();
            }

            InvalidateVisual();
        }
        catch (Exception ex)
        {
            Log("轮询异常: " + ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            _polling = false;
        }
    }

    // ------------------------------------------------------------ 动画
    private void StartAnim()
    {
        _lastTick = DateTime.UtcNow;
        if (!_animTimer.IsEnabled)
        {
            _animTimer.Start();
        }
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (dt <= 0 || dt > 0.25)
        {
            dt = 0.016;
        }

        // 出现要快，否则 1.6 秒里前半段几乎看不见
        var follow = Math.Min(1, dt * 20);
        _micGlow += ((_micBusy ? 1 : 0) - _micGlow) * follow;
        _camGlow += ((_camBusy ? 1 : 0) - _camGlow) * follow;

        _micPulse = AdvancePulse(_micPulse, dt);
        _camPulse = AdvancePulse(_camPulse, dt);

        InvalidateVisual();

        var settled = Math.Abs(_micGlow - (_micBusy ? 1 : 0)) < 0.01
                      && Math.Abs(_camGlow - (_camBusy ? 1 : 0)) < 0.01
                      && _micPulse < 0
                      && _camPulse < 0;
        if (settled)
        {
            _animTimer.Stop();
        }
    }

    /// <summary>推进一次弹跳；返回负数表示弹跳结束。</summary>
    private static double AdvancePulse(double value, double dt)
    {
        if (value < 0)
        {
            return -1;
        }

        var next = value + dt / PulseSeconds;
        return next >= 1 ? -1 : next;
    }

    // ------------------------------------------------------------ 绘制
    public override void Render(DrawingContext context)
    {
        // 尺寸固定，直接按内容坐标绘制。不用 Bounds 做居中偏移：
        // 父容器 arrange 出来的尺寸不一定等于 Measure 结果，会推出可视区域。
        DrawDot(context, CenterX, MicY, MicDotColor,
                ref _micImage, ref _micAssetTried, MicAsset, _micGlow, _micPulse);
        DrawDot(context, CenterX, CamY, CamDotColor,
                ref _camImage, ref _camAssetTried, CamAsset, _camGlow, _camPulse);
    }

    private static void DrawDot(
        DrawingContext context,
        double x,
        double y,
        Color dotColor,
        ref IImage? image,
        ref bool assetTried,
        string asset,
        double glow,
        double pulse)
    {
        // 不占用、或者弹出动画已经结束（持续占用）就什么都不画
        if (glow <= 0.01 || pulse < 0)
        {
            return;
        }

        // 资源只读一次，失败就只画圆点，避免每帧重复抛异常
        if (!assetTried)
        {
            assetTried = true;
            image = LoadAsset(asset);
        }

        // 弹跳曲线：两头为 0、中间最大
        var bounce = Math.Sin(pulse * Math.PI) * 0.34;
        var scale = glow * (1 + bounce);

        using (context.PushOpacity(glow))
        {
            // 安卓风格：实心彩色圆点，没有光环
            context.DrawEllipse(
                new ImmutableSolidColorBrush(dotColor),
                null,
                new Point(x, y),
                DotRadius * scale,
                DotRadius * scale);

            if (image is null)
            {
                return;
            }

            var src = image.Size;
            var k = Math.Min(IconFit / src.Width, IconFit / src.Height);
            var w = src.Width * k * scale;
            var h = src.Height * k * scale;
            context.DrawImage(
                image,
                new Rect(x - w / 2, y - h / 2, w, h));
        }
    }
}
