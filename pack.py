# -*- coding: utf-8 -*-
"""把编译输出打成 ClassIsland 插件包 .cipx（本质是 zip，改后缀）。"""
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SRC = ROOT / "bin" / "Release" / "net10.0-windows"
OUT_DIR = ROOT / "cipx"
OUT = OUT_DIR / "ClassIslandDeviceDot.cipx"

# 只打插件自身的文件。ClassIsland 本体已经带了 Avalonia / Microsoft.Extensions 等依赖，
# 插件包里再塞一份会和本体版本打架。
ALLOW = {
    "ClassIslandDeviceDot.dll",
    "ClassIslandDeviceDot.deps.json",
    "ClassIslandDeviceDot.runtimeconfig.json",
    "manifest.yml",
    "icon.png",
    "README.md",
}

if not SRC.exists():
    sys.exit(f"编译输出目录不存在：{SRC}")

OUT_DIR.mkdir(exist_ok=True)
if OUT.exists():
    OUT.unlink()

files = [p for p in sorted(SRC.iterdir()) if p.name in ALLOW]
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    for p in files:
        z.write(p, p.name)
        print("  +", p.name)

print(f"\n已生成 {OUT}  ({OUT.stat().st_size / 1024:.1f} KB, {len(files)} 个文件)")
