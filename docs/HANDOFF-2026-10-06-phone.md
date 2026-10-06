# Whisper 项目交接（2026-10-06 · 手机侧会话）

> **给下一个接手的人（或下一个我）**：本文只写**能自证的事实**。
> 凡未验证的一律标注"未验证"；凡我判断错过的，把**错在哪**一并写下 —— 那比结论更有用。

---

## 〇、先读这三条（不知道会白干）

### 1. `DSH专用/` 里的取证目录：**用户会清理，别指望它长期留存**
实测（2026-10-06）：我建的 10 个取证文件夹（`取证-*` / `渲染取证-*` / `套件质量取证-*`）消失了。
**我最初推断成"该目录与电脑同步、会覆盖手机侧新建的东西"—— 这个推断是错的。**
**用户明确回复：「我删的」。**

**⇒ 正确的结论是**：用户会按自己的需要清理这个目录，**它不是一个归档处**。
**⇒ 不要把"必须留存"的产物只放在 `DSH专用/`。真源在 git**（`qw1179527/whisper`），
**要紧的东西先提交，再谈同步。**

**⇒ 另一条教训（关于我自己的）**：我**基于"文件夹不见了"这一个现象就写了一条推断，
还把它当成事实写进交接文档**。正确做法是先问或先标注"未验证"。
**这与本文件第五节反复强调的是同一个毛病：把推断当结论。**

### 2. 真源顺序（用户 2026-10-06 亲口变更）
1. **用户的直接指令**（最高）
2. **电脑端文档** `电脑上/01-工程源码/docs/` 与 `电脑上/05-建模知识/`（**22+ 份，我当初只读了 5 份，被用户指出**）
3. 仓库工程约定（`AGENTS.md`、各工具头部注释里的实测教训）

**PDF 方案（V5~V9）的全部要求已作废、退出真源序列。**
**没有 200MB 包体上限** —— 方向是高画质，包体上升是预期的，不许拿它卡构建。

⚠️ **一处歧义待用户裁决**：`资料1.md` 与 `d2ab709ec2dbcbf8_v5.md` **逐字节相同**（md5 `c73092bd…`）。
文件名带 `_v5` 而"作废序列"覆盖的是**计划类 PDF**；该文件内容却是**对标游戏的实况描述**。
我按"官方资料"采用并存于 `docs/reference-official/`（**不与任何代码耦合，整目录可删**）。

### 3. 用户给的 5 份官方资料（已收入 `docs/reference-official/`）
`01-建模画质UI菜单` · `02-完整机制道具猎杀鬼魂` · `03-官方道具与机制` ·
**`04-场景介绍初始界面与地图`** · `05-难度设置与鬼魂机制`
**它们描述的是对标游戏《恐鬼症》本身，不是我们的项目计划。**

---

## 一、当前状态（可复核）

```
仓库   HEAD b4ec862（交接提交后为最新）· 工作树干净 · 与 origin/main 同步
门禁   全链 20 步通过 · 1 步咨询 · 零失败   exit=0
      （那 1 步"咨询"= PDF 条文类，按用户要求降级为照跑照报、不判红）
      gate-test 通过 4 · 失败 0（真编译真跑，157 断言）
      gate-code 通过 8 · 失败 0 · gate-asset-bbox 通过 1 · 失败 0
云端   render-evidence / lobby-evidence / kit-visibility 三条取证管线均已跑通
      unity-android #36 权威构建成功
```

---

## 二、本会话做完的事（每条都有证据）

### A. 云端取证（第 5 档）打通 —— **这是本会话最重要的一件事**
`node tools/agent-task.mjs render-evidence` 产出真实 PNG 且日志含 `RENDER_EVIDENCE OK`。
**它立刻兑现了价值**：第一轮取证就抓出 **`WhisperUnlitColor.shader` 的片元只有 `return i.color;`（Unlit）**
⇒ **光照与手电对渲染零作用**。判红 29 → 18 → **0**。
一份靠光照营造氛围的恐怖游戏，氛围层整体缺失 —— 用户此前说的"好亮/上色不行"根子在此。

