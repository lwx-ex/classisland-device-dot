# 从源码构建

插件必须引用 **目标 ClassIsland 本体自带的 dll**，不能用 NuGet 上的插件 SDK 包编译
（NuGet 包版本与真机脱节，装到真机会报 `FileNotFoundException`）。所以构建前要先准备 dll。

## 1. 准备环境

- **.NET SDK**：net10 包需要 .NET 10 SDK，net8 包需要 .NET 8 SDK。
- **Python 3**：只用来把编译结果打成 `.cipx`（`pack.py`）。

## 2. 准备引用 dll

在你自己的 **目标版本 ClassIsland 安装目录**里，找到下面 4 个文件，拷到本目录的
`ref\ClassIsland\`（该目录已在 `.gitignore` 里，不会上传）：

- `ClassIsland.Core.dll`
- `Microsoft.Extensions.Hosting.Abstractions.dll`
- `Microsoft.Extensions.DependencyInjection.Abstractions.dll`
- `CommunityToolkit.Mvvm.dll`

版本对应关系：

| ClassIsland | 目标框架 | Avalonia | dll 来自 |
|---|---|---|---|
| 2.1.2.0 技术预览版 | `net10.0-windows` | 12.1.4 | 预览版安装目录 |
| 2.1.0.1 稳定版 | `net8.0-windows` | 11.3.17 | 稳定版安装目录 |

## 3. 编译

双击 `build.bat`：它会检查目标框架 → `dotnet build -c Release` → `python pack.py`，
产物在 `cipx\ClassIslandDeviceDot.cipx`。

也可以手动指定 dll 目录（不建 `ref` 时用这个）：

```
dotnet build -c Release -p:ClassIslandDir="D:\ClassIsland\app-2.1.2.0-0"
python pack.py
```

## 4. 打 net8 稳定版包

仓库默认配置是 **net10（预览版）**。要打 net8 稳定版包：

1. 把 `ClassIslandDeviceDot.csproj` 的 `TargetFramework` 改成 `net8.0-windows`，
   `Avalonia` 版本改成 `11.3.17`；
2. `ref\ClassIsland\` 换成从 **2.1.0.1 稳定版**拷来的 4 个 dll；
3. `build.bat` 里的检查串同步改成 `net8.0-windows`；
4. 编译打包后，把这三处改回 net10。
