import os
import sys
import shutil
from pathlib import Path

def get_dir_size_fast(path, max_depth=10):
    total = 0
    try:
        for root, dirs, files in os.walk(path):
            # Don't follow symlinks/junctions
            dirs[:] = [d for d in dirs if not os.path.islink(os.path.join(root, d))]
            for f in files:
                fp = os.path.join(root, f)
                try:
                    if not os.path.islink(fp):
                        total += os.path.getsize(fp)
                except (PermissionError, FileNotFoundError, OSError):
                    pass
    except (PermissionError, FileNotFoundError, OSError):
        pass
    return total

def to_gb(b):
    return b / (1024 ** 3)

def to_mb(b):
    return b / (1024 ** 2)

print("=" * 70, flush=True)
print("C: DISK STORAGE SCANNER", flush=True)
print("=" * 70, flush=True)

usage = shutil.disk_usage("C:\\")
print(f"Total: {to_gb(usage.total):.2f} GB | Used: {to_gb(usage.used):.2f} GB ({(usage.used/usage.total)*100:.1f}%) | Free: {to_gb(usage.free):.2f} GB\n", flush=True)

# 1. Root System Files
print("--- 1. Root System Files (C:\\) ---", flush=True)
for rf in ["C:\\pagefile.sys", "C:\\hiberfil.sys", "C:\\swapfile.sys", "C:\\DumpStack.log.tmp"]:
    if os.path.exists(rf):
        try:
            sz = os.path.getsize(rf)
            print(f"  {os.path.basename(rf):25s}: {to_gb(sz):6.2f} GB", flush=True)
        except Exception:
            print(f"  {os.path.basename(rf):25s}: [Locked / In Use]", flush=True)

# 2. Main Top-Level Folders on C:\
print("\n--- 2. Main Directories on C:\\ ---", flush=True)
for md in ["C:\\Users", "C:\\Program Files", "C:\\Program Files (x86)", "C:\\ProgramData", "C:\\Windows"]:
    if os.path.exists(md):
        sz = get_dir_size_fast(md)
        print(f"  {md:25s}: {to_gb(sz):6.2f} GB", flush=True)

# 3. User Home Folders (C:\Users\preet)
user_home = "C:\\Users\\preet"
print(f"\n--- 3. User Folders ({user_home}) ---", flush=True)
items = []
try:
    for entry in os.scandir(user_home):
        try:
            if entry.is_dir(follow_symlinks=False):
                sz = get_dir_size_fast(entry.path)
                items.append((entry.name, sz, True))
            elif entry.is_file(follow_symlinks=False):
                sz = entry.stat().st_size
                items.append((entry.name, sz, False))
        except Exception:
            pass
except Exception:
    pass

items.sort(key=lambda x: x[1], reverse=True)
for name, sz, is_dir in items:
    if to_mb(sz) > 50:
        kind = "[DIR] " if is_dir else "[FILE]"
        print(f"  {kind} {name:25s}: {to_gb(sz):6.2f} GB", flush=True)

# 4. AppData Local Top Subfolders
print(f"\n--- 4. AppData\\Local Top Subfolders ---", flush=True)
app_local = os.path.join(user_home, "AppData", "Local")
local_items = []
if os.path.exists(app_local):
    for entry in os.scandir(app_local):
        if entry.is_dir(follow_symlinks=False):
            sz = get_dir_size_fast(entry.path)
            if to_mb(sz) > 100:
                local_items.append((entry.name, sz))

local_items.sort(key=lambda x: x[1], reverse=True)
for name, sz in local_items[:15]:
    print(f"  {name:30s}: {to_gb(sz):6.2f} GB", flush=True)

# 5. AppData Roaming Top Subfolders
print(f"\n--- 5. AppData\\Roaming Top Subfolders ---", flush=True)
app_roaming = os.path.join(user_home, "AppData", "Roaming")
roaming_items = []
if os.path.exists(app_roaming):
    for entry in os.scandir(app_roaming):
        if entry.is_dir(follow_symlinks=False):
            sz = get_dir_size_fast(entry.path)
            if to_mb(sz) > 100:
                roaming_items.append((entry.name, sz))

