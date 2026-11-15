@echo off
setlocal
cd /d "%~dp0"

echo [check] TargetFramework ...
findstr /C:"<TargetFramework>net10.0-windows</TargetFramework>" ClassIslandDeviceDot.csproj >nul
if errorlevel 1 (
    echo.
    echo ERROR: ClassIslandDeviceDot.csproj 里的 TargetFramework 不是 net10.0-windows。
    echo        ClassIsland 2.1.2.0 预览版是 net10.0，插件必须是 net10.0-windows，
    echo        否则加载失败。请先改回来。
    echo.
    pause
    exit /b 1
)

echo [1/2] dotnet build -c Release ...
dotnet build -c Release
if errorlevel 1 goto fail

echo.
echo [2/2] python pack.py ...
python pack.py
if errorlevel 1 goto fail

echo.
echo Done: cipx\ClassIslandDeviceDot.cipx
pause
exit /b 0

:fail
echo.
echo BUILD FAILED. See messages above.
pause
exit /b 1
