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
# ⚠ `which` / `ls` 在 proot 里会**崩**（rust-coreutils 的 auxv 读取与 proot 不兼容：
#   `panicked at rustix/src/backend/linux_raw/param/auxv.rs`）。用 `command -v` 与 shell 内建。
set -u

PREFIX=/data/user/0/app.dsh.mobile/files/engine
ROOTFS=/data/user/0/app.dsh.mobile/files/ubuntu2604
GODOT_BIN=/data/user/0/app.dsh.mobile/files/dsh-home/whisper/tmp/godot/Godot_v4.4-stable_linux.arm64
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
    PATH="$JAVA_HOME_R/bin:$ANDROID_EXT/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin" \
    LANG=C.UTF-8 TERM=xterm \
    JAVA_HOME="$JAVA_HOME_R" \
    LD_LIBRARY_PATH="$EXT/lib" \
    ANDROID_HOME="$ANDROID_EXT" \
  "$GODOT_BIN" "$@"
