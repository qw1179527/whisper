# 手机端自出包 · 路径 A 的可行性验证（2026-10-05）

> **结论先行**：Unity 的 **IL2CPP 转换器（`il2cpp-compile.dll`）可以在手机的 .NET 8 上运行** ——
> 已在真机上实测通过（证据见 §三）。这是"手机本地出包、产出与出货等价"这条路上**唯一不可替代的一环**。

---

## 一、这条链是什么

```
① C# 源码 → Assembly-CSharp.dll        需 Unity 引用程序集 + .NET SDK        ✅ 已具备
② IL → C++（IL2CPP 转换）              需 il2cpp 工具链（托管 .NET 程序）      ✅ 本文验证通过
③ C++ → libil2cpp.so                   需 arm64-linux-android sysroot + clang ⬜ 未验证
④ 换进模板 APK + 重签                   需 apksigner / zipalign              ✅ 已具备
```

**为什么值得做**：CI 一轮 26~42 分（构建 26 分 + 产物下载最长 16 分），
而这条链的目标是 **1~3 分**，且**产物是 IL2CPP arm64 —— 与 CI 出的包等价**（能验性能，不只是验逻辑）。
相比之下 Mono 热插拔（路径 B）只能出 32 位开发包。

---

## 二、工具链从哪来

**不能**从 Windows 安装包取 —— 那是 NSIS 格式，7-Zip 在 Linux 与手机上都解不开（实测，见
`.github/workflows/extract-unity-tools.yml` 头部注释里三轮教训）。
**正解**：Unity 给 **Linux** 发的是纯 `TAR_XZ`：

```
https://download.unity3d.com/download_unity/e1dba0a9aba4/LinuxEditorInstaller/Unity-6000.3.25f1.tar.xz
  ↑ 4,536,554,312 字节（可与官方核对）
只取两棵子树：Editor/Data/Managed/* 与 Editor/Data/il2cpp/*
```

取包与提取由 CI 工作流 `extract-unity-tools` 完成（产物 `unity-tools`，约 40 MB）。

---

## 三、三个障碍与排除（**每一步都是报错信息直接指路的**）

复现脚本：`node tools/prepare-il2cpp-for-android.mjs <deploy 目录>`

| # | 报错原文 | 原因 | 修法 |
|---|---|---|---|
| **1** | `libhostpolicy.so is for EM_X86_64 (62) instead of EM_AARCH64 (183)` | `deploy/` 里**自带一整套 x86_64 的 .NET 运行时**（14 个文件），host 优先用它 | 全部挪进 `_x64_native/` |
| **2** | `The application was run as a self-contained app because 'il2cpp-compile.runtimeconfig.json' did not specify a framework` | `runtimeconfig.json` 用的是 `includedFrameworks`（自包含模式） | 改写为 `"framework": {"name":"Microsoft.NETCore.App","version":"8.0.0"}` |
| **3** | `Could not resolve CoreCLR path` | `deps.json` 把**整个 `runtimepack.Microsoft.NETCore.App.Runtime.linux-x64`** 声明为"应用自带资产"；原生文件挪走后它仍按 deps 去 app 目录找 `libcoreclr.so` | 从 **`targets` / `libraries` / `dependencies` 三处**摘掉 runtimepack |

> **注意第 3 条的关键**：只挪文件不够 —— **`deps.json` 也在"声称"运行时是应用自带的**。
> 两处必须一起改，否则报"Could not resolve CoreCLR path"（这个报错**不提示**是 deps 的问题，最费时间）。

---

## 四、决定性证据

排掉三个障碍后，开 `COREHOST_TRACE=1` 跑，轨迹末尾出现：

```
Processing TPA for deps entry [Microsoft.NETCore.App.Runtime.linux-bionic-arm64, 8.0.31, …]
Launch host: /…/native/dotnet/root/dotnet, app: /…/deploy/il2cpp-compile.dll, argc: 0, args:
```

两个关键点：
- **`Microsoft.NETCore.App.Runtime.linux-bionic-arm64, 8.0.31`** ← 用的是**手机自己的**运行时
- **`Launch host`** ← host 解析全部成功并**启动了应用**

**判据说明**：`argc: 0` 时它**静默退 1、不打印任何东西**。
**那不是失败** —— 那是"缺参数"的正常表现（它是 Bee 构建程序驱动，要喂响应文件）。
**判据要看轨迹里的 `Launch host` 行，不能看退出码。**

---

## 五、手机侧的环境事实（都实测过）

| 项 | 值 |
|---|---|
| .NET SDK | **8.0.131**，共享运行时 `Microsoft.NETCore.App 8.0.31`（`linux-bionic-arm64`） |
| `DOTNET_ROOT` | **必须显式设置**（指向 SDK 根），否则 `Could not resolve CoreCLR path` |
| 库依赖 | `libcoreclr.so` 只需 `libm/dl/log/z/c.so` —— **Android 全都有**；`node dlopen` 实测**加载成功** |
| `runtimeconfig` 要求 | `tfm: net8.0` · `Microsoft.NETCore.App 8.0.4` → **与手机的 8.0.31 兼容**（roll-forward） |
| `deps.json` | 无 Windows 专属依赖 |

---

## 六、还没做的（如实登记）

| # | 待办 | 说明 |
|---|---|---|
| 1 | **摸清 `il2cpp-compile` 的参数格式** | 它是 Bee 驱动，要**响应文件**。最省事的来源：**CI 构建日志里 Unity 实际怎么调它的完整命令行**（`#24` 的日志里有 `il2cpp` 字样 266 行，可挖） |
| 2 | **第 ③ 步：C++ → `libil2cpp.so`** | 需 arm64-linux-android 的 **sysroot**（头文件+库）。`libil2cpp` 源码已在 `unity-tools` 里；手机有 `clang 21.1.8`。**未验证** |
| 3 | `global-metadata.dat` 与模板 APK 的匹配 | IL2CPP 产物要换进模板 APK，需确认模板包的结构（`assets/bin/Data/Managed/Metadata/global-metadata.dat` + `lib/arm64-v8a/libil2cpp.so`） |

---

## 七、与其它出包路径的关系

| 路径 | 单次 | 等价出货 | 状态 |
|---|---|---|---|
| **A 手机 IL2CPP 自出包** | 1~3 分（目标） | ✅ 等价 | **② 已通**；③ 待验 |
| **B 手机 Mono 热插拔** | 1~2 分 | 🟡 32 位逻辑等价 | 需一次 CI 出 Mono 模板包 |
| C 灰盒 | 秒级 | ❌ 仅逻辑 | ✅ 已跑通 |
| CI | 26~42 分 | ✅ | ✅ |

**A 与 B 共享同一套前提**（手机 .NET、真引用程序集、apksigner），
所以**推进 A 的过程顺带把 B 的前提也备齐了**。
