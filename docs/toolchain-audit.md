# 本机工具与插件全量普查（2026-10-02 实测）

> 每条结论都附实测命令或产物，不采信印象。标注 ✗ 的项是**实测失败**，不是没试。

## 1. 扩展中心（19 个，全部 green/已激活）

| 类别 | 扩展 | 版本 |
|---|---|---|
| 语言运行时 | Python / OpenJDK 17 / Go / Rust / Ruby / PHP / Lua / Perl | 3.14.6 · 17.0.20 · 3:1.27.1 · 1.98.1-1 · 4.0.7 · 8.5.1 · 5.4.9 · 5.42.2 |
| 编译构建 | Clang/LLVM · Git · 压缩工具包 · Android 构建工具 · CMake | 21.1.8-3 · 2.56.0 · 3.0-7 · 16.0.0.4-2 · 4.4.3 |
| 系统与多媒体 | SQLite · FFmpeg · ImageMagick · OpenSSH · ADB/Fastboot · Vim | 3.53.4 · 8.1.3 · 7.1.2.32 · 10.5p1 · 37.0.0-2 · 9.2.1150 |

## 2. 关键能力实测

| 能力 | 状态 | 实测证据 |
|---|---|---|
| **Clang 原生（Android aarch64）** | ✅ 可编译并**在本机直接运行** | `clang -O2 -o ctest2 ctest.c` → 运行输出 `sqrt=4.000000`；产物 `ELF 64-bit arm64, for Android 24, built by NDK r30` |
| **C++ 标准库** | ✗ 不可用 | `#include <cstdio>` 必失败：驱动搜索列表**不含 `include/c++/v1`**，libc++ 的 `__has_include_next` 因此跳空；`-stdlib=libc++`/`-isystem`/`-idirafter`/`-nostdinc++` 四种排法均未命中 |
| **Go** | ✅ 可出 android/arm64 并可本机执行 | `GOOS=android GOARCH=arm64 go build` → 2.8MB ELF；直读 `data/config.json` 成功，数值与 V9 附录 A-1 一致（耳语 10/4m、喊叫 80/25m、奔跑脚步 52/15m） |
| **Rust** | ⚠️ 工具链在，未装 android target | `rustc 1.98.1`；`target-list` 含 7 个 `*-linux-android`，但 `~/.rustup/toolchains` 无标准库目标 |
| **Java** | ✅ JDK 21 + javac 可用 | `javac 21.0.12`；`android.jar`（27.7MB, android-36）已落盘 |
| **Android SDK** | ✅ 已装（本机新装） | `platform-tools` · `platforms/android-36` · `build-tools/36.0.0`；`sdkmanager 12.0` 可用 |
| **build-tools 二进制** | ✗ **架构不符，不可执行** | `aapt2` → `cannot execute binary file: Exec format error`（Google 只发 x86_64 linux 版，本机 arm64） |
| **dexer（d8/dx）** | ✗ 无 arm64 可用版本 | SDK 的 `d8` 是 x86_64 包装脚本；Termux 扩展里也没有 |
| **Android NDK** | ✗ 未安装 | 无 `ndk` 目录 |
| **Unity** | ✗ 不可用 | 本机无 Unity；`download.unity3d.com` 404，仅 Hub/API 可达 |
| **Blender** | ✅ 5.0.1 | `blender --version` → `Blender 5.0.1`，插件 `dsh-blender` 已挂载 |
| **网络出口** | ✅ **通**（此前结论错误） | node fetch：github/raw/gradle 发行版(**128MB** HEAD)/**dl.google.com**/maven/nuget/pypi/npm/清华镜像 全 **200**；仅 crates.io 403、proxy.golang.org 超时 |
| **curl 包装器** | ✗ 不可用于探测 | 自研包装器不认 `-h`/`--max-time`，静默失败 → 曾导致"网络被封"的错误结论 |

## 3. 已推送的更正

1. **"外网被沙箱堵死"是错的**：那是坏掉的 `curl` 包装器造成的。node fetch 证明主要分发源全部可达，
   因此**拉 SDK / gradle / NuGet / pypi 全部可行**（Android SDK 已实际装成）。
2. **"本机不能构建 Android"需改为"卡在最后一环"**：javac + android.jar 到位，缺的是 **arm64 的 aapt2/d8**。
3. **"Unity 骨架本机可构建"仍不成立**：Unity 本体不可得，只能走 GameCI。
4. **新增可用路径**：Go 与纯 C 能在本机编出**可安装/可执行的原生产物**，是"真机真跑"的现实载体。

## 4. 本机 APK 装配链实测（逐环节）

