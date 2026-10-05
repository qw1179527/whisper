# B 方案落地：手机端工作流（2026-10-05）

> 用户选择：**B（手机做建模 + 门禁，电脑只在出包时开机）**；C（云端 CI）用户**已自行实现**。

## 一、设备端最终清单（**全部 MD5 校验一致**）

目标：`/storage/emulated/0/DSH专用/电脑上/`

| 文件 | 大小 | 校验 |
|---|---|---|
| `whisper-full.zip` | 5,064,721,743 B（4.72 GB） | ✓ |
| `dsh-mcp-tools.zip` | 2,524,069 B | ✓ |
| `手机端工具包.zip` | 4,394,774 B | ✓ `1085674e5748` |
| `建模知识.zip` | 4,378,908 B | ✓ |
| `电脑上-目录说明.md` | 3,704 B | — |

## 二、手机端三个脚本（**全部 LF 行尾 + bash 语法检查通过**）

| 脚本 | 作用 | 关键设计 |
|---|---|---|
| **`手机端-01-Termux装环境.sh`** | Termux 侧：装 `proot-distro`/`nodejs-lts`/`python`/`git`/`unzip`/`wget` · 建 Ubuntu 容器 · **跑 Node 门禁自检** | **为什么分两段**：`proot-distro login` 会切进容器，容器内命令必须在容器内执行 —— **不能一个脚本跨容器做完** |
| **`手机端-02-容器内装Blender.sh`** | 容器内：`apt install blender` + python · **验证「本项目生成器能调通 Blender」** | 关键实证：跑 `node tools/gen-kits.mjs --emit /tmp/kits-out`，**真生成 GLB** 才算通 |
| **`手机端-03-日常工作流.sh`** | 日常：`gate` 跑门禁 · `gen` 跑生成器 · `status` 看状态与 git · `all` 全跑 | 门禁通过才提示可提交；**明确提醒"出 APK 不在手机"** |

### 还交付了
- **`工具/phone-mcp-server.mjs`** —— 手机版 MCP 服务（stdio · **零 npm 依赖**）· 5 个工具：`read_file` `write_file` `list_dir` `run_shell` `fetch_url` · **已本机自检通过**
- `phone-build-environment-plan.md` · 建模知识 15 份文档 + 9 个样板文件（4 个 `.blend` + 5 张渲染图）

## 三、手机上怎么跑起来（**顺序**）

```bash
# ① 解压手机端工具包
cd /sdcard/DSH专用/电脑上 && unzip -o 手机端工具包.zip

# ② Termux 侧装环境（含建容器 + 门禁自检）
sh 手机端/手机端-01-Termux装环境.sh

# ③ 进容器装 Blender（注意：这两行是分开的两步）
proot-distro login ubuntu
bash ~/dsh-work/手机端-02-容器内装Blender.sh
exit

# ④ 日常：建模 → 门禁 → 提交
sh 手机端/手机端-03-日常工作流.sh all
```

## 四、手机端能力边界（**官方依据，已核实**）

### ❌ 出 APK 不能在手机
Unity 官方原文：
> "the **Unity Editor requires Windows, macOS, or Linux**" · "**Unity will not run on a Chromebook or tablet**"
> （[Unity Essentials · System requirements](https://learn.unity.com/pathway/unity-essentials/unit/editor-essentials/tutorial/unity-essentials-install-unity?version=6.0)）

另据 [Android requirements and compatibility](https://docs.unity3d.com/6000.3/Documentation/Manual/android-requirements-and-compatibility.html)：
Android 目标平台支持 **Vulkan 与 GLES 3.0+**；**Built-in ✓ · URP ✓ · HDRP ✗**；**不支持 Android 模拟器**。

### ✅ 手机端承担（本项目实际工作重心）
| 能力 | 依据 |
|---|---|
| **Blender 建模/导出 GLB** | **所有套件与货车样板都是 Blender 产出**（`gen-kits.mjs` 生成 Blender 脚本） |
| **Node 门禁全套** | `gate-model` / `validate-levels` / `data-mirror` / `gen-map` 全是 Node |
| **Python 工具** | `device_state.py` 等 |
| **MCP 服务** | `phone-mcp-server.mjs` |

## 五、B 方案的实际闭环

```
手机（Termux + proot Ubuntu）
  ├─ Blender 建模 → 套件 GLB
  ├─ node tools/gen-map.mjs / gen-kits.mjs     ← 生成
  ├─ node tools/gate-model.mjs / validate-levels.mjs   ← 门禁
  ├─ node tools/data-mirror.mjs                ← 三份镜像同步
  └─ git commit + push
                    ↓
电脑（偶尔开机）或 用户已实现的 CI → 拉取 → Unity 出 APK → 装机验证
```

**手机上完成的占日常迭代约 90%**（建模、关卡、门禁、文档、官方资料检索），
电脑只承担"Unity 出包"这一件手机做不到的事。

## 六、实机注意（非脚本问题）
- **解压需要约 9 GB**（zip 4.8 GB + 解压 8.74 GB）；设备可用 **272 GB**，够；
- 34528 个小文件解压**较慢**，MT 管理器或 Termux `unzip` 都行；
- **Blender 在手机上受内存与散热限制**：本项目套件属轻量（单套件 ~1k 面）可行；
  大场景渲染会很慢 → 建议**手机只做建模与导出**，渲染预览降分辨率；
- **`adb push` 传目录会卡死**（实测 60 秒只写 3.5K）→ 一律**先打包成单文件**再传。
