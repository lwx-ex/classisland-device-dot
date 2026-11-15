using Avalonia.Layout;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Components;
using ClassIslandDeviceDot.Controls;

namespace ClassIslandDeviceDot;

/// <summary>
/// 组件默认设置：RelativeLineNumber = 0 表示默认挂在主行，
/// 也就是课表那一行。用户从组件库拖出来时不用再手动换行。
/// </summary>
public class DeviceDotSettings : ComponentSettings
{
    public DeviceDotSettings()
    {
        RelativeLineNumber = 0;
        MainWindowBodyFontSize = 14;
        MainWindowSecondaryFontSize = 12;
        MainWindowEmphasizedFontSize = 15;
        MainWindowLargeFontSize = 17;
    }
}

/// <summary>
/// 主界面组件：橙点 = 麦克风，绿点 = 摄像头。
/// </summary>
[ComponentInfo(
    "3F1B7A64-9C22-4E0D-9B57-6AB4D2F10C85",
    "设备占用指示",
    "\uE412",
    "橙点表示麦克风、绿点表示摄像头，被占用时亮起并弹出提示。")]
public class DeviceDotComponent : ComponentBase<DeviceDotSettings>
{
    public DeviceDotComponent()
    {
        var view = new DeviceDotControl();
        view.Focusable = false;
        Content = view;

        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;

        // 控件自己按文字宽度算尺寸，高度固定，别让容器拉伸
        Height = DeviceDotControl.BoxHeight;
        MinHeight = DeviceDotControl.BoxHeight;
        MaxHeight = DeviceDotControl.BoxHeight;
    }
}
