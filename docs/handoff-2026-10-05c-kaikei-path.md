# 可开黑关键路径（2026-10-05 续接会话 · 已核实）

> 目标：**两台同局域网设备靠房间码建房-加入，同步可见彼此，走完进局→取证→撤离，不黑屏不崩。**
> 本文件只记「已核实的事实 + 缺口 + 改动点」，避免下一轮重新侦察。

## 0. 门禁现状（本轮已恢复全绿）

| 门禁 | 结果 |
|---|---|
| `bash tools/unity-syntax-check.sh` | **exit=0**（真实错误 56 → 12 → 0） |
| `bash tools/unity-tests.sh EditMode` | **total=45 passed=45 failed=0**，XML 落在 `_evidence/unity-tests/EditMode.xml` |

本轮为此新增两个可重跑脚本（都在 `tools/`）：
- `dedupe-unity-stubs.mjs` —— 修 `UnityStubs.cs` 里 6 个类型被定义两次（`CS0101`+`CS0111`）。
  成因：历史 `fix-stubs-*` 批脚本**往文件末尾追加**了更新的一整套桩，没删中部旧版贫桩。
  做法是**并集合并**（旧版独有成员 `GetTemporary`/`SetPixels32`/`TextureWrapMode`/`TextureFormat.Alpha8`/`Rect.Contains` 全部并入新版），不是删一套。
  备份 `native/unity-stubs/UnityStubs.cs.bak-dedupe`。
- `add-missing-render-stubs.mjs` —— 补齐 15 个缺失入口（`Vector4`、`Mathf.Pow/Log/SmoothStep`、
  `Camera.allowHDR/depthTextureMode`、`Material.SetVector`、`Light.renderMode`+`LightRenderMode`、
  `Canvas.scaleFactor`（宿主是**单行类**，脚本会先展开）、`Texture2D.Apply(bool)/GetPixels32/EncodeToPNG`、
  `RenderTexture.GetTemporary(4参)`），并顺带删掉重复的 `CanvasScaler`。
  每个入口都带官方出处注释；幂等，可重复跑。

## 1. ⛔ 开黑头号阻塞：装的是**本机回环桩**

`unity/Assets/Scripts/Runtime/GameBootstrap.cs:598`
```csharp
Services.Install(new LocalNetService(tickRate));   // ← 本机桩：不跨进程、不联网
```
真实的 `UdpV6NetService`（`Net/UdpV6NetService.cs`，21 KB，IPv6 直连 + 房间码内嵌主机地址 + 4 人 + 带宽预算）
**已经写好且与 `LocalNetService` 同契约**（`TryHost(roomCode)` / `TryJoin(roomCode)` / `SendLocalPlayer(in PlayerSnapshot)`），
但**没有任何代码路径会构造它**。

**改动点（约 20 行）**：`TryInstallServices` 按「是否联机 / 有无房间码」选实现：
- 单人 → `LocalNetService`（现状）
- 建房（主机）→ `new UdpV6NetService(...)` + `TryHost(roomCode)`，并把 `roomCode` 显示在菜单板右上角
- 加入 → `new UdpV6NetService(...)` + `TryJoin(roomCode)`
需要一个「房间码」输入位（目前 `GameBootstrap` 上没有这个字段）。

## 2. ⛔ 开黑二号阻塞：**远端玩家没有可见实体**

已核实：`PlayerSnapshot`（`Core/Contracts/MatchState.cs:19`，含 `PlayerId/X/Y/Z/Yaw/IsLocal/Sanity01`）
只在联机服务内部 `_players` 列表里增删改（`LocalNetService.cs:81`、`UdpV6NetService.cs:386`），
**没有任何代码把它渲染成世界里的实体** —— 见 `Runtime/` 下只有 `MonsterViews.cs`（怪），没有 `PlayerViews` 之类。
后果：**两台机器连上也只能"看不见彼此"**。

**改动点**：新增 `Runtime/PlayerViews.cs`（照 `MonsterViews.cs` 的写法）：
每帧读 `Services.Net` 的玩家列表 → 为每个 `IsLocal == false` 的玩家建/更新一个胶囊+头（或复用玩家身体构建器）→
按 `X/Y/Z/Yaw` 摆位插值。这是"可开黑"的**验收核心项**。

