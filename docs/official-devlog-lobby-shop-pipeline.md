# 官方开发日志：主界面/商店/关卡制作流程（2026-10-05 · 第 31 轮）

> 来源：`devtrackers.gg` 转载的官方 Development Preview #17（2024-02-15，Kinetic Games 团队原文）。
> 本环境**可读**；`gamerant.com` 取回失败（fetch failed）。

## 一、官方**主界面（大厅）与商店**的做法（原文要点）

### 商店与装备页重做（本轮最直接相关）
> "the highly requested **Shop Update** … features a **brand new design for the Shop and Equipment pages,
> **with simplicity in mind**. And a new **Loadout page** for getting into your games as quickly as possible."
- 明确的设计原则：**simplicity（简洁）**优先；
- 页面构成：**Shop（商店）· Equipment（装备）· Loadout（快速配装）**；
- **每一页都带一段短教程**（"each page even has a short tutorial to help you out if you get lost"）；
- **手柄玩家可用按键快速买卖**，不必移动光标满屏找；
- **Loadout 页**：键盘玩家可**重命名配装**；底部按钮可**批量调整所有道具的数量与等级**。

### **玩家颜色**贯穿三处 UI（重要线索）
> "the ability to **change player colours** (for the **sticky notes on the main menu/contract board**,
> at the **top of the shop** and on the **sanity screen**)"
⇒ 官方 UI 里玩家颜色**统一出现在**：
1. **主菜单/合约板上的便签（sticky notes）** ← 即我们用"6 张纸片"表达的那块板，官方叫 **sticky notes**；
2. **商店顶部**；
3. **理智屏**。

这条与我们从 `/van/site-map` 读到的"玩家位置颜色与理智监视器一致"**互相印证**：
**玩家颜色是跨 UI 的同一套语义**，我们做多人与 UI 时必须统一。

### 光标定制
> "customisation for your cursor during gameplay. You'll be able to pick which **icon** … the **colour**,
> **size**, **transparency level** and **how it changes while you're highlighting something interactive**."
⇒ 光标有 5 个可调维度，且**悬停交互物时会有变化**（我们目前没有这套）。

## 二、官方**关卡制作流程**（原文，正好是我们的资产管线依据）
> "**Point Hope construction** continues, and now that the **final few areas are being populated with new assets**,
> we'll soon be able to **set it up for gameplay and get testing**."
⇒ 官方流程：**灰盒/白盒 → 用新资产填充区域 → 接入玩法并测试**。
这与我方**"布局表 → 套件生成 → 关卡 JSON → gate-model"**的管线同构（白盒=布局表，资产填充=套件 GLB）。

> "Zec has been hard at work continuing to **white-box** and create **concept art** for the farmhouse reworks."
⇒ 官方**先白盒 + 概念图**，再上资产。**农场图（Grafton）重做**的细节也给了：
- 建筑形状改动，**布局更动态**；
- **墙体破败**，外部天气**可能进入室内**（玩法与氛围耦合）；
- 二楼某房间**墙塌了，形成新的门洞**（关卡结构随美术状态变化）；
- 地板散落物品、床被部分拆解 —— **叙事性布置**（"也许有人来找东西，也许他还在"）。

## 三、可直接用于本项目的四条
| # | 官方做法 | 我们的落地 |
|---|---|---|
| 1 | **玩家颜色是跨 UI 同一套语义**（便签/商店顶/理智屏） | 多人时给每位玩家分配一个颜色，UI 三处统一取同一来源（避免三处各算） |
| 2 | 菜单板官方称 **sticky notes**；带玩家颜色 | 我们的"6 张纸片"命名与颜色语义对齐官方 |
| 3 | 商店 UI 三页：**Shop / Equipment / Loadout**，**每页带短教程**，Loadout 可重命名与批量调档 | 道具四大类那一轮按这三页做，并把"短教程"作为每个面板的必备内容 |
| 4 | 关卡流程：**白盒 → 资产填充 → 接玩法测试** | 与既有 `gen-map.mjs`（布局表）+ `gen-kits.mjs`（套件）+ `gate-model`（门禁）管线一致，**官方流程印证了我方管线** |

## 四、还找到但读不到的高价值来源
- **[Unity Developer Summit: Building Fear: The Tech Behind Phasmophobia](https://gdcvault.com/play/1035404/)**
  —— 官方技术演讲（渲染/性能/工程做法），**这是"主界面制作"最权威的一手来源**。
  > 注：`gdcvault.com` 需登录，本环境未取到正文；建议你人工看，或告诉我是否需要我换镜像找转载。

## 五、本轮代码交付
- `data/config.json` 新增 `sanity.drain.official`（官方流失公式全表 + 光照口径 + 九项来源 + `_src` 标 official）
- `SanitySystem` 新增**官方口径 Tick 重载**（`SanityTickContext`）：
  基础值(地图×阶段) × 难度乘数(+血月) × 单人与否 → 主灯/大暗区/火源修正 → Setup 保底 50
  ⚠ **手电不参与流失计算**（官方明确手电不能停止流失）；旧重载保留不动
- **新增 9 个测试**（此前 `SanitySystem` **零测试覆盖**）

## 六、门禁
| 门禁 | 结果 |
|---|---|
| `unity-syntax-check.sh` | exit=0 |
| `unity-tests.sh EditMode` | **total=71 passed=71 failed=0**（62 → 71，新增 9 个理智测试） |
| `validate-levels` / `data-mirror` | exit=0 / 镜像一致 |
