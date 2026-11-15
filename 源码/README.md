# 设备占用指示点

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

给 [ClassIsland](https://classisland.tech/) 主界面加一个小指示器：**橙点 = 麦克风，绿点 = 摄像头**。

![预览](preview.png)

- 平时是两个安静的灰色实心点；
- 一旦有程序开始用麦克风或摄像头，对应那个点会亮起来、弹一下，然后从圆点里"吐出"一个带图标的胶囊（例如「摄像头使用中」）；
- 停 2.6 秒后缩回成圆点，此时圆点保持本命色（橙 / 绿），表示设备仍在占用；
- 设备释放后，圆点重新变灰。

两个设备同时被占用时，会排队依次弹：先摄像头，再麦克风。

## 它怎么知道设备被占用了

两个数据源互相补位，都不需要驱动、注入或管理员权限：

1. **Windows 隐私记录（ConsentStore）** —— 和任务栏"摄像头正在使用"图标同一个数据源，判定与系统一致：

   ```
   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\
       {microphone|webcam}\...\LastUsedTimeStart / LastUsedTimeStop
   ```

2. **Windows Camera Frame Server 服务** —— 相机开流时该服务必然 RUNNING。
   某些 Win11（含 Dev 预览版）相机不写隐私记录，就靠它兜底。

为了避免程序崩溃后残留的"伪占用"，还做了两道保险：

1. `LastUsedTimeStart` 超过 24 小时的一律忽略；
2. 5 分钟内的新鲜记录直接信任，陈旧记录才用 exe 是否还在运行去验活。

## 安装

1. 从 Releases 下载对应版本的 `.cipx`；
2. 打开 ClassIsland →【应用设置】→【插件】→ 从文件安装；
   （找不到入口的话，把 `.cipx` 解压成同名文件夹，放到 `<ClassIsland 安装目录>\data\Plugins\`）
3. 重启 ClassIsland；
4. 【应用设置】→【组件】，把「设备占用指示」拖到主界面上即可。

### 选哪个包

| 文件 | 适用 ClassIsland | 运行时 |
|---|---|---|
| `ClassIslandDeviceDot.cipx` | 2.1.2.0 技术预览版（2.2 Misha DP3） | .NET 10 / Avalonia 12 |
| `ClassIslandDeviceDot_net8_OLD.cipx` | 2.1.0.1 稳定版 | .NET 8 / Avalonia 11 |

装错版本会加载失败。判断自己的版本：设置 → 关于，或看安装目录的 `app-<版本号>-<n>` 文件夹名。

## 从源码构建

见 [BUILD.md](BUILD.md)：准备引用 dll + 双击 `build.bat` 一步打包。
插件必须引用目标 ClassIsland 自带的 dll，不能用 NuGet SDK 包编译。

## 兼容性

- ClassIsland 2.1.0.1（稳定版）/ 2.1.2.0（技术预览版）
- Windows 10 1903 及以上 / Windows 11

## 许可

[MIT](LICENSE) © 2026 lwx-ex
