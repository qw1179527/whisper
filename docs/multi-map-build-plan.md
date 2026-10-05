# 多地图施工图（2026-10-05 · 第 13–14 轮）

> 状态：**注册表与运行时选图已完成**（第 13 轮，双门禁绿）；**第二张图尚未生成**。
> 本文件是下一步的施工图：生成器参数化的确切做法 + 新布局的坐标契约 + 验收命令。

## 1. 已完成（第 13 轮，可复核）

| 项 | 证据 |
|---|---|
| `data/config.json` 新增 `maps` 注册表（3 条：asylum_v1 已实现 + 2 张官方图未实现） | `node -e "console.log(require('D:/dshr/unity/Assets/Data/config.json').maps)"` |
| 每条带 `_src` 标注 design/official（禁止把设计值冒充官方） | 同上 |
| `GameBootstrap.SelectMap(id)` / `DescribeMaps()` / `MapId` | `unity/Assets/Scripts/Runtime/GameBootstrap.cs:36` 起 |
| 地图板读注册表 + 数字键 1-9 选图 + 未实现明确拒绝 | `MenuScene.OpenMapVotePanel` / `SelectMapByNumber` |
| 三份镜像同步 | `node tools/data-mirror.mjs` → 7 对逐字节一致 |

## 2. 生成器的真实结构（读码所得，不是猜）

`tools/gen-asylum-v1.mjs`（348 行）：

| 常量 | 行 | 内容 |
|---|---|---|
| `BOXES` | 21–85 | 房间/走廊表：`{ id, x0, x1, z0, z1, h, floor, kit, zone, evidence, props[], doors[] }` |
| `SHAFTS` | 86–91 | 竖井（三层垂直结构用） |
| `LINKS` | 92–107 | 楼层间连接 |
| `DOOR_WIDTH` | 108 | 门宽表 |
| `FOOTPRINT` | 154 | 套件占地（与 `asset-manifest.json` 同步；由 `tools/sync-footprints.mjs` 维护） |
| `OUT` | 345 | **硬编码** `unity/Assets/Levels/asylum_v1.json` |

**坐标系契约**（写新布局必须遵守）：
- `x0/x1/z0/z1` 是**最小角点 + 最大角点**（米，XZ 平面）；`pos = [x0, z0]`；
- `doors[].at` 是**沿该墙的绝对坐标**（不是相对偏移）：
  · `wall:'east'|'west'` → `at` 是 **z** 坐标；· `wall:'north'|'south'` → `at` 是 **x** 坐标；
- **门对齐硬门禁**（`tools/validate-levels.mjs` 判红条件）：两房相邻共享同一面墙，且两门 `at` 必须一致（开口对齐）；
- 同层房间**不得重叠**；从入口必须能洪水填充到所有房间（含撤离点）；
- 走廊必须用 `doorA/doorB` 引用具体门（房间id/门id）。

## 3. 参数化做法（建议）

1. 把 `BOXES/SHAFTS/LINKS/DOOR_WIDTH` 抽到 **`tools/lib/map-layouts.mjs`**（ASCII 文件名！中文路径会触发本机写文件故障）
   导出 `LAYOUTS = { asylum_v1: {...}, tanglewood_v1: {...} }`；
2. `gen-asylum-v1.mjs` 改为读 `--layout <id> --out <file>`（默认 `asylum_v1`，保持既有行为不变）；
   ⚠ 保留"默认不带参数时行为与现在逐字节一致"，否则 `gen-asylum` 的产物哈希会变、影响既有门禁；
3. 新增布局 `tanglewood_v1`（官方最小图：6 Tanglewood Drive，**单层住宅**）；
4. `data-mirror.mjs` 的 `PAIRS` 增加 `unity/Assets/Levels/<新图>.json ↔ Resources/Levels/<新图>.json`（否则运行时读不到）；
5. `config.maps.list` 里把该条 `implemented` 改 `true`。

## 4. 新布局 `tanglewood_v1` 的设计草案（单层 · 小图）

目标规模：官方小图是**单层住宅**，房间数远少于疗养院（本项目疗养院 15 房间）。
草案（可直接作为第一版，随后用门禁迭代）：

| 房间 | 坐标 | 门 |
|---|---|---|
| `entrance` 玄关 | x0..4, z0..3 | east @ z=1.5 |
| `hall_main` 客厅走廊 | x4..10, z0..3 | west @ z=1.5；north @ x=5.5 |
| `kitchen` 厨房 | x4..7, z3..6 | south @ x=5.5 |
| `bath` 卫生间 | x7..10, z3..6 | south @ x=8.5；north @ x=8.5 |
| `bedroom` 卧室 | x7..10, z6..10 | south @ x=8.5 |
| `garage` 车库（可选） | x10..14, z0..3 | west @ z=1.5 |

**对齐要点**（照着算，别猜）：
- `entrance.d_east` 的 `at` 是 z=1.5，而 `hall_main` 占 z0..3 → `hall_main.d_west` 也必须是 z=1.5；
- `hall_main.d_north` 的 `at` 是 x=5.5，`kitchen` 占 x4..7 → `kitchen.d_south` 也取 x=5.5；
- 每个门**只能**出现在两房共享的那条墙上，且两房该墙必须重合。

## 5. 验收命令（每一步都要跑）

```bash
cd /d/dshr
node tools/gen-asylum-v1.mjs --layout tanglewood_v1 --out unity/Assets/Levels/tanglewood_v1.json
node tools/validate-levels.mjs          # 门对齐 / 重叠 / 连通 / 镜像
node tools/data-mirror.mjs --sync       # 补镜像对之后
node tools/gate-model.mjs               # M1..M11
bash tools/unity-syntax-check.sh        # exit 0
bash tools/unity-tests.sh EditMode      # 期望 62/62 起（只增不减）
```

⚠ **注意**：`BootSmokeTests.cs:65` 断言默认关卡是 `asylum_v1` —— 换默认图会打破它；第二张图**不要**动默认值。

## 6. 本轮（14 轮）实际做了什么

只做了侦察与方案落盘（未改代码）：
- 读清 `gen-asylum-v1.mjs` 的布局表结构与坐标系/门对齐契约（见 §2）；
- 确认 `data-mirror.mjs` 的镜像对需要为新图补条目（否则运行时 `Resources.Load` 读不到）；
- 确认 `BootSmokeTests` 对默认关卡的断言位置（避免换图时踩雷）。
**门禁状态：仍为绿**（本轮无代码改动；上一轮末次实测 syntax exit=0 · EditMode 62/62）。