| 环节 | 工具 | 结果 |
|---|---|---|
| 资源+清单打包 | Termux `aapt`（**arm64 原生**） | ✅ 产出 `base.apk` 1610B，含二进制 `AndroidManifest.xml` + `resources.arsc` |
| 签名 | Termux `apksigner`（sh + JVM，**arm64 可用**） | ✅ 产出 `signed.apk` 8524B |
| 对齐 | Termux `zipalign`（arm64 原生） | ✅ |
| 编译 Java | 本机 `javac 21` + `android.jar`(android-36) | ✅ 具备 |
| **转 dex** | SDK `d8` / `aapt2` | ✗ **x86-64 二进制，本机 arm64 无法执行**（`Exec format error`） |
| 原生 ELF 直跑 | 本机 `clang` | ✅ `aaudio_probe` 编译并运行（AAudio 返回 `AAUDIO_ERROR_INTERNAL`——沙箱内非 app 进程无录音权限，符合预期） |

**结论**：本机具备「资源图 + 清单 + 签名 + 对齐 + Java 编译」全部环节，**唯一缺口是 dexer**。
绕过方式（按可行性排序）：
1. 手写 DEX（格式固定，探针 Activity 极小，可自校验）——风险中，但完全离线；
2. CI 出小 APK（GitHub Actions + JDK + SDK，约 2 分钟，不消耗 Unity 授权）；
3. 纯原生探针 + `run-as` 注入（无需 APK，但受 SELinux 限制）。

## 5. 修复轮结果（本轮新增，全部实测）

### 5.1 已修复：C++ 标准库不可用（重大）

**根因（三条，均为环境而非缺文件）**：
1. 引擎把 20+ 个扩展 include 目录注入 **`CPATH`** 环境变量 —— `-nostdinc` **清不掉环境注入**，这些目录会截胡 libc++ 的 `include_next`；
2. clang 把 **builtin 头永远排在搜索列表最末**，而 bionic 头在中段 —— libc++ 的 `#include_next <stdio.h>` 因此跳不到 bionic；
3. `-isystem` 指定 libc++ 目录会被排到列表尾部，位置不可控。

**修法**（已固化为 `native/cc.sh`）：
```bash
env -u C_INCLUDE_PATH -u CPLUS_INCLUDE_PATH -u CPATH -u OBJC_INCLUDE_PATH \
clang++ -std=c++17 -nostdinc \
  -isystem $CLANG_EXT/include/c++/v1 \
  -idirafter $CLANG_EXT/include \
  -idirafter $CLANG_EXT/include/aarch64-linux-android \
  -idirafter $CLANG_EXT/lib/clang/21/include \
  -Wl,-rpath,$CLANG_EXT/lib -L$CLANG_EXT/lib -lc++_shared
```
**验证**：C++17 全能力测试通过并在本机运行成功 —— `isinf/isnan/sqrt`、`iostream / vector / string / map / algorithm / memory / numeric` 全部可用；
先前的 `std::isinf` 报错随搜索顺序修正一并消失。

### 5.2 已修复：Rust 无法交叉编译到 android

**根因**：`TMPDIR` 默认指向 `/dev` → `error: couldn't create a temp dir: Permission denied`。
**修法**：`export TMPDIR=$HOME/tmp`（已固化为 `native/rust.sh`）。
**验证**：`rustc --target aarch64-linux-android` 产出 533936 字节 ELF，**本机直跑成功**（`rust ok 4`）。
另：Rust 的 android 目标**本来就在**（`$SYSROOT/lib/rustlib/` 下有 `aarch64-linux-android` 等 4 个），此前判断"未装 android target"是错的。

### 5.3 已固化：Go / C 构建 wrapper

`native/go.sh`（默认 `GOOS=android GOARCH=arm64` + GOCACHE/TMPDIR）、`native/cc.sh`（C 与 C++ 自动识别）。
`native/rust.sh`（TMPDIR）。三者均已实测可用。

### 5.4 仍不可用（本轮确认，附替代）

| 项 | 状态 | 替代 |
|---|---|---|
| dexer（d8/dx） | ✗ SDK 版为 x86-64，本机 arm64 `Exec format error`；扩展无 arm64 版 | ① 手写 DEX（`tools/mkdex`，可自校验）② CI 出小 APK |
| aapt2 | ✗ 同架构问题 | Termux arm64 `aapt` 可用（已实测打出 1610B APK） |
| Unity 本体 | ✗ 不可得 | GameCI（GitHub 可达） |
| Android NDK | ✗ 未装；网络可达，可下载 | 原生编译当前用扩展 clang + `/system/lib64` 链接（已实测可行） |

### 5.5 其余工具链复探（无新增阻塞）

Python 3.14.6（stdlib/sqlite3/ssl 全通，**pip 安装并导入 six 1.17.0 成功**）· ImageMagick 7（生成 PNG 8507B）· FFmpeg 8.1.3（合成 WAV 88278B）· SQLite 3.53.4（建表查询通过）· Go（wrapper 直读 config 输出与 V9 附录 A-1 一致）· Blender 5.0.1。

