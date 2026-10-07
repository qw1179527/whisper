# Godot 端 · Whisper 迁移（方案 B：先搬引擎无关层）

> 起因：Unity 一轮出包 **22 分钟**（CI + docker + xvfb + 许可证），
> 而 Godot 在本机 **39 秒**跑通完整出包（见 `tools/godot.sh`）。
> 但 2.3 万行里只有一部分能搬 —— 本目录只放**引擎无关**的那部分。

## 目录

```
godot/
  Whisper/                 引擎无关核心（C# / net8.0 / 零 UnityEngine 引用）
    Whisper.csproj
    Scripts/Core/          契约 + Services + DesignTokens（8 文件）
    Scripts/Gameplay/      配置/关卡/怪物/道具/会话/理智/温度/任务…（39 文件）
  tools/                   本机验证跑手（不依赖 Godot 编辑器）
    VerifyKits.csproj      用纯 C# 解析器核对全部真实套件
```

## 判据（怎么知道"这层真的与引擎无关"）

```bash
bash native/dotnet.sh build godot/Whisper/Whisper.csproj
# 期望：Build succeeded · 0 Warning · 0 Error
```
**能编过 = 这一层不依赖任何引擎。** 换任何宿主（Godot / 裸 .NET / 测试跑手）都能直接编。

## 已完成的验证

| 项 | 命令 | 结果 |
|---|---|---|
| 引擎无关层编译 | `dotnet build godot/Whisper/Whisper.csproj` | **0 警告 0 错误**（47 文件）|
| 真实套件解析 | `dotnet godot/tools/bin/.../verify-kits.dll unity/Assets/ThirdParty/CC0/kits` | **21/21 可解析**（exit 0）|

## 关键移植决策（都写在代码注释里）

1. **`LevelAssembly.cs` 原样搬** —— 它看着像渲染代码，其实**已经是纯计算**：
   产出"装配计划"（墙段 / 门板 / 道具三张表），由 `LevelBuilder` 去建对象。
   实测它的 `UnityEngine` 命中**只在注释里**。
2. **`GlbReader` 不搬，另写 `GlbReaderPure`** —— Unity 版 539 行把顶点解成 `Mesh`、
   材质解成 `Material`（渲染用，换引擎必须重写）。
   但 `KitVerifier` 只要三个计数（部件 / 顶点 / 三角形）⇒ 那部分是纯逻辑。
   `GlbReaderPure` 只做容器解析 + accessor 计数，**并保持 `Model` 与 Unity 版同名同型**
   （我第一版把 `Primitives` 写成 `int`、`VertexCount` 写成字段 ⇒ `model.Primitives.Count`
   编译失败 CS0428/CS0266；**移植的最低要求是调用点零改动**）。
3. **`KitVerifier` 的调用点只改两处**：类型名 `GlbReader.Model` → `GlbReaderPure.Model`、
   `GlbReader.TryRead(` → `GlbReaderPure.TryRead(`。

## 还没搬的（第二阶段，需要 Godot API 重写）

`LevelBuilder`（596 行）· `GlbReader`（539）· `LevelBuilder.Doors`（280）·
`LevelBuilder.LightRig`（264）· `KitMeshLibrary`（250）· `HudBuilder`（129）·
`LightShaft`（163）· `ProceduralTextures`（320）· 以及 `Runtime/` 下全部 UI/场景/控制器（8,967 行）。

## 一个待核实的清单差异

`asset-manifest.json` 声明 **22 个套件**，磁盘上有 **21 个 `.glb`**；
`truck_eurocargo` 在清单里、也在磁盘上（21 个之内）——
差异需在清单门禁里核实（可能是有一个 id 对应非 kits 目录的资源）。

---

## 进度：C# 工程已在 Godot 里编译通过（2026-10-07）

```bash
bash native/dotnet.sh build godot/Whisper/Whisper.csproj
# → Build succeeded.  0 Warning(s)  0 Error(s)
```