roaming_items.sort(key=lambda x: x[1], reverse=True)
for name, sz in roaming_items[:15]:
    print(f"  {name:30s}: {to_gb(sz):6.2f} GB", flush=True)

# 6. Specific Cleanable Temp & Caches
print(f"\n--- 6. Cleanable Temp & Cache Locations ---", flush=True)
cleanable = [
    ("User Temp (AppData\\Local\\Temp)", os.path.join(user_home, "AppData", "Local", "Temp")),
    ("Windows Temp", "C:\\Windows\\Temp"),
    ("User Downloads", os.path.join(user_home, "Downloads")),
    ("Recycle Bin C:", "C:\\$Recycle.Bin"),
    ("Windows Update Downloads", "C:\\Windows\\SoftwareDistribution\\Download"),
    ("Pip Cache", os.path.join(user_home, "AppData", "Local", "pip", "cache")),
    ("UV Cache", os.path.join(user_home, "AppData", "Local", "uv", "cache")),
    ("NPM Cache", os.path.join(user_home, "AppData", "Local", "npm-cache")),
    ("NuGet Packages", os.path.join(user_home, ".nuget", "packages")),
    ("Cargo Cache", os.path.join(user_home, ".cargo")),
    ("Rustup Toolchains", os.path.join(user_home, ".rustup")),
    ("Gradle Cache", os.path.join(user_home, ".gradle")),
    ("CrashDumps", os.path.join(user_home, "AppData", "Local", "CrashDumps")),
    ("DirectX D3DSCache", os.path.join(user_home, "AppData", "Local", "D3DSCache")),
    ("NVIDIA DXCache", os.path.join(user_home, "AppData", "Local", "NVIDIA", "DXCache")),
    ("NVIDIA GLCache", os.path.join(user_home, "AppData", "Local", "NVIDIA", "GLCache")),
    ("Unity Package Cache", os.path.join(user_home, "AppData", "Local", "Unity", "cache")),
    ("Unity Hub Cache", os.path.join(user_home, "AppData", "Roaming", "UnityHub")),
    ("Claude Local Data", os.path.join(user_home, "AppData", "Local", "Claude-Data")),
    ("Claude AppData", os.path.join(user_home, "AppData", "Local", "Claude")),
    ("Antigravity/Gemini Data", os.path.join(user_home, ".gemini")),
]

for label, p in cleanable:
    if os.path.exists(p):
        sz = get_dir_size_fast(p)
        if to_mb(sz) > 10:
            print(f"  {label:35s}: {to_gb(sz):6.2f} GB ({p})", flush=True)

# 7. Large Files in Downloads and Desktop (> 100 MB)
print(f"\n--- 7. Large Files in Downloads and Desktop (> 100 MB) ---", flush=True)
large_files = []
for scan_dir in [os.path.join(user_home, "Downloads"), os.path.join(user_home, "Desktop"), os.path.join(user_home, "Documents")]:
    if os.path.exists(scan_dir):
        for root, dirs, files in os.walk(scan_dir):
            for f in files:
                fp = os.path.join(root, f)
                try:
                    if not os.path.islink(fp):
                        sz = os.path.getsize(fp)
                        if sz > 100 * 1024 * 1024:
                            large_files.append((fp, sz))
                except Exception:
                    pass

large_files.sort(key=lambda x: x[1], reverse=True)
for fp, sz in large_files[:25]:
    print(f"  {to_gb(sz):6.2f} GB : {fp}", flush=True)

# 8. Program Files Top Consumers
print(f"\n--- 8. Program Files Top Consumers ---", flush=True)
for pf in ["C:\\Program Files", "C:\\Program Files (x86)"]:
    if os.path.exists(pf):
        print(f"  [{pf}]", flush=True)
        pf_items = []
        for entry in os.scandir(pf):
            if entry.is_dir(follow_symlinks=False):
                sz = get_dir_size_fast(entry.path)
                if to_mb(sz) > 500:
                    pf_items.append((entry.name, sz))
        pf_items.sort(key=lambda x: x[1], reverse=True)
        for name, sz in pf_items[:10]:
            print(f"    {name:30s}: {to_gb(sz):6.2f} GB", flush=True)

print("\nScan complete!", flush=True)
