# ProjectSettings 策略（**故意不提交伪造的设置文件**）

## 为什么这里没有 ProjectVersion.txt / ProjectSettings.asset
这些是 Unity 编辑器生成的 YAML（部分含 GUID 与二进制引用），**手写必然是错的**，还会让"工程可打开"变成假象。
本骨架的纪律是：**能被静态检查的用文本真源表达，必须由编辑器生成的注明生成方式**。

## 真源与生成方式（逐项）

| 设置项 | 真源 | 生成方式 | 校验 |
|---|---|---|---|
| Unity 版本 | `unity/dependency-lock.json` → `unity.editor` = **6000.0.38f1** | 首次打开工程时由该版本创建 `ProjectVersion.txt` | CI 读锁文件传给 `game-ci/unity-builder`，禁止 workflow 内硬编码 |
| 包依赖 | `unity/Packages/manifest.json`（由锁文件生成） | `node tools/gen-manifest.mjs` | `node tools/gen-manifest.mjs --check` |
| targetSdk / minSdk | 锁文件 → `stores.targetSdk`=**36** / `minSdk`=**26** | 构建时由 `Whisper.Editor.BuildPipeline` 读取锁文件写入 | CI 从锁文件注入 `androidTargetSdkVersion` / `androidMinSdkVersion` |
| 16KB 页对齐 | 锁文件四件套（Unity/Firebase/Vivox/Burst） | 依赖版本已锁定即可满足 | `tools/arch-guard.mjs` 检查锁文件版本下限 |
| Billing v8 | 锁文件 → `stores.playBillingVersion`=8 | 后端 `verifyPurchase`（§15.1） | 规格书验收（P-B3） |

## 首次在有 Unity 的机器上要做的三件事（一次性）
1. 用 **Unity 6000.0.38f1** 打开 `unity/`，让它生成 `ProjectVersion.txt` 与 `ProjectSettings/*.asset`；
2. 确认 `Packages/manifest.json` 未被编辑器改写（若改写说明锁文件过期，改锁文件后重新生成）；
3. 把 Boot 场景（唯一手工场景，C1）与 `GameBootstrap` 组件落地，其余场景一律运行时拼装。

## 不要做的事
- ❌ 手工创建 `.asset` / `.unity` / `.meta` 文件（GUID 会错，Unity 打开即报错）
- ❌ 把 `Library/`、`Temp/`、`Logs/` 提交进仓库（见仓库根 `.gitignore`）
- ❌ 在 workflow 或 C# 里硬编码 Unity 版本与目标 SDK（必须读 `dependency-lock.json`）