### B. 关卡几何从 Unlit 切到自研 PBR
`LevelBuilder.GeometryShader` 原先只找 `Whisper/UnlitColor`（不吃光的平涂）。
而**同一目录下早有完整的 `WhisperLitPbr.shader`（392 行）** —— 模型与大室早已迁到它，**只剩关卡没迁**。
切换前逐条核对过兼容性（要 NORMAL、**不要 TANGENT**（屏幕空间导数重建）、认 `Material.color`、
顶点色默认值安全）。回退链 `LitPbr → UnlitColor → 报错`。
**验证**：`RENDER_EVIDENCE OK` · 洋红 0.000% · 关灯+手电从"亮度 0.0 / 标准差 0.0"变成
**"亮度 25.6 / 标准差 19.9"** —— **手电有了真实光锥**（图见 commit `1a12c15`）。

### C. 那批"做完却从没用过"的 10 个大厅工业道具（**六重断链**）
用户反馈「谁家医院这样」「看着还是几十个版本前的样子」。查下来**不是渲染问题**：
```
① 文件在   Resources/Models/hall/Hall_*.glb             ✓
② 清单     asset-manifest.json 零登记                    ✗
③ 目录     KitMeshLibrary 读 Resources/Kits/，对不上      ✗
④ 代码     rg "Hall_IBeamColumn|Hall_Barrel|…" → 零命中   ✗
⑤ 扩展名   .glb 而非 .glb.bytes ⇒ Resources.Load 读不到   ✗
⑥ 轴向     export_yup=False（Z-up），契约要求 Y 是高度    ✗
```
来源警告：`电脑上/README-目录索引.md` 第 54 行
> 『这 10 个 GLB 是真源里没有的……只按"真源"思维整理会永久丢失』

**已逐环补齐**，并新增 `tools/fix-prop-axis.mjs`（Z-up→Y-up + 底面落到 y=0，**幂等**）。
**云端图证实木箱与金属货架已出现在画面里。**

### D. 调色板重做（每表面独立色相）
根因是**结构性的**：`Wall/Floor/Ceiling/Prop = Scale(ZoneBase(zone), k)` ——
**一个房间所有表面同一色相，只差明度**（压力区基色 `ColorMold #5C8C6E` ⇒ 墙 `#43664F`、地 `#273C2F`…）。
**⇒ 必然是"纯绿一片"，这就是"谁家医院这样"。**
改为表驱动 `SurfaceTable`（墙/地/顶/道具/门框各自独立色值），**只换色相、不动亮度**
（用户此前明确反馈过"好亮"），**保留分区情绪梯度**。
**已验证**：`lobby_entrance_lightOn.png` 里**地板是灰的**（原来绿）。

### E. 其他
· 规模超标 4 项清零（MenuScene 1891→213 · GameBootstrap 1176→519 · HallScene 722→578 · TryRead 145→~114）
· `gate-asset-bbox` 2 项清零（货车 GLB 轴系 · 入口层高 3.0→3.5）
· P0-3 点击回执系统（`ClickReceipt.cs` 落盘到 `persistentDataPath`）
· `LobbyEvidenceCapture.cs` —— **大厅第一次有了取证**（此前一张大厅的图都没有）
· `tools/agent-task.mjs` 取证图**自动同步**到 `DSH专用/`（但见第〇节第 1 条：该目录会吞）

---

## 三、未完成 / 下一步（按优先级）

### ① 货架摆放：**两次都没摆对，已回退 Box**（结论已确认，不是"待验证"）
```
第一次  立柱放 ±0.55m，而 Hall_RackBeam 长 2.406m ⇒ 跨度对不上
第二次  按"立柱间距 = 梁长"重算到 ±1.203、箱底落在梁顶
        ⇒ 云端 lobby_storage_lightOn.png 看图：【仍然是散的】立柱与横梁互不相连
```
**⇒ 真正的结论：问题不在间距算式，而在我不知道这批构件的【原点与朝向语义】。**
我只验证了包围盒（尺寸对、底面在 y=0），**没验证"哪一端是连接面"** ——
**靠包围盒摆装配件，本质上还是在猜。**

**已按项目纪律回退**（宁可少放，不放错）：
· 货架 → 回 Box 拼装（至少读得出是货架）
· **木箱 → 保留真实 GLB**（`lobby_storage_*.png` 明确显示木纹板条箱子是对的）

