# 接续清单（手机端 → 电脑端）

> 生成时间：2026-10-03 · 对应提交 `2e856d1`
> 这份文件的作用：在电脑端开局时，5 分钟内知道**做到哪了、下一步做什么、怎么验证**。

---

## 一、当前进度（已实测，不要重做）

| 模块 | 状态 | 证据 |
|---|---|---|
| Unity 工程结构 | ✅ 七 ASMDEF 齐备 | `find unity -name '*.asmdef'` → 10 个 |
| 玩法代码 | ✅ 40 源文件 / 5303 行 | Core/Gameplay 全部逻辑 |
| 本机断言 | ✅ **113 条全绿** | `bash unity-check.sh` → exit 0 |
| 四类门禁 | ✅ 建模 11 / 物理 4 / 代码 7 / 功能 3 | 各自 `--inject-*` 注入验证有效 |
| 关卡几何 | ✅ 内墙存在、门可通行、11 房间全可达 | 405/405 可走格单块连通 |
| 证据点可达性 | ✅ 5/5 落位，最近站位距离 0.00m | 断言 `证据点可达性（F-A 阻断项）` |
| CI 推送 | ✅ 已推到 GitHub | `github.com/qw1179527/whisper` |
| CI 构建 | ⚠️ 卡在 Unity 许可证 | 见第三节 |
| 灰盒版 | ✅ 可玩 | `DSH专用/whisper-graybox-0.7.5-dev.apk` |

---

## 二、电脑端开局（3 步）

```bash
# ① 恢复项目（迁移包里的 whisper/）
cp -a DSH-MIGRATION/whisper ~/whisper && cd ~/whisper

# ② 重建本机工具链
bash native/fetch-dotnet.sh          # .NET SDK（跑断言）

# ③ 验证环境（应全绿，否则先别往下走）
bash unity-check.sh                  # 期望：21 步 exit 0 · 113 条断言 0 失败
```

**若 ③ 报错**，把报错发我——不要带病往下做。

---

## 三、唯一阻塞项：Unity 许可证

### 已确认的事实（不要再重复排查）

| 结论 | 依据 |
|---|---|
| 账号 `qw1179527@qq.com` **存在且密码正确** | 激活日志：`Successfully updated the access token` |
| 该账号**没有任何 Unity 授权** | 激活日志：`Found 0 entitlement groups and 0 free entitlements` |
| 生成 `.alf` 的能力**已具备** | CI 跑通过：`Unity_v6000.3.25f1.alf`（816 字节） |
| 命令行激活**对个人版无效** | Unity 官方手册：个人版只能用 Hub |
| 手动激活（`.alf` 上传）**不支持个人版** | Unity 官方手册明示 |

### 电脑端要做的（约 5 分钟）

```
① 装 Unity Hub（直链，不走网页）：
   https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe
② Hub 里登录 qw1179527@qq.com
③ 齿轮 → Preferences → Licenses → Add → Get a free personal license
   ⚠️ 即使许可证已显示在列表里，也必须点 Add 走完，否则不生成 .ulf
④ 取 .ulf：
   Windows  C:\ProgramData\Unity\Unity_lic.ulf
   Mac      /Library/Application Support/Unity/Unity_lic.ulf
   Linux    ~/.local/share/unity3d/Unity/Unity_lic.ulf
```

拿到 `.ulf` 后二选一：
- **命令行写 Secret**：`GITHUB_TOKEN=<新令牌> node tools/gh/put-secret.mjs qw1179527/whisper UNITY_LICENSE --from-file Unity_lic.ulf`
- **网页填**：仓库 Settings → Secrets → Actions → `UNITY_LICENSE`

然后触发 `unity-android` 工作流即可出包。

### 或者：Hub 装不上时的备用路径

若 Hub 仍崩溃（手机端实测崩过），先做这两步再试：
1. 清配置：把 `%APPDATA%\UnityHub` 改名（不要直接删）
2. 以管理员身份运行

---

## 四、出包后立刻能做的事

```bash
# 构建产物在 build/Android/*.apk（CI 里作为 artifact 下载）
# 装到真机，对照验收：
#   · 能进关卡、看到几何 + HUD
#   · 走动撞墙正常（LevelGeometry.Resolve）
#   · 5 个证据点都能拿到
#   · 两个撤离点都能触发结算
```

---

## 五、还没做的（下一步开发顺序）

| 优先级 | 项 | 说明 |
|---|---|---|
| P0 | 玩家控制器 | `PlayerController.cs`：输入 → Resolve 碰撞 → 相机跟随 |
| P0 | 场景实体接线 | GameSession.Tick ↔ Unity 生命周期（位置/状态同步到 GameObject） |
| P1 | 怪物实例化 | 三怪预制体 + 移动解析注入 |
| P1 | HUD 实渲染 | HudModel → HudBuilder 接真 UI |
| P2 | C4 烘焙占位 | V9 §19.1 四项承诺里唯一未落地的 |
| P2 | 门禁遗留 | gate-physics 的 P1/P2 注入仍是伪造的；台账产物未重生成 |
| P3 | 联机/后端 | V9 §13.4/§15，需账号与预算决策 |

---

## 六、⚠️ 安全事项（务必先做）

**两个 GitHub 令牌与 Unity 密码在本项目的会话里出现过，已暴露：**

1. 撤销旧令牌：https://github.com/settings/tokens
2. 修改 Unity 账号密码：https://id.unity.com
3. 新令牌只放环境变量或 Secrets，**不要再粘进对话**

---

## 七、验证命令速查

```bash
bash unity-check.sh                  # 全链：21 步 / 113 断言
node tools/gate-model.mjs            # 建模门禁 11 项
node tools/gate-physics.mjs          # 物理规则门禁 4 项
node tools/gate-code.mjs             # 代码质量门禁 7 项
node tools/gate-test.mjs             # 功能测试门禁 3 项
node tools/gate-asset-bbox.mjs       # 资产几何真源 1 项
bash build.sh --quick                # 灰盒产物 V1~V6
node tools/nav-probe.mjs             # 怪物导航仿真探针
tools/git.sh <git 子命令>             # 本机 git 包装（修了 exec-path 与 CA 路径）
```
