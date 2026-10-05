# 手机端方案：能在手机上做什么、不能做什么、怎么搭

> 目标（用户 2026-10-05）：**编译转到手机上，工具在手机可调用，电脑关机也能用**。
> 本文件先给**能力边界**（有官方依据），再给**可落地的清单**与步骤。

## 一、能力边界（**官方原文，不是我推测**）

### ❌ Unity Editor **不能在手机上运行**
Unity 官方文档原文：
> "**While you can use Unity to build applications that run on many platforms,
> the Unity Editor requires Windows, macOS, or Linux.**"
> "**Unity will not run on a Chromebook or tablet.**"

来源：[Unity Essentials: Install Unity · System requirements](https://learn.unity.com/pathway/unity-essentials/unit/editor-essentials/tutorial/unity-essentials-install-unity?version=6.0)

补充（同一官方体系）：[Android requirements and compatibility](https://docs.unity3d.com/6000.3/Documentation/Manual/android-requirements-and-compatibility.html)
- Android **目标平台**支持：Vulkan · OpenGL ES **3.0/3.1/3.2**（1.x/2.0 不支持）
- 渲染管线：**Built-in ✓ · URP ✓ · HDRP ✗（不支持 Android）** · 自定义 SRP ✓
- **不支持 Android 模拟器**

⇒ **出 APK 这一步无法搬到手机上**。这是 Unity 厂商限制，不是我不会做。

### ✅ 能在手机上做的（且都是本项目的**实际工作重心**）
| 能力 | 手机可行性 | 依据 |
|---|---|---|
| **Blender 建模/渲染** | ✅ 可（Termux + proot Ubuntu + Blender；有成熟社区方案 [DroidDesk](https://github.com/orailnoor/DroidDesk)） | 本项目**所有套件与货车样板都是 Blender 产出** |
| **Node.js 工具链** | ✅ Termux `pkg install nodejs` | 本项目门禁全是 Node：`gen-map` / `gen-kits` / `gate-model` / `validate-levels` / `data-mirror` |
| **Python 工具** | ✅ Termux `pkg install python` | `device_state.py` 等 |
| **MCP 工具服务** | ✅ 取决于实现（见下） | `dsh-*` 11 个包 |
| **git 版本管理** | ✅ Termux `pkg install git` | 仓库本身 |

## 二、工具可移植性清单（**逐个核实过**）

| 工具 | 形态 | 手机可移植 | 依赖 |
|---|---|---|---|
| `dsh-blender` | Node + 调 Blender 可执行文件 | ✅ **可移植** | Blender（Termux proot 装）、Node |
| `dsh-readpage` | Node 取网页正文 | ✅ 可移植 | Node |
| `dsh-continuum` / `dsh-craft` / `dsh-focus` / `dsh-memory` / `dsh-graded-mode` | Node 纯逻辑/文件 | ✅ 可移植 | Node + 文件路径 |
| `dsh-remote-cpolar` | Node 网络 | ⚠️ 需核对（可能绑 Windows 服务） | — |
| `dsh-sh` | **PowerShell 常驻 shell** | ❌ **不可移植**（Android 无 PowerShell） | PowerShell |
| `dsh-super-injector` | 依赖 DSH 宿主的插件加载器 | ⚠️ 需宿主；**手机上没有 DSH 宿主** | DSH |

**关键结论**：`dsh-sh`（我最常用的快速执行工具）在手机上**没有对应物**；Android 的等价物是 **Termux `sh`**。

## 三、「电脑关机也能调用」的三条路径（**按可行性排序**）

### 路径 A：手机跑独立 MCP 服务端（**推荐，最接近你的要求**）
```
Termux → Node → 一个自建的 MCP 服务端（把可移植的 dsh-* 能力包成 MCP 工具）
       → 任何 MCP 客户端（含手机上的 AI App）都可调用，不依赖电脑
```
- **可行**：本项目已有 `dsh-blender` 这类"工具包即 MCP 服务"的现成结构可克隆；
- **我能做**：把可移植工具整理成一个手机版 MCP server 包 + 一键安装脚本；
- **限制**：不带 DSH 宿主（手机上没有），所以是"**独立 MCP 服务**"而非"完整 DSH"。

### 路径 B：手机当建模+门禁生产端，电脑只做最后出包
```
手机：Blender 建模 → 套件 GLB → Node 跑 gen-map / gate-model / validate-levels → git push
电脑（偶尔开机）：git pull → Unity 出 APK
```
- **优点**：**90% 的迭代（建模、关卡、门禁、文档）都在手机上完成**，电脑只需偶尔开机出包；
- **这是我认为最务实的方案** —— 因为它把"不能在手机上做的那一步"缩到最小。

### 路径 C：云端 CI 出包
```
手机 git push → GitHub Actions（GameCI 镜像）→ 产出 APK 下载
```
- **优点**：**电脑完全不用开**，真正满足"关机也能用"；
- **代价**：需要 GitHub 账号 + 仓库私有/公开设置 + Unity 许可证（`Unity_lic.ulf` 已在手机上！见设备上既有文件）；
- ⚠ 注意：**Unity 个人版许可证用于 CI 需确认合规**（我不能替你决定）。

**我的建议**：**B + C 组合** —— 日常在手机上做建模与门禁（B），出包走云端（C）。

## 四、手机端搭建步骤（Termux 侧）

```bash
# 1) 装 Termux（F-Droid 版，非 Play 商店版的旧版本）
# 2) 基础环境
pkg update && pkg upgrade -y
pkg install -y nodejs-lts python git openssh

# 3) 让 Termux 能访问共享存储（放工程的位置）
termux-setup-storage
# 之后 /sdcard 或 ~/storage/shared 指向 /storage/emulated/0

# 4) 解压工程（手机上看得到 zip 的路径）
cd ~/storage/shared/DSH专用/电脑上
unzip -q whisper-full.zip        # 或手机 MT 管理器解压
unzip -q dsh-mcp-tools.zip
unzip -q 建模知识.zip

# 5) 验证门禁可跑（**这是"手机能编译"的实证**）
cd whisper
node tools/gate-model.mjs
node tools/validate-levels.mjs
node tools/gen-map.mjs --layout tanglewood_v1 --out /tmp/t.json   # 试生成
```

### Blender（Termux proot）
```bash
pkg install -y proot-distro
proot-distro install ubuntu
proot-distro login ubuntu
# Ubuntu 内：
apt update && apt install -y blender python3-pip
blender --version
```
> ⚠ 手机跑 Blender 的**实际限制**：内存与散热。**轻量套件（货车这种 ~3k 面）可行**，
> 大场景渲染会很慢。建议手机只做**建模与导出**，渲染预览可降分辨率。

## 五、我接下来会交付的东西
1. **`手机端-一键安装.sh`** —— 放在 `电脑上/`，跑一遍配好 Termux 环境并自检
2. **`手机端-MCP服务/`** —— 把可移植工具包成一个独立 MCP server（路径 A）
3. **`手机端-说明.md`** —— 中文步骤 + 能力边界（就是本文档的手机版）

## 六、需要你确认的一件事
**路径 C（云端 CI）需要 GitHub 仓库**。你设备上已有 `GITHUB-SETUP.md` 与 `git.sh`，
且 `whisper` 里有 `.git`（6.2 MB）—— 看起来你已经在做这件事。
请告诉我：**是否已有可用的 GitHub 仓库/账号？** 若有，我可以把 CI 出包流程（`build-apk.yml`）一并写好；
若没有，我就专注做路径 A + B。
