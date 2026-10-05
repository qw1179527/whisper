# `LobbyEvidenceCapture` 规格 —— P1 的前提（2026-10-06）

> **为什么需要它**：目标里 P1 的验收标准是「能在云端取证里看到画面对」，
> 而**当前没有任何一张大厅的图** —— `RenderEvidenceCapture` 按**关卡房间**取景
> （`LoadRooms()` → `Targets`），而大厅是**代码搭的、不属于任何关卡**。
> 没有基线图 ⇒ 改了也没法证明变好了。

## 已查明的事实（不用再查）

| 事实 | 出处 |
|---|---|
| `HallScene` 构造签名 = `public HallScene(Transform parent, Camera cam)` | `HallScene.cs:95` |
| 现成装配范例 | `MenuScene.cs:172`：`_hall = new HallScene(_root, _cam);` |
| 大厅尺寸属性 | `HallScene.WidthM` / `LengthM`（`MenuScene:180` 用 `_hall.WidthM * 0.5f` 算可行走矩形） |
| 取证脚本的取景方式 | `RenderEvidenceCapture.PlaceCamera(t, cam, roomId, view, cx, cz, sx, sz, sy, problems)` —— **按房间几何算**，不适用于大厅 |
| 取证脚本只接受 `-captureDir` | `RenderEvidenceCapture.cs:101`、`ArgValue` 在 649 |
| 离屏取帧的正确做法 | **`Camera.Render()` → `RenderTexture` → `ReadPixels` → `EncodeToPNG`**（同步、确定）。`ScreenCapture.CaptureScreenshot` 在 `-batchmode` 下**不管用**（`RenderEvidenceCapture` 头部注释里记着实测） |
| ⚠ **不要加 `-nographics`** | `Camera.Render()` 在无图形设备下会崩 `0xC0000005`（同上注释）。必须 `xvfb-run` |

## 建议实现（约 120~150 行）

```
新建 unity/Assets/Editor/LobbyEvidenceCapture.cs
  public static class LobbyEvidenceCapture
  {
      public static void Run()
      {
          // ① 建根 + 相机（照 GameBootstrap.BuildCamera 的做法，或直接建）
          // ② new HallScene(root, cam)
          // ③ 取景点：至少覆盖
          //      · 入口视角（玩家一进来看到的）—— 官方要求"一进来就面对菜单板"
          //      · 俯视全景（orbit）
          //      · 货车特写（§三 的重点对象）
          //      · 菜单板正视
          // ④ 每个取景点 × 开灯/关灯 两相位（沿用 RenderEvidenceCapture 的相位思路）
          // ⑤ 写 PNG + lobby-index.csv 到 -captureDir
          // ⑥ **两层判据**：退出码 + 日志 sentinel `LOBBY_EVIDENCE OK`
          //    （照 render-evidence.sh 的教训：executeMethod 名字写错会"静默成功"）
      }
  }
```
**并同步**：
· 建 `LobbyEvidenceCapture.cs.meta`（GUID 用 `sha1('whisper-script-meta:'+文件名)` 确定性派生，同现有 8 个分部）
· 在 `unity-agent.yml` 的 `task` 选项里加 `lobby-evidence` 分支（照 `kit-visibility` 那支写）
· 新增 `tools/lobby-evidence.sh`（照 `render-evidence.sh` 的结构：两层判据 + 像素比对）

## 验收判据（写进脚本，不靠人看）
1. **退出码 = 0** 且 **日志含 `LOBBY_EVIDENCE OK`**（缺一即红）
2. **开灯 vs 关灯必须可分辨**（沿用 `tools/pixel-diff-pair.mjs`，阈值参考 render-evidence 的 1.0%）
3. **至少产出 N 张 PNG**（N = 取景点数 × 相位数），少于 N 判红
4. **产物非空校验**：`unzip -t` 必须通过（见 `docs/downloader-bug-parallel-corruption-2026-10-06.md` —— 大小对内容也可能坏）

## 与 P1 主任务的关系
```
① 写 LobbyEvidenceCapture → 出"改之前"的基线图      ← 先做这个
② 把 HallScene 的 44 处 Box 换成套件装配
③ 再跑一次取证 → 与基线比对（像素差异 + 人看）      ← 这才叫"能自证"
```
