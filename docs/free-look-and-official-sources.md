# 自由视角实现与官方资料来源（2026-10-05 · 第 21 轮）

> 交付物：`whisper-android-0.1.81-code81.apk`（36,268,813 字节）
> 门禁：`unity-syntax-check` exit=0 · `unity-tests EditMode` **62/62**

## 一、自由视角（用户第 5 条）—— 已实现

### 官方依据（用户《补充说明.md》第 5 行原文，逐字）
> 「场景建模：大厅的3D模型包含**完整的碰撞体（Collider）**，**玩家可以在其中自由行走**。
>  场景中的道具（如篮球、喷漆罐）都有独立的物理效果。」
> 「主菜单板：位于大厅最明亮的墙上…按 **空格键** 或**鼠标左键点击**即可进入操作界面，
>  再按一次 空格键 或 **Esc** 键则可退出。」

**关键区别**：官方的「**行走**」与「**进操作界面**」是**两件事** —— 玩家先自由走到板前，再按键/点击进操作视角。
我此前的实现把两者抹成了一件事（`UpdateBoardCamera` 每帧直接写相机位姿、只有两个固定机位），
所以玩家不能走、不能转头。

### 新增 `unity/Assets/Scripts/Runtime/LobbyCamera.cs`
| 项 | 实现 |
|---|---|
| 自由环视 | yaw + pitch，俯仰限位 ±78°（防翻过头顶） |
| 移动端操控 | **左半屏拖动 = 移动**、**右半屏拖动 = 环视**（官方主机版"左摇杆走/右摇杆看"的触屏等价物；本项目无摇杆控件体系，分屏拖动零控件依赖） |
| 桌面操控 | 鼠标偏离屏幕中心 → 缓慢移动（`W/A/S/D` 待 `KeyCode` 桩补齐后接；**不写不存在的成员**） |
| 眼高 | 1.7 m，**与局内 `PlayerController.EyeHeightM` 同值**（避免大厅/局内视线高度不一致） |
| 行走范围 | 大厅是规则矩形仓库 → 墙内收 0.4 m 的矩形约束（真实道具碰撞留给后续"道具物理"轮） |

### `MenuScene.UpdateBoardCamera` 重写
- **自由态**：`_lobbyCam.Active = true; return;` —— **绝不写相机 transform**
- **操作视角**：`Active = false`，**不把相机平移过去**，只**原地转向对准菜单板**（玩家仍站在原地，符合官方）
- 到位后**一个字节都不动**（此前抖屏的教训：永不静止 = 细颤）

### 为什么不用局内 `PlayerMotion`
`PlayerMotion.Step(...)` **要求 `LevelGeometry` 非空**（`PlayerMotion.cs:150` 显式抛异常），
而 `LevelGeometry` 只能从**关卡数据**（`LevelData`）编译 —— 大厅是代码搭的、没有关卡数据。
为大厅现造一份关卡数据成本过高，故 `LobbyCamera` 自己做矩形约束。

## 二、官方资料搜索结果（本轮）

### ⚠ 工具限制（必须说清）
`web_search` 在本环境**只返回链接与来源列表，不返回正文**；我已试过 **10+ 次**、多种查询措辞
（中/英、问句型/名词型）。本环境**没有 `web_fetch`**（不在工具集里，调用报 `unknown tool`）。
→ **我能定位官方页面，但读不到页面正文。** 故本项目内**权威来源仍是你的《补充说明.md》**（含逐条官方描述）。

### 找到的官方/高价值来源（供人工核对）
| 来源 | 用途 |
|---|---|
| [Phasmophobia Wiki · Lobby](https://phasmophobia.fandom.com/wiki/Lobby) | 大厅形态权威词条 |
| [Phasmophobia Wiki · 0.7.0](https://phasmophobia.fandom.com/wiki/0.7.0#Gameplay) | 大厅翻新版本（商店与布局重做的原始说明） |
| [厢式货车（中文）](https://phasmophobia.fandom.com/zh/wiki/%E5%8E%A2%E5%BC%8F%E8%B4%A7%E8%BD%A6) · [Truck (EN)](https://phasmophobia.fandom.com/wiki/Truck) | 货车内部布局 |
| [Truck Overhaul v0.6.2.0 补丁](https://steamdb.info/patchnotes/8911777/) | 货车重做的官方变更记录 |
| [ZHdK · Phasmophobia 技术分析 PDF](https://imachina.zhdk.ch/documentspublic/document164506.pdf) | 渲染/技术实现分析（对画质方向有用） |
| [Steam · Development Preview #2](https://store.steampowered.com/news/app/739630/view/5524239301852390164) | 官方开发预览（含大厅/建模方向） |

## 三、本轮其他交付（用户第 2、3、4 条）

| # | 处置 |
|---|---|
| 2「不留字体」 | 找到真根因：左上角整块在 **`GameBootstrap.Update()`** 每 0.5s 写 `_status.text`，我第 10 轮的开关加在 `MenuScene` 上**管不到它** → 加 `GameBootstrap.ShowDiagnostics=false`，关闭时 `SetActive(false)`（真正"不留字体"）；菜单板中央提示条也已删 |
| 3「谁把车放这儿」 | 原在**大厅中偏左、紧贴菜单板前墙**（挡主视野）→ 挪到**右后侧、车头朝后墙、车尾朝场内** |
| 4「瞎写界面」 | 删掉我编的两行（「◀ 地图｜菜单板｜商店 ▶」「每日挑战·每周挑战」）—— 官方是"快速跳转**按钮**"与"菜单板**板面内容**" |

## 四、仍未解决（诚实列出）

| 项 | 状态 |
|---|---|
| **点击失灵 / 长时间点无反应**（用户第 1 条） | **未定论**。已列 P0：需先加"每次点击落盘回执"再真机复现定位（不再猜） |
| **建模精细重做**（用户追加） | 未开始。方案见 `docs/five-fixes-and-modeling-redo-plan.md` §三：走既有资产管线（`gen-hall-kits.mjs` + 清单唯一真源 + `gate-model` M1–M11），每套件出图人工核对。**多轮工作量** |
| 0.1.81 真机验证 | 未做（自由视角是否可用需真机拖屏验证） |
