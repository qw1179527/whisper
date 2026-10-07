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
