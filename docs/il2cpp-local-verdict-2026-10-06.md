# 判决：本机 IL2CPP 探索 —— 暂停（2026-10-06）

## 起因
用户问："能不能本机实现 IL2CPP 编译从而减少时间"。

被否决的是"手机出包取代 CI"；本探索是**另一件事** —— 把 CI 里最贵那一步（IL2CPP 实测占
14.7/21.4 分）搬到手机上做**增量**，产物仍是 IL2CPP + ARM64（**不违反"出货形态不降级"**）。

## 已确认可行的部分（有实测证据）
| 环节 | 证据 |
|---|---|
| `il2cpp-compile.dll` 能在手机 .NET 8 上跑 | 轨迹 `Launch host: … app: il2cpp-compile.dll`；框架解析为 `Microsoft.NETCore.App.Runtime.linux-bionic-arm64, 8.0.31` |
| 从 Windows 包取工具链不可行 | NSIS，7-Zip 在 Linux 与手机上都解不开 |
| 正解是 Linux `TAR_XZ` | 已成功提取 `Editor/Data/{Managed,il2cpp}`（`extract-unity-tools` v3 成功） |
| Unity 的 Android 构建管线**本身是 .NET 程序** | 构建日志：`netcorerun … AndroidPlayerBuildProgram.exe`、`netcorerun … ScriptCompilationBuildProgram.exe` |
| `UnityLinker` 是 .NET 程序且已在手 | `il2cpp/build/deploy/UnityLinker` |
| 那批 `.rsp` 是 **Roslyn 的**，不是 il2cpp 的 | 内容是 `-target:library -define:UNITY_6000_3_25 …`；DAG 里**只有引用程序集路径，没有 il2cpp 可执行调用** ⇒ **IL2CPP 不走 Bee，是 Editor 直接驱动的** |

## 卡住的地方（三次尝试，如实记录）
```
① 从基础编辑器包里取 PlaybackEngines/AndroidPlayer/*  →  取出 0 个文件
   原因：Linux 编辑器的基础 tar.xz【不含 Android Player】，它是单独的模块包
        （unity-android.yml 能构建，是因为 GameCI 的 Docker 镜像已把模块装好，掩盖了这点）
② Unity 官方发布 API（services.api.unity.com/.../releases?version=6000.3.25f1）
   → 只返回 5 项，**全是编辑器**（LINUX/MAC×2/WINDOWS×2），**没有模块下载**
③ 四个候选命名（UnitySetup-Android-Support-for-Editor-*.tar.xz 的四种路径）→ 全部失败
```

**⇒ 真正的阻塞是"Android 模块包的确切下载地址未知"，而不是"技术上做不到"。**

## 未回答的问题
`AndroidPlayerBuildProgram` 是**托管 IL 还是原生 x86_64**？这决定整条链在手机上是否可行。
（同类程序 `il2cpp-compile.dll` 已证实是托管的，所以乐观，但**未验证就不写成通过**。）

## 下一步若重启这条线，最可能成功的入口
**从 GameCI 的 Docker 镜像里取** —— 那个镜像**确定**含 Android 模块：
```yaml
container:
  image: unityci/editor:ubuntu-6000.3.25f1-android-3
# 然后 find /opt/unity/Editor/Data/PlaybackEngines/AndroidPlayer -name 'AndroidPlayerBuildProgram*'
```
**但本轮不继续**：用户选定的重点是"彻底不用管"，而这一点**已经达成**
（云端取证 + CI 出包 + 8 并行下载 + 门禁链都在跑）。IL2CPP 本机化是"省时间"的锦上添花，
按目标里的"修到能用即停，不再往工具上加新档位"，**停在这里**。
