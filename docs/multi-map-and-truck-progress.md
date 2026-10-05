# 多地图与货车进展（2026-10-05 · 第 15–16 轮）

> 状态：**三张地图已落地并过全部门禁**；**货车几何与大厅接入已完成，局内功能区待接**。
> 下一个会话照这份继续，不要重新侦察。

## 一、多地图（用户要求「接下来接入多地图选项」）—— **已完成**

| 项 | 事实 |
|---|---|
| 注册表 | `data/config.json` → `maps.list`（3 条，每条带 `_src` 标 design/official） |
| 生成器 | `tools/gen-map.mjs`（通用：几何编译 + 自检 G1–G5）· 布局数据 `tools/lib/map-layouts.mjs` |
| 原生成器 | `tools/gen-asylum-v1.mjs` 已参数化（`--out`），**产物哈希前后一致** `08B8D11050CA2A17` |
| 三张图 | `asylum_v1`（15 房/12 廊/3 层）· `tanglewood_v1`（6 房/5 廊/1 层）· `bleasdale_v1`（7 房/5 廊/**2 层 + 楼梯井**） |
| 门禁 | `validate-levels` exit=0（3 关卡通吃）· `gate-model` M1–M11 全过 · `data-mirror` 7 对逐字节一致 |
| 运行时 | `GameBootstrap.SelectMap(id)` 按注册表解析 `resource`；**未实现/找不到即返回 false，不静默换图** |
| UI | 地图板读注册表 + 数字键 1-9 选图；未实现的图明确拒绝 |

### 生成器自检（G1–G5，产出前判红、不产出半成品）
- **G1** 同层不重叠 · **G2** 门必须落在**两房共享墙**上且开口中心对齐（±0.02m）·
  **G3** 从入口洪水填充全可达（**含竖井跨层边**，与 gate-model M8 同口径）· **G4** 门洞不越界 ·
  **G5** 竖井矩形必须在**它跨越的每一层**都落在某个可走房间内。

### 我在这上面栽的跟头（都值得记）
1. **自检必须先用坏输入验证它会红**：我在布局里故意留了不存在的门引用 → G2 判红且 exit=1、未产出，证明自检有效，然后才改成真门对。
2. **新产物的 schema 必须照抄真源**：走廊写成 `{doorA,doorB,widthM}` 是错的，真源是 `{from,to,doorA,doorB,width}`（**from/to 是房间 id**）；事件也不是 `{id,kind,atRoom}` 而是 `{type,minute,durationSec,params,sanityEffect,counterplay}`（V9 §30.2 强制每个事件有反制手段）。
3. **两层图是耦合约束**：竖井交集 与 同层不重叠 必须**同时**满足。我改一个崩另一个，连改四版（v1→v2→v3）才通。**最终解法：两层同位镜像**——两层共享走廊带 z1..4，竖井取 x6..9,z2..3 天然落在两层走廊内。
4. **`z0/z1` 是最小/最大角**，我读反过一次，白跑一轮。

## 二、货车（用户《补充说明》§8）—— **几何与大厅接入完成，局内待接**

### 已完成
- `unity/Assets/Scripts/Runtime/TruckScene.cs`（新）：整车几何
  - 规格来源：**Iveco Eurocargo 75E18**（真实 75E18：7.5t、轴距 3105mm、整车高约 3.0m）；
    本项目工程近似：驾驶室 2.2×2.1×2.2 · 厢体 2.4×5.4×2.3 · 地板高 0.95 · 坡道 2.2×1.6（**标 design**）
  - **车牌 `7GHD666`**（常量 `PlateNumber`，几何已建）
  - 结构：驾驶室（平头/挡风玻璃/侧窗/保险杠/前照灯）· 厢体（底板/四壁/顶棚/后门框）· 车轮（4×2 四轮）· 坡道 + 键盘 · 车内：指挥区（监控电脑/任务面板/地点地图/两座椅）+ 装备区（左右装备墙 + 三档横架）
  - 公开 API：`WorldBounds` · `CommandCenter` · `GearCenter` · `RampCenter` · `Contains(world)` · `L(world)`
  - 关键零件带名字供局内逻辑接：`Truck_Ramp` · `Truck_MonitorScreen` · `Truck_TaskPanel` · `Truck_MapPanel`
- `HallScene`：新增 `BuildTruck()` + `public TruckScene Truck`（**大厅与局内共用同一实例**，避免两处各建一份漂移）

### 未做（下一轮）
- **局内**：货车作为安全区（鬼无法进入、车内不掉理智）、车尾键盘控坡道升降、主门钥匙生成
- **交互**：监控电脑（切摄像头/夜视）· 任务面板 · 地点地图（切楼层）· 装备墙取用/放回
- **大厅态**：除 "00:00" 计时器外屏幕关闭
- **视觉验证**：货车目前只有门禁级验证（编译通过 + 测试 62/62），**没有渲染截图确认外观**

## 三、门禁状态（本轮末实测）

| 门禁 | 结果 |
|---|---|
| `tools/validate-levels.mjs` | exit=0（3 张关卡 + 镜像逐字节一致） |
| `tools/gate-model.mjs` | 通过 11 路 · 失败 0 |
| `tools/data-mirror.mjs` | 7 对镜像逐字节一致 |
| `tools/unity-syntax-check.sh` | exit=0 |
| `tools/unity-tests.sh EditMode` | **62/62 passed** |
| 交付包 | `whisper-android-0.1.77-code77.apk`（36,140,181 字节）· 先于货车接入，货车需重新出包 |

## 四、教训：材质/字段名不能猜

接货车进大厅时我第一版写了 `_matBody/_matMetal`（凭猜的字段名）——
**语法门禁判绿，EditMode 才会红**。核实后发现 HallScene 的材质**全是方法内局部变量**，
没有任何材质字段。正解：新增 `BuildTruck()` 方法，内部用类里已有的
`Mat(Color, roughness, MaterialFamily)` 现取材质，调用点只写一行。
这与本会话反复出现的同一类错（缺 using、缺 KeyCode 成员、schema 猜形状）是同一个根因：
**结构信息必须读出来，不能凭印象写。**