## 3. 菜单入口现状（新菜单板 vs 旧 UI）

`MenuScene.cs`：
- **新菜单板**（`BoardItemTitles`，6 项）：`case 1: OnOption(5, "多人联机")` → 只复用旧的「建房间」动作，**不开局**；
  `case 0` 单人 → `StartMatch()`。
- **旧 UI**（`OnOption(int,string)` 的 `case 5` 建房 / `case 6` 加入）仍在，但旧层按用户指令**要被废弃**。
- 缺：**输入房间码加入**的 UI、**房间码展示**（官方在菜单板右上角）。

## 4. 多地图（用户本轮追加要求）

已核实：**只有一张图**，且是硬编码。
- `GameBootstrap.cs:35` `public string LevelResourcePath = "Levels/asylum_v1";`（`TryLoadLevel` 在 :612 按它 `Resources.Load`）
- `unity/Assets/Levels/` 下只有 `asylum_v1.json`（15 KB）
- `tools/gen-asylum-v1.mjs`（21.6 KB）只有**一份布局表**，输出路径也硬编码（:345 `OUT = unity/Assets/Levels/asylum_v1.json`），**不吃命令行参数**
- `data/config.json` 顶层**没有** maps/levels 注册表（只有 `level` 段放 matchSeconds / extraction / eventPool 等全局规则）

**改动点（建议顺序）**：
1. `config.json` 加 `maps` 注册表：`[{ id, name, resource, sizeHint, floorCount, kitPrefix }]`（并同步三份镜像：`node tools/force-sync-config.mjs`）
2. `gen-asylum-v1.mjs` 参数化：`--layout <name> --out <file>`，布局表按图拆分（第二张图建议做**小图**，官方最小图是 6 Tanglewood Drive 那种单层住宅，与现有三层疗养院形成大小对比）
3. `GameBootstrap.LevelResourcePath` 改为按 `mapId` 解析；`BootSmokeTests.cs:65` 断言 `asylum_v1`（改默认值时要同步该断言，注意 45/45 不能掉）
4. 菜单板左侧**地图选项板**（`HallScene` 已有静态 `MapBoardText`）做成可点：主机选定，客户端只读显示
5. 联机：主机把 `mapId` 放进开局握手（`WireFormat` 已有定长批 + 握手阶段；地图 id 只需 1 字节槽位索引），客户端按收到的 id 加载同一张图

## 5. 下一步（按依赖顺序，不要跳）

1. **GameBootstrap 换真联机服务**（§1）+ 房间码字段 → 出包 `bash tools/build-android.sh <N>`（当前已到 0.1.71，下一版 0.1.72）
2. **房间码 UI**：建房后显示在菜单板右上角；加"输入房间码加入"面板
3. **PlayerViews**（§2）→ 两台设备实测：能看到彼此移动
4. **多地图**（§4）：注册表 + 生成器参数化 + 第二张图 + 选图 UI + 联机传 mapId
5. 之后才回到 supplement §4 的 UI 收尾与 §8 货车

## 6. 本机纪律（本轮踩坑补充）

1. **`node -e` 内联在中文路径 + PowerShell 下会因引号层数静默崩**（本轮崩过一次，替换只做一半）→ 一律落成 `.mjs` 再跑。
2. **`write`/`edit` 对中文路径可能报 `ReplaceFileW EIO (Win32 1175)` 且文件停在旧版本** → 先写 ASCII 路径（如 `D:\dshr\...`）再拷回。
3. **PowerShell 控制台中文乱码**（`✓`→`鉁?`）→ 核对文件一律用 `read` 或读回文件。
4. **`adb exec-out screencap -p > x.png` 在 Windows 会损坏 PNG** → 用 `adb shell screencap -p /sdcard/x.png` + `adb pull`。
5. **桩文件的宿主类可能是单行**（`public class Canvas : Behaviour { ... }`）→ 块定位脚本必须先处理单行类。
