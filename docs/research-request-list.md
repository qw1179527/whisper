# 资料查证清单（我列出、你协助查）· 2026-10-05

> 目的：把"机制数值一律以官方资料为准、不猜"这条约束**真正落实**。
> 我列的是**具体 URL + 具体问题**，不是泛泛的"找资料"。

## 〇、我的取用边界（先说清，避免你白费力气）

| 我能自己做 | 需要你协助 |
|---|---|
| ✅ `phasmophobia.su` **整站可读**（我已抓到 6 个页面原文） | ❌ **phasmophobia.fandom.com 全系读不到**（含 `/zh/`、`/ru/`，超时） |
| ✅ 部分非 Fandom 站点（devtrackers.gg 成功过） | ❌ **wikiwiki.jp 403 反爬** |
| ✅ Steam 社区公告类页面**部分可读** | ❌ **我看不到图片内容**（没有图像识别网络图的能力） |
| | ❌ gdcvault.com（需登录） |

**所以你的帮忙最值钱的三类**：① **Fandom 页面的正文**（复制粘贴给我）② **官方补丁说明**（Steam 公告）③ **图片里的信息**（截图里的文字/布局，你口述或贴文字）。

---

## 一、P0 —— 直接决定"可开黑"核心循环能否跑通

### 1. 七种证据的判定规则（我方 7 种证据的实现毫无依据）
**为什么急**：`ItemSystem` 与证据判定是我们自己的猜测实现；证据是整局的核心产出。
**查什么**（每条都要）：判定**触发条件**（距离/时长/前置状态）· **显示方式** · **能被谁看到**（在场/货车）· **是否受猎杀影响**。

| 证据 | phasmophobia.su 页面（我可自读，但你若能补 Fandom 更好） |
|---|---|
| EMF 5 级 | `/knowledge-base/evidence/emf-5` |
| 通灵盒 | `/knowledge-base/evidence/spirit-box` |
| 零下温度 | `/knowledge-base/evidence/freezing-temperatures` |
| 紫外（指纹/足迹） | `/knowledge-base/evidence/ultraviolet` |
| 鬼火/灵球 | `/knowledge-base/evidence/ghost-orbs` |
| 笔记本笔迹 | `/knowledge-base/evidence/ghost-writing` |
| D.O.T.S. | `/knowledge-base/evidence/dots-projector` |

**同时需要**：`/knowledge-base/evidence`（总览页我已有：7 类清单）。

### 2. 21 件道具的**使用规则**（我方只有笼统的"道具存在"）
**为什么急**：用户《补充说明》§9 要求"道具四大类"落地；没有官方规则就只能猜。
**每件需要**：**判定距离/半径** · **持续时长** · **等级（I/II/III）差异** · **消耗/冷却** · **能否放置** · **是否算"活动电子设备"**（影响 Raiju 与猎杀吸引）。

优先级最高 8 件（其余可后补）：
| 道具 | 页面 |
|---|---|
| 盐 | `/knowledge-base/equipment/salt` |
| 运动传感器 | `/knowledge-base/equipment/motion-sensor` |
| 声音传感器 | `/knowledge-base/equipment/sound-sensor` |
| 头戴设备 | `/knowledge-base/equipment/head-mount`（或 head-gear） |
| 摄像机（视频） | `/knowledge-base/equipment/video-camera` |
| 相机（拍照） | `/knowledge-base/equipment/photo-camera` |
| 十字架 | `/knowledge-base/equipment/crucifix` |
| 圣木 | `/knowledge-base/equipment/incense` |

### 3. 撤离与结算（我方 `GameSession.EndMatch` 是自定的）
**查什么**：**撤离条件**（官方：所有存活玩家都在货车内）· **门/坡道关闭时机** · **保险**（覆盖范围、触发）·
**金钱与 XP 计算**（含媒体证据计分、骨头、完美调查定义）· **死亡后装备丢失规则**。
**可能的页面**：`/knowledge-base/gameplay/...`（撤离/结算/奖励）或 `/knowledge-base/guides/...`。
**需要你帮**：如果 phasmophobia.su 没有，请从 **Fandom 的 `Contract` / `Money` / `Experience` / `Insurance`** 页贴给我。

---

## 二、P1 —— 界面与大厅（你反复强调"完全参考官方大厅"）

