# 货车与玩法层接线（2026-10-05 · 第 17 轮）

> **本轮最重要的发现是一条架构事实，而不是安全区**：`Gameplay/Session/GameSession.cs`（理智→猎杀→
> 取证→撤离的**完整玩法层**）**全仓零实例化** —— 局内实际跑的是 `GameBootstrap` 自己的简化逻辑。
> 这直接决定"可开黑核心循环"还差什么。

## 一、结论：`GameSession` 从未接进对局（P0）

**证据（实测，非推断）**
```
grep -r "GameSession" unity/Assets/Scripts → 仅 3 处命中，全部在 GameSession.cs 自身文件内
grep -r "new GameSession(" → 零命中
grep "_session" GameBootstrap.cs → 零命中
```
`GameSession.cs:189` 原本写着：
```csharp
Sanity.Tick(dt, args.TorchOn, inSafeZone: false, lightsOut: args.LightsOut);
```
`inSafeZone` 硬编码 `false` 且**没有任何调用者**。

**含义**
- 理智系统、猎杀调度、证据/道具、撤离结算 **全部写好了但没通电**；
- 局内正在跑的是 `GameBootstrap.TickInteractionAndTasks()` 那套简化实现；
- 「进局→取证→撤离」循环**缺的是接线，不是功能**。

**为什么之前没被发现**：这些类各自有单元/EditMode 测试（62 个全过），测试直接 new 它们，
所以"类是否正确"一直是绿的；**没有任何测试问"产品里到底 new 了谁"**。
这与本项目既有教训完全同族（`SendLocalPlayer` 零调用者、`UdpV6NetService` 从未构造、
`MenuScene` 键盘检查挂在条件链里从未生效）——**"验证过 ≠ 在产品里"**。

## 二、本轮实际完成（可复核）

| 项 | 内容 |
|---|---|
| `TruckScene` | 增加 `SafeZoneMargin=2.5f`（**design 值**，§8 说"货车及其周围"）、`SafeZoneBounds`、`IsInSafeZone` |
| `HallScene` | 增加 `TruckSafeZone`（货车未建时返回"地底 + 零尺寸"，**fail-safe：宁可不保护也不误保护**） |
| `GameBootstrap` | 增加 `TruckSafeZone` 属性、`SetTruckSafeZone(Bounds)`、`PlayerInTruckSafeZone` |
| `MenuScene` | 货车建好后**推送**安全区给组合根（`_hall` 由 MenuScene 持有，组合根不反向抓 UI 层） |
| `GameSession` | 安全区字段与两个判定（`PlayerInSafeZone` / `MonsterMayEnter`）已就位，`Sanity.Tick` 改为读它 |

**门禁**：`unity-syntax-check` exit=0 · `unity-tests EditMode` **62/62** · 三张地图 `validate-levels` 与 `gate-model` 全过。

## 三、本轮踩的坑（同一族，已写进各脚本头注释）

| # | 错 | 真相 | 教训 |
|---|---|---|---|
| 1 | 写 `_matBody/_matMetal` 接货车 | HallScene 材质**全是方法内局部变量** | **用成员的先 grep 它的真实声明** |
| 2 | 写 `_hall.TruckSafeZone` | `GameBootstrap` **没有** `_hall`（只有 `MenuScene.cs:82` 有） | 跨类引用尤其要先 grep |
| 3 | 在 `GameSession` 用 `Vector3` | **`Whisper.Gameplay` 程序集不引用 UnityEngine**（CS0246） | 分层是真的：**玩法层零 Unity 依赖** |

> 第 3 条不是"写错了"，而是**有价值的架构信息**：玩法层收裸浮点/bool，几何判断留在 Unity 侧。
> 这也解释了为什么 `SanitySystem.Tick(..., inSafeZone: bool, ...)` 收 bool 而不是坐标。

**共同点：我在"写代码"时假定了一个我没读过的结构。** 语法门禁不做名字解析（本轮 3 次全判绿），
只有真 Unity 编译（EditMode）会红 —— 代价是整轮返工。

## 四、下一步（按依赖顺序，P0 优先）

1. **P0 玩法层接线**：把 `GameSession` 实例化进对局，替换 `GameBootstrap` 里的简化逻辑。
   需覆盖：出生点、证据点落位、怪物实例化、阶段推进、撤离结算。
   **必须配回归测试**（否则又是一次"接线了但没验证"）。
2. **P0 接线后**：把 `TruckSafeZone` 的裸浮点真正喂进 `GameSession`（现在只存了 Bounds，因为组合根没有 session 字段）。
3. 货车其余功能：车尾键盘控坡道升降、主门钥匙生成、监控电脑（切摄像头/夜视）、地点地图（切楼层）、装备墙取用/放回、大厅态"除 00:00 计时器外屏幕关闭"。
4. **货车视觉验证**：目前只有门禁级验证，**没有渲染截图确认外观**。
5. 之后按用户约束：道具四大类 → 机制细化 → 过渡动画与真人化角色 → 真实物理与画质。