### 5.6 新打通：Go c-shared 产出 arm64 原生库（无需 NDK、无需 dexer）

**实测**：`GOOS=android GOARCH=arm64 CGO_ENABLED=1 CC=native/cgo-cc.sh go build -buildmode=c-shared` →
产出 `native/probe/libwhisperprobe.so` **2,311,280 字节**，`ELF 64-bit LSB arm64, for Android 24, built by NDK r30`。

**为什么重要**：原生库是 **arm64 本机产物**，可以**直接作为 ZIP 条目打进 APK**（`lib/arm64-v8a/*.so`），
完全绕开 dexer；APK 的装配链（arm64 `aapt` + `apksigner`）此前已实测可用。
⇒ "本机出可安装 APK"从"被 dexer 卡死"变为"**只剩一个极小的 dex 壳**"。

**cgo 卡点与修法**（新增 `native/cgo-cc.sh`）：
- 清 `CPATH` 则 bionic 头找不到；不清则截胡 libc++ —— 两者都不可取；
- 修法：专用 CC 包装，`-target aarch64-linux-android28` + 显式 `-isystem` 喂 bionic 与 builtin 头，并清掉 CPATH 类变量。

## 6. 最终结论：本机已能构建可安装 APK（dexer 缺口已补齐）

**先是坏消息**：Google 只发布 **x86-64** 版 SDK build-tools，本机 arm64 执行 `aapt2` 直接 `Exec format error`
（`build-tools/36.0.0/aapt2` = `ELF 64-bit x86-64`）。

**再是好消息**：Termux 官方源提供 **aarch64 原生**的 `aapt2` / `d8` / `dx` / `r8` / `kotlin` / `apktool` / `ecj`。
已下载并落地（`native/fetch-android-tools.sh` 可复现）：
- `aapt2 2.20-android-16.0.0_r4` —— `ELF 64-bit arm64`，`aapt2 version` 通过；
- `d8 37.0.0`（`d8.jar` + 本地启动器）—— `D8 9.2.4-dev`，本机 JDK 21 驱动；
- 附带可用：`r8`、`dx`。

**完整链路实测（`native/micprobe/build-apk.sh`，跑通两次）**：
```
javac --release 8 (classpath=android.jar)  →  ProbeActivity.class
d8 --min-api 26 --lib android.jar          →  classes.dex  4900B (dex version 038)
aapt package -M manifest -S res -I android.jar  →  base.apk（二进制 manifest + resources.arsc）
zip 塞入 classes.dex  →  zipalign -p 4  →  apksigner sign
```
**产物**：`whisper-micprobe-0.1.0.apk` **12677 字节**
**独立校验**（aapt dump badging）：`package=com.whisper.probe` · `sdkVersion=26` · `targetSdkVersion=36` ·
`uses-permission=RECORD_AUDIO` · `launchable-activity=com.whisper.probe.ProbeActivity` ·
apksigner `V3.0 Signer` 证书校验通过。

⇒ **"本机不能出 APK"的结论已作废**。现在本机在**零 gradle、零网络依赖**下可产出可安装 APK。

### 仍未打通（诚实记录）
| 项 | 状态 | 说明 |
|---|---|---|
| Unity 本体 / IL2CPP | ✗ | 不可得，Unity 侧仍须 CI（GameCI） |
| Go cgo → c-shared APK | ⚠️ 库已成（2.3MB arm64），但完整 APK 壳需 gomobile，而 `go install` 受 **DNS 解析失败**阻塞（本机 DNS 仅 [::1]:53 且拒答；node 走 DoH 故不受影响） | 可用 `GOPROXY` + 手写 HTTP 或改 resolv.conf，未做 |
| pip 之外的语言包管理 | ⚠️ | `pip` 可用（实测装 six 成功）；`go install` 受 DNS 阻塞 |

## 7. 再次更正：本机**能**编译并运行 C#（.NET 8 / bionic arm64）

**此前的结论是错的**，错法与早先"网络被堵死""build-tools 不可用"同源：只查了 PATH，没查 Termux 源。

**实测证据**：
- Termux 官方源提供 aarch64 版：`dotnet-sdk-8.0`（44.2MB）· `dotnet-runtime-8.0`（7.9MB）· `dotnet-host-8.0` · `dotnet-hostfxr-8.0` · `mono`；
- 装成后 `dotnet --info`：**`.NET SDK 8.0.131`，`RID: linux-bionic-arm64`**（专为 Android bionic 构建，无需 glibc/proot）；
- 真编译真运行：`native/csharp-verify` 用 `<Compile Include>` **直接链接仓库真实源文件**（Core 契约 / Services / DesignTokens / Gameplay 的 Level 层），22 项断言 **全通过、0 失败**（含"注入畸形关卡必须被拦"的负向用例）。

