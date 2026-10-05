# 2026-10-05 续接会话 · 状态与下一步（真源：D:\DSH专用\whisper）

> 本文档是**当前门禁状态与最小修复路径**的记录。开工前先读
> `docs/handoff-2026-10-05.md` + `docs/spec/supplement-2026-10-05-permanent.md`。

## 1. 本轮做了什么（可复核）

### 1.1 修掉一个卡死所有后续工作的门禁红（已完成）

`bash tools/unity-syntax-check.sh` 原本 **exit=1 / 56 条真实错误**，其中 12 条是
**同一批类型被定义两次**：`Rect / Texture / Texture2D / RenderTexture / TextureFormat / FilterMode`
（`CS0101` ×6 + `CS0111` ×6，全部在 `native/unity-stubs/UnityStubs.cs`）。

**成因**：历史上 `tools/fix-stubs-*.mjs` 那批脚本是往文件**末尾追加**一整套更新的桩，
但没有删掉文件中部 249–275 行的旧版贫桩 → 同名类型出现两次。

**修法（合并，不是删一套）**：`tools/dedupe-unity-stubs.mjs`（新增，可重跑，带 `--check` 干跑）
- 删掉旧版 6 个冲突类型（含旧 `Rect`，105 行）；
- 把**旧版独有成员并入新版**（并集，不丢入口）：
  `RenderTexture.GetTemporary/ReleaseTemporary`、`Texture2D.SetPixels32/wrapMode`、
  `TextureWrapMode` 枚举、`TextureFormat.Alpha8/RGB24`、`Rect.Contains`；
- 收尾自检：7 个类型各恰好 1 个定义；
- 原文件备份 `native/unity-stubs/UnityStubs.cs.bak-dedupe`。

**结果**：`CS0101/CS0111` 归零，真实错误 **56 → 12**（34678 → 34776 字节）。

### 1.2 发现并纠正一个会误导后续会话的事实

`C:\Users\qing_\Documents\deepseek-harness\default-workspace\whisper` 是**旧的停滞快照**
（45 个 C# / 15 个 GLB），真源是 `D:\DSH专用\whisper`（80 个 C# / 32 个 GLB，设备上 0.1.71 由它构建）。
已在旧树写入 `whisper\真源已迁移-读这里.md` 并在旧交接文件头部加了纠正提示。

## 2. 当前门禁状态（本轮结束时的实测）

```
bash tools/unity-syntax-check.sh   → exit=1，真实错误 12 条（其余为允许的 Editor 侧缺失）
```

**12 条全部是「桩缺 API 成员」**，无一条是逻辑/语法问题，集中在上一轮新写的文件：

| 缺失入口 | 报错处 | 出处（官方文档） |
|---|---|---|
| `Mathf.Pow` | `PostFx.cs:54, 230` | ScriptReference/Mathf.Pow |
| `Mathf.Log` | `PostFx.cs:225, 226` | ScriptReference/Mathf.Log |
| `Camera.depthTextureMode` | `PostFx.cs:140`、`GameBootstrap.cs:475` | ScriptReference/Camera-depthTextureMode |
| `Camera.allowHDR` | `GameBootstrap.cs:433, 463, 475` | ScriptReference/Camera-allowHDR |
| `Material.SetVector` | `PostFx.cs:257, 259` | ScriptReference/Material.SetVector |
| `Light.renderMode` | `HallScene.cs:128` | ScriptReference/Light-renderMode |
| `Vector4`（类型本身缺失） | `PostFx.cs:257, 259` | ScriptReference/Vector4 |

### 最小修复路径（下一步就做这个）

1. 在 `native/unity-stubs/UnityStubs.cs` 的 `namespace UnityEngine` 里补上表内 7 个入口
   （`Vector4` 是 struct：`x,y,z,w` + 构造器 + 常用运算符；`Camera` 加 `allowHDR`/`depthTextureMode`
   两个属性；`Material` 加 `SetVector(string, Vector4)`；`Light` 加 `RenderMode` 枚举 + 属性；
   `Mathf` 加 `Pow`/`Log`）。
2. 同一文件里 `DepthTextureMode` 枚举**已存在**（`None/Depth/DepthNormals/MotionVectors`）——直接复用，不要新建。
3. 复跑 `bash tools/unity-syntax-check.sh` 应 exit=0；
4. 再跑 `bash tools/unity-tests.sh EditMode`（期望 **45/45**）——语法预检**抓不到跨文件/成员名错误**，
   真 Unity 编译才算过（见 handoff §0.4）。
5. 两道都绿之后，才进入用户指定的下一步：**`docs/spec/supplement-2026-10-05-permanent.md` §4 主界面 UI 收尾**
   （左侧地图投票板交互 / 右侧商店电脑可点开 / 右上 ID 卡详情 / 每日周挑战上板 / 地点↔主菜单↔商店快速跳转 /
   **废弃旧右侧竖排 8 按钮与左侧老面板** / 训练·选项·制作人员做成真面板）。
   ⚠ 用户原话纪律：**「不要再在旧界面上花时间」**——旧 UI 只做删除，不做美化。

## 3. 纪律提醒（本轮踩到的）

1. **`node` 的 `-e` 内联脚本在中文路径 + PowerShell 里会因引号层数静默崩**（本轮崩过一次，
   替换只做了一半）。写补丁脚本一律落成 `.mjs` 文件再跑，别用 `node -e`。
2. **写文件用 `write` 工具时，中文路径可能报 `ReplaceFileW EIO (Win32 1175)` 且文件停在旧版本**
   （`edit`/`write` 都可能）。对策：先写 ASCII 路径（如 `D:\dshr\...`）再 `Copy-Item` 拷回。
3. **PowerShell 控制台显示中文会乱码**（`✓/✗` 显示成 `鉁?`）。核对文件内容一律用 `read` 工具或读回文件，
   不要凭控制台输出下结论。
4. **`adb shell input tap` 注入点击时通时不通**（ColorOS 拦截）；判定"点击是否生效"先比对两次截图的 SHA256
   （见 handoff §N4）。不要因为注入无效就判定代码坏了。
5. **`adb exec-out screencap -p > file.png` 在 Windows 上会损坏 PNG**；改用
   `adb shell screencap -p /sdcard/x.png` 再 `adb pull`。
