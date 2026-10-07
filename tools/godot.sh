#!/data/user/0/app.dsh.mobile/files/engine/bin/bash
# godot.sh — 宿主侧 Godot 转发器（Android/bionic 上跑 glibc 的 Godot，走 proot）
#
# ══════════════════════════════════════════════════════════════════════════════
# 为什么需要这层（2026-10-07 打通，用于 Unity → Godot 的迁移）
# ══════════════════════════════════════════════════════════════════════════════
# Godot 官方只提供 glibc 的 linux 构建（`/lib/ld-linux-aarch64.so.1`），
# 而 Android 用 bionic ⇒ **宿主上直接跑是 "required file not found"**（实测）。
# 本机已有的 glibc 环境是 `~/ubuntu2604` rootfs（Blender 也跑在里面）⇒ 复用同一套 proot。
#
# ══════════════════════════════════════════════════════════════════════════════
# 关键：**Java 必须能跑**，而它是 bionic 的
# ══════════════════════════════════════════════════════════════════════════════
# Godot 导出 Android APK 需要 `keytool` / `apksigner`（Java 程序）。
# 本机的 JDK（扩展 `openjdk-17`）是 **Android/bionic 构建**
#   `ELF ... dynamic (/system/bin/linker64), for Android 24, built by NDK r29`
# 而 Godot 在 glibc 的 proot 里 ⇒ 跨 libc。
#
# 实测可行的组合（三次试错的结果，别改动这几项）：
#   ① bind `/system` `/vendor` `/apex` —— 让 bionic 的 `linker64` 与系统库可见；
#      否则报 `failed to find generated linker configuration from /linkerconfig/ld.config.txt`
#   ② `LD_LIBRARY_PATH` 必须指向**扩展的 lib/**（`extensions/openjdk-17/lib`），
#      **不能**指向 JVM 自己的 `lib/` —— 后者里没有 `libz.so.1`，会报
#      `library "libz.so.1" not found: needed by .../libjli.so`
#   ③ `JAVA_HOME` 指向 `.../lib/jvm/java-17-openjdk`
#
# ⚠ **第三次同类坑：glibc 的 .NET 运行时**
# Godot 的 C# 支持靠 `hostfxr` 加载 .NET。而宿主自带的 .NET（`native/dotnet/root/dotnet`）是
# **Android/bionic**（`/system/bin/linker64`）⇒ 与 Godot 的 glibc 环境**跨 libc，用不了**。
# 现象：`Unable to load .NET runtime, specifically hostfxr` → `handle_crash: signal 11`。
# ⇒ 必须装 **glibc 的 .NET 8 runtime** 到 rootfs：`/usr/share/dotnet`（已装 8.0.11）。
# 这是本链路第三个"宿主工具是 bionic、Godot 在 glibc 里"的坑（前两个：JDK、Android SDK 工具）。
#
# ⚠ **第六次跨环境坑：JDK 扩展的 `libz.so.1` 遮蔽了 glibc 的**
# 现象（Godot 导出日志原文）：
#   `/usr/share/dotnet/.../libSystem.IO.Compression.Native.so: cannot open shared object file`
#   `SDK resolver "Microsoft.DotNet.MSBuildWorkloadSdkResolver" returned null`
#   → `error MSB4236: The SDK 'Godot.NET.Sdk/4.4.0' specified could not be found.`
# 因果链：NuGet 的 SDK 解析器要用 **IO.Compression**（解压 nupkg）→ 它依赖 **libz** →
#   而 `LD_LIBRARY_PATH` 里 **JDK 扩展的 `lib/` 排在最前**，那里是 **bionic 的 `libz.so.1`** →
#   glibc 进程加载 bionic 的 libz 失败 ⇒ 压缩库起不来 ⇒ 解析器返回 null ⇒ 找不到 SDK。
# ⇒ **glibc 的库路径必须排在扩展 lib 之前**（JDK 自身有 rpath，不依赖 LD_LIBRARY_PATH）。
# 这一条与前面"libz 找不到"是同一个库的**两个相反方向**：
#   放太前 → 遮蔽 glibc 的；不放了 → Java 找不到。最终顺序：glibc 优先，扩展垫底。
#
# ⚠ **第五次跨环境坑：libssl —— 最终解法是把 OpenSSL 1.1 放在最前面**
# .NET 8 的 native shim **硬要 OpenSSL 1.1 的符号**（strings 查到的原文）：
#   `Cannot get required symbol CMAC_CTX_copy from libssl`
#   `Cannot get required symbol HMAC_CTX_copy from libssl`
#   `Cannot get required symbol EC_POINT_get_affine_coordinates_GFp from libssl`
#   `Cannot get required symbol OBJ_ln2nid from libssl`
# 而这些符号**在 OpenSSL 3 已被移除**；rootfs 是 Ubuntu 26.04 + OpenSSL 4 ⇒ 落差最大。
# 我按顺序排除过：文件缺失(❌有) → LD_LIBRARY_PATH(❌) → 兼容名(❌) →
#   CLR_OPENSSL_VERSION_OVERRIDE(❌) → dlopen 失败(❌**能成功**，Python ctypes 实测) → 符号(✅)
# ⇒ **`/opt/ssl11` 放 OpenSSL 1.1 的 libssl/libcrypto，并置于 LD_LIBRARY_PATH 最前**。
#   它只给 .NET 用，**不动系统的 OpenSSL 3/4**（其他程序仍走系统路径）。
#
# ⚠ **第五次坑的原始记录：libssl 缺失**
# .NET 的加密栈硬依赖 `libssl`，而 proot 里报：
#   `No usable version of libssl was found` → `Aborted`（SIGABRT，dotnet publish 直接死）
# 而 rootfs **有** `libssl.so.3`（真实 ELF，8 月的文件）⇒ 不是缺件，是**解析不到**。
# 根因：上面为 Java 设的 `LD_LIBRARY_PATH` **只含 JDK 扩展的 lib（bionic 库）**，
#   把 glibc 的默认搜索路径挤掉了。⇒ 必须把 `/usr/lib/aarch64-linux-gnu` 等补回去。
#
# ⚠ **第四次跨环境坑：CoreCLR 的 GC 堆预留**
# 装好 glibc .NET 后仍失败，报：
#   `GC heap initialization failed with error 0x8007000E`（= E_OUTOFMEMORY）
#   `Failed to create CoreCLR`
# 而本机内存充足（15.7 GB 总 / 5.5 GB 可用、ulimit 不限）
# ⇒ 是 CoreCLR **默认预留的 GC 区域在 proot 的地址空间布局下映射失败**。
# ⇒ 用环境变量把它的预留压小：`GCHeapHardLimit=0x10000000`(256MB) + 关 server/concurrent GC。
#
# ⚠ `which` / `ls` 在 proot 里会**崩**（rust-coreutils 的 auxv 读取与 proot 不兼容：
#   `panicked at rustix/src/backend/linux_raw/param/auxv.rs`）。用 `command -v` 与 shell 内建。
set -u