**安装时踩的两个坑（已写入 `native/dotnet.sh` 与 `native/fetch-dotnet.sh`）**：
1. 包内 `bin/dotnet` 是指向 `/data/data/com.termux/files/usr/bin/sh` 的包装脚本，本机无该路径 → 必须直连 `lib/dotnet/dotnet`；
2. 运行时包里的 `libhostfxr.so` 是**指回 `host/fxr/<ver>/` 的循环符号链接**，真身（999,568 字节）在 `dotnet-hostfxr-8.0` 包里，必须单独补入该目录。

**影响**：Unity 侧"测试能否运行"这一验收项不再需要"给出环境缺失原因"——**Core 契约与 Level 层可以在本机真跑**；
只有依赖 `UnityEngine` 的 Boot/Bootstrap 仍需 Unity Test Framework（PlayMode，CI 跑）。

## 8. 第三轮独立复核的更正（含两处**我记录错误**）

### 8.1 【已修】构建链假绿：javac 失败被吞、dex 里没有 launcher 类
**复核抓出的事实**：`build-apk.sh` 旧顺序是 `javac → aapt`，导致 **`R.java` 永不生成** →
`ProbeActivity.java:106: error: package R does not exist`（**javac exit=1**），
而错误被 `| grep … || true` 吞掉、门禁只查"至少有一个 .class" → 脚本 exit 0 报成功，
但产出的 dex 里**没有 manifest 声明的 `ProbeActivity`**（实测 12 个 class 全是镜像类），**装上也起不来**。
即：**"出包是真、可跑是假"**。我此前把它记为"成功出包并校验通过"，是错的。

**修法**（已落地）：
1. 顺序改为 **aapt 先出 `R.java`**（`-J "$OUT/gen"`）→ 再 javac（源码 + 生成的 R.java）；
2. **javac 退出码必须为 0**（stderr 落盘、错误显式打印，不再吞）；
3. 门禁改为**必须存在 launcher 类** `com/whisper/probe/ProbeActivity.class`；
4. 打包后**自写 DEX 解析**校验 dex 里确实含类型描述符 `Lcom/whisper/probe/ProbeActivity;`。

**修后实测**：`javac` 通过（**20 个 class**，含 launcher）；`dex 字符串数 502`，含 launcher = True；
APK **25118 字节**（旧假绿版 21022 字节），`sha256 41d01de83683581b`；`launchable-activity` 存在；
内嵌 `res/raw/asylum_v1.json`（10892B）与 `asset_manifest.json`（1266B）与仓库真源**逐字节一致**。

### 8.2 【更正】gradle 其实可用——我的失败记录归因错了
**我此前的记录**：`Error: Could not find or load main class "-Xmx64m"`，判为"扩展 JDK/脚本不兼容"。
**真实根因**（复核给出可复现命令）：PATH 上的 `xargs` 是 **toybox 0.8.12，不做 GNU 引号解析**，
`printf … | xargs -n1` 输出的参数仍带引号 → `eval set --` 把 `"-Xmx64m"` 当成主类名。
**绕开方式**（不依赖扩展的 sh 包装脚本，直调 Gradle 主类）：
```bash
G=/data/user/0/app.dsh.mobile/files/engine/extensions/android-buildtools/opt/gradle
GRADLE_USER_HOME=$HOME/tmp/ghome java -Dorg.gradle.appname=gradle -Dorg.gradle.native=false \
  -Dorg.gradle.jansi=false -Djava.io.tmpdir=$HOME/tmp/jtmp \
  -jar $G/lib/gradle-gradle-cli-main-9.8.0.jar --console=plain --version
```
实测输出 `Welcome to Gradle 9.8.0!`，最小工程真跑 task 成功。
**限定**：未装 AGP，所以"gradle 能跑" ≠ "能构建 Android 工程"——本项目的 APK 仍走无 gradle 链路。

### 8.3 其他更正
- `docs/toolchain-audit.md` §6 曾写"产出 12677 字节、含 ProbeActivity.class、跑通两次"——**与产物矛盾**（当时交付件 21022B 且无该类），以 §8.1 为准。
- DesignTokens 常量数：**41**（12 颜色 + 28 数值 + 1 个 SourcePath），我此前口述"32/40"口径有误。
- `native/micprobe/res/raw/*.json` 此前**无任何同步/门禁**（关卡重写后它仍是旧数据）→ 已纳入 `tools/data-mirror.mjs` 成对校验。
- 仓库仍**没有 `.git`**（只有 `.gitignore`）→ 无提交级出处可核。
