# USB 传输完成记录（2026-10-05）

## 一、传输结果（**全部 MD5 校验一致**）

目标：`/storage/emulated/0/DSH专用/电脑上/`（设备 RMX5062 / 可用空间 272 GB）

| 文件 | 大小 | MD5 校验 |
|---|---|---|
| `whisper-full.zip` | **5,064,721,743 B（4.72 GB）** | ✓ `a780da82…` |
| `dsh-mcp-tools.zip` | 2,524,069 B | ✓ `5f9d0675…` |
| `手机端工具包.zip` | 4,388,900 B | ✓ `926fdc96…` |
| `建模知识.zip` | 4,378,908 B | ✓ `4177f022…` |
| `电脑上-目录说明.md` | 3,704 B | — |

## 二、为什么改用 zip（**这是一次真实的踩坑与修复**）

| 方案 | 结果 |
|---|---|
| ❌ **`adb push` 直传目录**（34528 个小文件） | **卡死**：60 秒只写入 3.5K，`adb` 进程白烧 63 秒 CPU；且**两次把常驻 shell 直接搞崩**（`code=null`） |
| ✅ **7z 打 zip 再传** | 打包 **33 秒**（8.74 GB → 4.72 GB）；传输 **162 秒 @ 29.9 MB/s**；**MD5 一致** |

**根因**：`adb push` 对目录是**逐文件建/写**，3 万多文件的元数据开销把传输彻底压死。
**结论（写进工具纪律）**：**往手机传大工程，一律先打包成单文件再传**。

## 三、手机端交付物（已随包送达）

| 交付物 | 作用 |
|---|---|
| `手机端工具包.zip` → `手机端-一键安装.sh` | Termux 一键：装 nodejs/python/git/unzip · 开通共享存储 · 解压三个包 · **跑本项目 Node 门禁自检** |
| 同上 → `工具/phone-mcp-server.mjs` | **手机版 MCP 服务**（stdio · **零 npm 依赖**），5 个工具：`read_file` `write_file` `list_dir` `run_shell` `fetch_url`。已本机自检通过 |
| 同上 → `phone-build-environment-plan.md` | 手机端能力边界与三条路径（A 独立 MCP / B 手机建模+门禁 / C 云端 CI 出包） |
| `建模知识.zip` | 15 份文档（官方资料 5 份 · 建模方法 4 份 · 货车样板 3 份 · 规则与清单 3 份）+ 9 个样板文件（4 个 .blend + 5 张渲染图） |

## 四、**手机端能力边界（官方依据）**

### ❌ Unity Editor 不能在手机上运行 —— **出 APK 无法搬到手机**
Unity 官方原文：
> "the **Unity Editor requires Windows, macOS, or Linux**"
> "**Unity will not run on a Chromebook or tablet.**"

来源：[Unity Essentials: Install Unity · System requirements](https://learn.unity.com/pathway/unity-essentials/unit/editor-essentials/tutorial/unity-essentials-install-unity?version=6.0)

另据 [Android requirements and compatibility](https://docs.unity3d.com/6000.3/Documentation/Manual/android-requirements-and-compatibility.html)：
Android 目标平台支持 Vulkan 与 GLES 3.0+；**Built-in ✓ · URP ✓ · HDRP ✗**；**不支持 Android 模拟器**。

### ✅ 手机能承担（都是本项目实际工作重心）
| 能力 | 依据 |
|---|---|
| **Blender 建模/渲染** | 本项目**所有套件与货车样板都是 Blender 产出**（`gen-kits.mjs` 生成 Blender 脚本 → GLB） |
| **Node 门禁全套** | `gen-map` / `gen-kits` / `gate-model` / `validate-levels` / `data-mirror` 全是 Node |
| **Python 工具** | `device_state.py` 等 |
| **MCP 服务** | 已交付 `phone-mcp-server.mjs` |

## 五、待你答复一件事
**路径 C（云端 CI 出包）需要 GitHub 仓库** —— 这是唯一能让「电脑完全关机也能出包」的方案。
设备上已有 `GITHUB-SETUP.md`、`git.sh`，`whisper` 里也有 `.git`（6.2 MB），看起来你已在这条路上。
**请告诉我：是否已有可用的 GitHub 仓库/账号？** 有的话我把 `build-apk.yml`（GameCI 出 APK）写好一并交付。