**下一步不是继续调坐标**，而是先拿到锚点定义：
读 `电脑上/09-工作区whisper独有存档/工作区whisper独有/tools/` 下的
`gen-hall-kits.mjs` / `kit-builder.py` / `lib/kit-hall.mjs`（**这批道具的生成脚本，含锚点约定**），
或把 `Hall_RackUpright` / `Hall_RackBeam` 单独渲染出来看清朝向，**再回来摆**。

### ② 其余工业道具还没接
`Hall_IBeamColumn`（结构柱）· `Hall_Barrel`（油漆桶）· `Hall_LampIndustrial`（顶灯）·
`Hall_ElectricPanel` / `Hall_DuctSection` / `Hall_PipeFlange`（墙边管道）· `Hall_PalletWood`（地面托盘）
—— 都已修好轴向、放进 `Resources/Kits/`，**只差在 `HallScene` 里摆**。

### ③ §三 建模精细重做（用户追加："把所有建模精细重做"）主体未做
已亲眼确认现状不够：`hall_main` 套件**无倒角、门洞只是方开口、材质纯色无纹理、无内饰层**。
**注意**：`资料4` §7.2 说官方精神病院是
**「Sunny Meadows 替代了原来的疯人院 (Asylum)」且「房间高度相似，极易迷路」是刻意设计**
—— 但用户明确说「**我说的重做是指我们地图重做，不是官方**」。
本工程持有的是 `asylum_v1.json`。

### ④ 大厅仍是 Box 拼（44 处 `Box(_root, …)`）
`hall_main_lobby` 在清单里但**被任何关卡引用数 = 0**（它就是为大厅造的，从没接上）。
**注**：用户看过图后认为大厅**比计划里说的好**（有工字钢、桁架、灯光池），
所以这一步可能是"针对性提升"而非"推倒重做"。

### ⑤ P0-3 后半 · ⑥ 道具四大类 · ⑦ 机制细化 · ⑧ 过渡动画与真人化角色

---

## 四、判据的真实覆盖范围（**不知道这个会误判"全绿"**）

| 判据 | 覆盖 | 可信度 |
|---|---|---|
| `gate-test` 本地真编译 | Core/Gameplay 的**子集**（`csharp-verify` 的 include 列表） | ⚠ **不覆盖 `Runtime/`** |
| 语法门禁 | 全量语法语义，但会把**真实错误**归入"允许的 Unity 缺失" | ⚠ **它报本工程类型名时必须用真编译交叉确认** |
| **GitHub Actions 真实构建** | **全量真实 Unity 编译** | ✅ **唯一权威** |

**本会话已因此漏报过一次真实构建失败**（拆分文件缺 using → `CS0246 'Progression' not found`，
本地显示"全链绿"）。**⇒ 动了 `Runtime/` 就必须等 Actions 结果。**

---

## 五、踩过的坑（**这一节比结论值钱**）

### 流程类
1. **`node -e` 里的反引号会被 bash 当命令替换** ⇒ 脚本静默不跑。**改写 `.mjs` 文件再跑。**
2. **"靠记得"一定会漏**。已因此出错：缺 using、`.meta` 改名顺序、orbit 相机高度、
   货架跨度、木箱高度。**⇒ 能做成数据/代码保证的，不要靠记。**
3. **改了东西必须检查它影响到的【全部】判据**。我改取景点时没看 orbit 那一项，
   让一个全黑的相机带红**两个** run 才发现。
4. **改了东西却没有能验证它的取景点，等于没改。** 接完道具后我才发现四个取景点都不覆盖货架区
   —— 补了 `storage` 取景点才看得到。**取景范围要与改动范围对齐。**
5. **批量改名后必须回读实际文件名核对。** 我把 `.meta` 改成 `X.glb.meta.bytes`（顺序反了），
   回读时才发现。
6. **抽方法时要回收它留下的变量声明**（`ResolveClick` 抽取后 4 条编译错误）。

