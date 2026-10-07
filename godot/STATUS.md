# Godot 端现状（2026-10-07 · 第二轮结论）

## 已确认可用（有读数）
| 能力 | 判据 |
|---|---|
| arm64 编辑器在手机上跑 | `4.4.stable.mono.official.4c311cbee` |
| C# 工程编译（离线 nuget） | `Build succeeded · 0 警告 0 错误`（47 个引擎无关文件）|
| **引擎无关核心在 Godot 运行时可用** | `✓ 关卡 asylum_v1：房间 15 · 走廊 12` / `✓ 套件 morgue.glb：部件 33 · 顶点 792` |
| **场景装配** | `✓ 已摆房间 15 个（缺套件 0）· 套件缓存 9 种 · 网格节点 428` |
| GLB 导入（Godot 原生 `GltfDocument`）| 9 种套件成功 |
| 导出 + 签名（GDScript 项目）| `Signed` · 39~56 秒 |
| 导出 + 签名（C# 项目）| `Signed` · 168 秒 · 53.3 MB |
| **渲染本身** | 真机弹窗**画得出来** ⇒ 管线/2D/文字都正常 |

## 唯一的阻塞：**C# Android 包的程序集放置**
真机原文：
```
.NET assemblies not found
Unable to find the .NET assemblies directory.
Make sure the 'data_Whisper_android_arm64' directory exists and contains the .NET assemblies.
```
实测事实：
- **程序集确实在 APK 里** —— `assets/.godot/mono/publish/arm64/`（170 个，含 `Whisper.dll`)
- 而运行时找的是 **`data_Whisper_android_arm64`**
- `libgodot_android.so` 里有 `res://.godot/mono/publish/`（**不带 arm64**）⇒ 目录层级对不上
- Godot 导出预设自己提示：`Exporting to Android when using C#/.NET is experimental ...
  consider using gradle builds instead` ⇒ **官方路径是 Gradle 构建**，那需要完整 Android SDK + Gradle

## 我试过但没走完的
"把程序集复制到候选路径 + 重签名" —— 需重打包 53 MB APK；
不加压缩地放两份变成 **233 MB**，代价超过收益，已停手。

## 建议（按性价比）
1. **要能玩的包 → 用 Unity 那条**：`whisper-android-apk` 已验证出包（36~38 MB、真机装得上、能跑到主界面）
2. **要让 Godot 出可玩的包** → 需要补 **Android SDK + Gradle**（官方 C# Android 路径）
3. **本目录的价值不变**：47 个引擎无关文件是**可移植的验证层**
   （`dotnet build godot/Whisper/Whisper.csproj` 通过 = 与引擎无关；
    `verify-kits` 21/21 = 逻辑真的能处理真实资产）