### 4. **大厅（Lobby）的完整官方描述** ← **这条最需要你帮**
`phasmophobia.fandom.com/wiki/Lobby` **我读不到**。你能否把该页正文**复制粘贴**给我？
我需要它回答：
- 一层/二层的**功能区划分**与**相对位置**（菜单板在哪面墙、地图板在左、商店电脑在右——这些我有，但需要更精确的相对坐标感）；
- **二层娱乐设施**的清单与位置（篮球/玉米洞/保龄球/Biggish Blocks）；
- **展示区**（骨头/诅咒之物/奖杯）的位置；
- **环境互动**清单（喷漆罐、音响、柜子、储物柜）；
- **彩蛋**（篮球 666、紫外线信息、JOJO WAS HERE、幽灵窥视）的**具体位置**；
- **0.7.0 大厅翻新**改了什么（`/wiki/0.7.0`）。

### 5. **主菜单板（sticky notes）的 UI 细节**
需要：**6 张便签的排布**（我按 2×3 做的）· **玩家颜色如何映射到便签** · **ID 卡在右上角的尺寸与内容** ·
**每日/周挑战显示在板的哪个位置** · **房间码显示位置**。
**来源**：Fandom `Lobby` 页 + 你游戏内截图（**截图我看不到图，但你可以口述文字**）。

### 6. **商店三页的具体 UI**
官方开发日志说：`Shop / Equipment / Loadout` 三页 + 每页短教程 + Loadout 可重命名与批量调档。
需要：**每页的布局与字段**（价格/等级/上限/拥有数）· **分类方式** · **购买/出售交互**。
**来源**：Steam 公告（Shop Update 那期）+ Fandom `Shop` 页。

---

## 三、P2 —— 建模参考（**这条我必须承认能力不足**）

### 7. **我看不到图片** —— 建模参考必须有你来
货车（Eurocargo 75E18）与大厅仓库的**外观**，我只能从**文字描述**与**尺寸数据**入手。
如果你能提供：
- 官方**货车外观截图**（或实车照片）→ 告诉我**关键比例与特征**（如：驾驶室与厢体的高度关系、车轮位置、导流罩形状、车尾坡道形式）；
- 大厅**几张不同角度的截图** → 告诉我**空间关系**（长宽比、二层占多少、菜单板相对大小）；
- **玩家/鬼的角色截图** → 告诉我**风格**（写实？卡通？比例）。
我会据此调整样板；**但纯靠文字我做不到"1:1 还原"**，这一点我提前说清。

---

## 四、P3 —— 机制细节（可后补，但迟早要）

| # | 主题 | 来源 |
|---|---|---|
| 8 | **29 种鬼的详细页**（能力/速度/阈值/证据组合） | `/knowledge-base/ghosts/<name>`（我可自读，逐个抓） |
| 9 | 诅咒道具 7 种的精确数值 | `/knowledge-base/cursed-objects/*`（我可自读） |
| 10 | **难度与自定义难度的完整参数表** | 需 Fandom `Difficulty` 页（你帮） |
| 11 | **进度/声望**（等级、转生、解锁表） | Fandom `Progression`/`Prestige`（你帮） |
| 12 | **每日/周任务的生成与奖励规则** | Fandom `Daily Tasks`（你帮） |
| 13 | **媒体与日记**（照片上限 5、视频 5、音频 3、独特标记） | Fandom `Media`/`Journal`（你帮）—— 我方已有部分（§9 第 4 条） |
| 14 | **装备解锁等级表**（21 件各在几级解锁） | `/knowledge-base/equipment` 提到"按等级解锁"但我只拿到清单，**需展开表** |

---

## 五、我建议的**最小可用批次**（如果你只帮一次，帮这四条最值）
1. **Fandom `Lobby` 页正文** → 解锁"主界面完全参考官方大厅"（你最重要的诉求）
2. **Fandom `Shop` 页 + Shop Update 公告** → 解锁商店三页与道具购买规则
3. **`/knowledge-base/equipment/*` 里盐/传感器/摄像机/十字架/圣木 5 件**（我可自读，但若你把 Fandom 对应页也给我，能交叉验证）
4. **一张货车外观参考 + 一张大厅全景** → 解锁建模方向（**只有你能提供**）

> 我这边会继续自读 `phasmophobia.su` 的 29 个鬼页与诅咒道具页，不用等你。