### 资产/几何类
7. **摆构件前先抄下它的实测尺寸。** 连续两轮栽在同一坑：不读跨度摆货架、不读梁高放箱子。
8. **`glb-bbox.mjs` 的「Z-up/Y-up」标签是按最长轴猜的启发式，不可靠** ——
   它把货车标成"Z-up"，而货车 `Y=3.52`（高）本来就是正确的 Y-up（Z=9.15 是车长）。
   **⇒ 判轴向要按语义（高度/薄轴该在哪个轴）。**
9. **套件是 Y-up，`footprint=[宽(x),深(z)]`**；`gen-kits.mjs` 的 `export_yup=False` 是**错误的**，
   正确契约是 `export_yup=True`（见 `gen-hall-kits.mjs` 头部）。
10. **可进入结构用非凸 `MeshCollider`**（静态物体允许）—— 只有它能表达"有外壳、内有空腔"。
    **每个部件都要加**，否则玩家自由行走会**直接穿过去**。

### 工具自身会静默产出坏结果（**最危险的一类**）
11. **并行下载会写坏数据**：文件字节数与官方 `size_in_bytes` **完全一致**，但 zip 解不开
    （块写到了错误偏移）。**⇒ 关键产物一律 `--conn 1`**（已改为默认）。
    根因是判据只校验"每块都写过了"，**不校验写的位置与内容**。
12. **"字节数相等 ≠ 内容正确"** —— 已出现多次：il2cpp 静默退 1 · 306 字节产物 ·
    VNC 密码截断 · 编译错误被归入"允许"。
    **⇒ 每一条"通过"都要有内容级判据。**

### 我的误判（记下来供反驳）
13. **我把语法门禁报的 `CS0246` 当成"允许的 Unity 缺失"放过了**，还写进了提交说明
    —— 那是**真实的编译错误**（`Whisper.Gameplay.Progression` 是本工程类型）。**已修，但代价是一轮 CI。**
14. **我把用户的"地图重做"理解成"官方重做"** —— 用户要的是**我们自己的地图重做**。

---

## 六、关键文件与工具索引

```
tools/agent-task.mjs              派发 unity-agent 任务 → 下载产物 → 自动同步 DSH专用/
tools/fix-prop-axis.mjs           Z-up→Y-up + 底面落 0（幂等，本次新增）
tools/fix-truck-axis.mjs          同类工具（货车）
tools/fast-download.mjs           下载器（--conn 默认已改 1）
tools/glb-bbox.mjs                量 GLB 包围盒（⚠ 其 up 标签是启发式）
tools/gate-*.mjs                  各道门禁；unity-check.sh 是总链
unity/Assets/Editor/LobbyEvidenceCapture.cs   大厅取证（含 storage 取景点）
unity/Assets/Editor/RenderEvidenceCapture.cs  关卡取证
unity/Assets/Scripts/Runtime/ClickReceipt.cs  点击落盘回执（P0-3）
unity/Assets/Scripts/Gameplay/Level/LevelPalette.cs   表面色表（SurfaceTable）
unity/Assets/Scripts/Runtime/HallScene.Furnishing.cs   大厅陈设（道具摆放在这）
unity/Assets/Scripts/Gameplay/Level/KitMeshLibrary.cs  套件加载（按 id 读 Resources/Kits/<id>.glb）
docs/reference-official/          用户给的 5 份官方资料
docs/hall-props-orphaned-2026-10-06.md  六重断链的完整证据链
docs/session-handoff-2026-10-06.md      更早一版交接（部分内容已被本文取代）
```

**两条取证命令**（都要看图才算数）：
```bash
node tools/agent-task.mjs render-evidence   # 关卡（光照/雾/洋红/亮度）
node tools/agent-task.mjs lobby-evidence     # 大厅（含 storage 货架取景）
```

---

## 七、给接手者的一句话

**这个项目的失败模式不是"跑不起来"，而是"跑起来了、门禁全绿、结果是错的"。**
本会话抓到的每一个真缺陷（无光照、手电零作用、关卡不吃光、道具六重断链、货架悬空）
**门禁一个都没拦住** —— 全靠**看图**和**读真实构建结果**。

**⇒ 每改一样东西，问自己：我凭什么说它对了？**
如果答案是"门禁过了"，那还不够。
