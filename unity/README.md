# Project Whisper · Unity 工程（出包说明）

## 为什么出包不能在本机做

本项目在 **Android 手机 + AI** 的环境下开发，而 Unity 编辑器在该环境下不可用，实测依据：

| 检查项 | 实测结果 |
|---|---|
| Unity 官方发布索引（`services.api.unity.com`） | Linux 编辑器**只有 X86_64**（最新 6000.3.25f1 · 4.2 GB 下载 / 8.1 GB 安装） |
| 本机架构 | `aarch64` —— 官方无 arm64 Linux 编辑器 |
| `download.unity3d.com` | 对本区域**返回 404**（根路径同样 404，属区域拦截；URL 取自官方 API） |
| 本机 libc | `bionic`（Android），Unity 需要 glibc |
| 可用内存 | 约 6 GB（Unity + IL2CPP 在该量级下极易 OOM） |
| 磁盘 | 282 GB 可用（**不是**瓶颈） |

理论上可用 `proot` + glibc rootfs + `box64`/`qemu-user` 做 x86_64 模拟（本机已有 `proot`，
`clang`/`cmake` 也齐，可自建 box64），但 IL2CPP 编译在模拟下会从"分钟级"变成"小时级"，
且内存余量不足 —— 结论：**本机不适合出 Unity 包，出包交给 CI**。

## 怎么出包（三条路，任选）

### ① GitHub Actions（已配好，推荐）
本仓库已含 `.github/workflows/unity-android.yml`。只需：
1. 把仓库推到 GitHub
2. 配 3 个 Secrets：`UNITY_LICENSE` / `UNITY_EMAIL` / `UNITY_PASSWORD`
   （获取方式见 https://game.ci/docs/unity/activation）
3. 触发 `unity-android` 工作流 → 产物里下载 `whisper-android-apk`

工作流已内置 **V9 §13.8 包体门禁**（APK >200MB 直接失败）。

### ② 本地/任意有 Unity 的机器
```bash
# 首次：生成 Boot 场景（场景零手工，由代码生成）
Unity -quit -batchmode -projectPath unity -executeMethod Whisper.Editor.EditorSceneBootstrap.EnsureBootScene
# 构建
Unity -quit -batchmode -projectPath unity -executeMethod Whisper.Editor.BuildScript.BuildAndroid
# 产物：build/Android/whisper-android.apk
```
需要 Unity **6000.3.25f1**（见 `ProjectSettings/ProjectVersion.txt`）+ Android Build Support 模块。

### ③ 云端构建机（GameCI 的 Docker 镜像）
`unityci/editor:ubuntu-6000.3.25f1-android-3`，配合上面的 `-executeMethod` 命令。

## 首次打开工程后需要确认的设置

`ProjectSettings/*.asset` 必须由 Unity 生成（本机没有编辑器，只放了 `ProjectVersion.txt`）。
首次打开后确认：包名 `com.whisper.projectwhisper` · targetSdk 36 · ARM64 · IL2CPP · URP。

## 这个工程里已经就绪的部分

| 内容 | 状态 |
|---|---|
| Gameplay/Core 全部玩法代码 | ✅ 本机 113 条断言真跑通过 |
| 关卡 DSL + 几何编译 + 装配计划 | ✅ 内墙/门/连通/道具落位都有断言 |
| Boot 组合根（三接口注入 + 几何装配 + HUD） | ✅ 代码就绪（`Runtime/GameBootstrap.cs`） |
| 构建脚本 + 包体门禁 | ✅ `Assets/Editor/BuildScript.cs` |
| Boot 场景 | ⚠️ 本机无编辑器，改为**代码生成**（`EditorSceneBootstrap.cs`） |
| ProjectSettings 资产 | ⚠️ 需在 Unity 里首次生成 |
| PlayMode 用例 | ⚠️ 从未执行过（本机无 Unity） |