PREFIX=/data/user/0/app.dsh.mobile/files/engine
ROOTFS=/data/user/0/app.dsh.mobile/files/ubuntu2604
# ⚠ **必须用 .NET(mono) 版**：标准版不含 C# 运行时，跑不了 `godot/Whisper`（C# 工程）。
#   本机实测：标准版 `--headless --build-solutions` 不可用；mono 版带 `GodotSharp/` API 程序集。
GODOT_BIN=/data/user/0/app.dsh.mobile/files/dsh-home/whisper/tmp/godot/mono/Godot_v4.4-stable_mono_linux.arm64
EXT="$PREFIX/extensions/openjdk-17"
JAVA_HOME_R="$EXT/lib/jvm/java-17-openjdk"
ANDROID_EXT="$PREFIX/extensions/android-buildtools"

export PROOT_TMP_DIR="$PREFIX/tmp"
export PROOT_LOADER="$PREFIX/libexec/proot/loader"
# 关键：proot 的 seccomp 加速与某些 guest 初始化冲突（Blender 的 Python 实测被 SIGKILL）
export PROOT_NO_SECCOMP=1

exec "$PREFIX/bin/proot" --link2symlink -0 -r "$ROOTFS" \
  -b /dev -b /proc -b /sys \
  -b /system -b /vendor -b /apex \
  -b /storage/emulated/0 \
  -b /data/user/0/app.dsh.mobile/files \
  -b "$PREFIX/extensions/openjdk-17" \
  -b "$PREFIX/extensions/android-buildtools" \
  -b /data/user/0/app.dsh.mobile/files/dsh-home/whisper/tmp/godot \
  -w /root \
  /usr/bin/env -i \
    HOME=/root \
    PATH="$JAVA_HOME_R/bin:$ANDROID_EXT/bin:/usr/share/dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin" \
    LANG=C.UTF-8 TERM=xterm \
    JAVA_HOME="$JAVA_HOME_R" \
    LD_LIBRARY_PATH="/opt/ssl11:/usr/lib/aarch64-linux-gnu:/usr/lib:/lib/aarch64-linux-gnu:/lib:$EXT/lib" \
    ANDROID_HOME="$ANDROID_EXT" \
    DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_gcServer=0 \
    DOTNET_gcConcurrent=0 \
    DOTNET_GCHeapHardLimit=0x20000000 \
    DOTNET_GCConserveMemory=5 \
    DOTNET_TieredPGO=0 \
    DOTNET_ReadyToRun=0 \
    DOTNET_TieredCompilation=1 \
  "$GODOT_BIN" "$@"