工程结构：
```
godot/Whisper/
  project.godot                        Godot 4.4 · C# · mobile 渲染器
  Whisper.csproj                       **不用 Godot.NET.Sdk**（避免 nuget 依赖），
                                       改为 HintPath 直接引用 mono 版自带的 GodotSharp.dll
  Scenes/Main.tscn                     主场景（挂 MainProbe）
  Scripts/Runtime/MainProbe.cs         Godot 端探针：调引擎无关核心 + 画字到屏幕
  Scripts/Core · Scripts/Gameplay      47 个引擎无关文件（搬自 unity/Assets/Scripts）
```

### 为什么不用 `Godot.NET.Sdk/4.4.0`
它要走 nuget 还原，而本机（Termux/bionic + proot）**没验证过 nuget 可达**。
mono 版编辑器**自带 API 程序集**（`GodotSharp/Api/Release/GodotSharp.dll`）
⇒ 直接 `Reference + HintPath`，**零 nuget**，且路径是仓库内相对路径（可复核）。

### 运行时的最后一个缺件：**glibc 的 .NET 运行时**
Godot 的 C# 支持要靠 `hostfxr` 加载 .NET。而：
| | 形态 | 能否给 Godot 用 |
|---|---|---|
| 宿主 `native/dotnet/root/dotnet` | **Android/bionic**（`/system/bin/linker64`）| ❌ Godot 在 glibc 的 proot 里，跨 libc |
| rootfs | **无 .NET** | ❌ |
⇒ 需要 **glibc 的 .NET 8 runtime**：
`https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.11/dotnet-runtime-8.0.11-linux-arm64.tar.gz`（约 34 MB）

装法（拿到后执行）：解到 rootfs 的 `/usr/share/dotnet`，
并给 Godot 传 `DOTNET_ROOT=/usr/share/dotnet` + 把 `/usr/share/dotnet` 加进 `PATH`。

---

## ✅ 运行时验证通过（2026-10-07）

```bash
tools/godot.sh --headless --path .../godot/Whisper --quit-after 90
```
输出（原文）：
```
[Whisper] Godot 4.4-stable (official) · 引擎无关核心探针
  ✓ 关卡 asylum_v1：房间 15 · 走廊 12 · 事件 3
    套件清单 22 个 · 撤离点 entrance_safe
  ✓ 套件 morgue.glb：部件 33 · 顶点 792 · 三角形 396
```

⇒ **搬过来的核心在 Godot 运行时里真的可用**：
`LevelLoader` 解析并校验了真实关卡 JSON，`GlbReaderPure` 解析了真实 GLB。
这不只是"能编译"，是**运行时跑通**。

## 打通运行时又踩了 3 个坑（全部记在这里）

| # | 现象 | 真因 | 修法 |
|---|---|---|---|
| 8 | `Unable to load .NET runtime, specifically hostfxr` | 宿主 .NET 是 **bionic**，Godot 在 glibc 里 | 装 glibc 的 .NET 8 runtime 到 rootfs `/usr/share/dotnet` |
| 9 | `GC heap initialization failed with error 0x8007000E`（=E_OUTOFMEMORY），而内存充足 | CoreCLR 默认预留的 GC 区域在 proot 下映射失败 | `DOTNET_GCHeapHardLimit=0x10000000` + 关 server/concurrent GC |
| 10 | `Failed to load project assembly` | 我为了"零 nuget"绕开了 `Godot.NET.Sdk`（只 HintPath 引用 `GodotSharp.dll`）。**编译能过，但 SDK 还负责产物命名与放置** | 回到官方 `Godot.NET.Sdk/4.4.0` + `nuget.config` 指向 mono 版**自带的 nupkgs**（离线可用） |

### 坑 #10 的教训（值得单独记）
**"能编过"与"能加载"是两件事。** 我用 `Microsoft.NET.Sdk` + `HintPath` 让编译通过，
产物也在 `bin/Debug/net8.0/`，但 Godot 运行时报 `Failed to load project assembly` ——
因为 `Godot.NET.Sdk` 不只是引用程序集，它还管产物形态（`GodotSharp.dll` 随产物、`.deps.json`、目录约定）。

而**离线也不用妥协**：mono 版把 `Godot.NET.Sdk.4.4.0.nupkg` / `GodotSharp.4.4.0.nupkg` /
`Godot.SourceGenerators.4.4.0.nupkg` 全放在 `GodotSharp/Tools/nupkgs/` 里，
`nuget.config` 指过去即可 —— **零联网还原**。
