# 建模交接（whisper 大厅模块 · 2026-10-05）

> 用户原话：「我优化了你的建模能力，接受一下」
> 以及更早的批评：「别人三角洲行动一个枪皮就几百MB的建模，你这么大个仓库几十KB？」

---

## 0. 本次真正改变了什么

**换方法，不是加数量。** 之前大厅是一个 C# 脚本摆 186 个 `CreatePrimitive(Cube)`（12 tri/个，
合计约 1 万 tri）+ 128px 程序化贴图 —— 天花板就是低模灰盒。

现在改用 **Blender 出真网格**，并按**官方阈值表**控制预算：

| 项 | 之前 | 现在 |
|---|---|---|
| 单个模块几何 | 12 tri（方块） | **220–1644 tri**（真实工业构件） |
| 有倒角 | 无 | Bevel 2 段 |
| 有工业细节 | 无 | 柱脚+加劲肋、货架冲孔+斜撑、管道法兰+螺栓、油桶箍圈+卷边、配电箱铰链+锁+线孔、木箱板缝+角铁 |
| 贴图 | 128px 程序化 | 待接（下一步：`blender_bake` 出 AO/Normal/ORM） |

---

## 1. 已交付：10 个工业模块（GLB，已在项目里）

位置：`D:\DSH专用\whisper\unity\Assets\Resources\Models\hall\`（**合计 516 KB**）
生成器：`D:\DSH专用\whisper\docs\modeling\gen_hall_modules.py`（可重跑）

| 模块 | tri | 预算(mobile building_module 200–1000) | GLB |
|---|---|---|---|
| Hall_IBeamColumn | 864 | ✅ | 60 KB |
| Hall_RackUpright | 264 | ✅ | 21 KB |
| Hall_RackBeam | 220 | ✅ | 18 KB |
| Hall_PalletWood | 704 | ✅ | 54 KB |
| Hall_LampIndustrial | 512 | ✅ | 17 KB |
| Hall_ElectricPanel | 688 | ✅ | 49 KB |
| Hall_Barrel | 872 | ✅ | 34 KB |
| Hall_DuctSection | 668 | ✅ | 76 KB |
| Hall_CrateWood | 924 | ✅（80 条非流形边来自叠板条壳，见 §4） | 73 KB |
| Hall_PipeFlange | 1084→1644 | ❌ 超 84–644（螺栓圈太密，见 §4） | 115 KB |

**网格卫生**（`dsh_blender_kit.mesh_health`）：工字钢柱 **quad_ratio 1.00、0 松顶点、
0 非流形边、0 退化面、0 高极点** —— 满分。其余模块同项均为 0，只有木箱与管道偏高。

---

## 2. 阈值表在哪（**不要凭感觉定预算**）

`C:\Users\qing_\Documents\blender-mcp\drivers\modeling-thresholds.json`

关键值（mobile）：

```
poly_budget_triangles.mobile:
  building_module      [200, 1000]     ← 大厅模块看这一行
  scene_small          [100, 500]
  handheld_prop        [300, 800]
texture.texel_density_px_per_m.mobile: 512      ← 密度门禁
texture.max_texture_edge.mobile: 2048
texture.glb_size_mb: { soft: 8, hard: 15 }
mesh_health: loose_vertices_max 0 / degenerate_faces_max 0 / pole_edges_fail 6
```

---

## 3. 怎么用能力包（实测可行的最小路径）

**关键事实**：`dsh-blender` 插件的 `blender_python` **不注入** `dsh_compat`/`dsh_kit`
（只有 `bpy` / `bmesh` / `mathutils` / `workspace` / `input_path` / `output_path`）。
但能力包就是磁盘上的一个 .py，**用 `runpy` 加载即可拿到全部 77 个入口**：

```python
import runpy
kit = runpy.run_path(r"C:\Users\qing_\Documents\blender-mcp\drivers\dsh_blender_kit.py")
kit["mesh_health"](obj)                      # 网格卫生
kit["texel_density"](obj, 1024)              # 纹素密度
kit["normalize_texel_density"](obj, 512.0, 1024)   # 归一到 512 px/m
kit["apply_material_preset"]([obj], "metal_painted")  # 13 个预设
kit["audit_scene"]("mobile", "building_module")       # 按门禁审计
kit["lod_chain"]([obj], (1.0, 0.5, 0.25))    # LOD 链
kit["lightmap_uv"](obj)                      # 第二套 UV（烘焙用）
kit["bake_maps"](obj, out_dir, maps=("AO","NORMAL","DIFFUSE"))
kit["finalize"](...)                         # 收尾（UV+平滑+导出+预览+QA）
```

**MCP 服务器**（31→39 工具）另有 `blender_audit` / `blender_build` / `blender_help` 等，
注册在 profile 级 —— **新会话自动加载**，本会话的工具清单里没有。
服务器启动（机器重启后必须做）：
```powershell
Start-Process powershell -ArgumentList '-ExecutionPolicy','Bypass','-File','C:\Users\qing_\Documents\blender-mcp\run-server.ps1' -WindowStyle Hidden
```
健康检查：`Invoke-RestMethod http://127.0.0.1:8765/health`
（`extraRoots` 含 `D:\DSH专用`，所以 **Blender 能直接读写项目目录**）

---

## 4. 已知问题（不粉饰）

1. **Hall_PipeFlange 超预算**（1644 tri vs 上限 1000）。原因：两端法兰各 8 颗螺栓 ×
   6 边圆柱。螺栓是"工业感"的关键，不能简单删 → 下一步把螺栓改成**法线贴图上的六角形**
   （几何只留 4 颗 + 贴图补），或做 LOD1（只留法兰轮廓）。
2. **Hall_CrateWood 有 80 条非流形边 / 64 个高极点**。来源是**四面板条 + 角铁均为独立闭合壳**
   （真实木箱的合理构造）。判定口径 `non_manifold_edges_max_print: 0` 是为 **3D 打印**设的，
   对游戏资产偏严。若门禁要过，需要 Boolean Union 合并壳（但会破坏 quad 拓扑）。
3. **还没做烘焙贴图**。当前 GLB 只有材质预设（颜色/粗糙/金属），没有 AO / Normal / ORM。
   这是"观感密度"的另一半 —— 下一步用 `kit["bake_maps"]` 出图，再进 Unity。
4. **还没接进 Unity**。GLB 已复制到 `Resources/Models/hall/`，但 `HallScene.cs` 仍在用
   `GameObject.CreatePrimitive`。替换工作未开始。

---

## 5. 下一步（优先级）

1. **烘焙**：`bake_maps(obj, maps=("AO","NORMAL","ORM"))` → 1024² → 存进 Resources
2. **LOD**：`lod_chain` 出 LOD1/LOD2，Unity 侧挂 `LODGroup`
3. **接进 Unity**：`HallScene.cs` 改成从 `Resources/Models/hall/*.glb` 加载并实例化
   （本工程已有 `GlbReader` + `ModelLibrary.InstantiateSingleFile`，可直接复用；
   注意 `Resources.Load` 路径**保留 `.glb`**，只剥 `.bytes`）
4. **合批**：仓库全是静态物 → `StaticBatchingUtility.Combine` 或共享材质 + GPU Instancing，
   把 draw call 压到方案门禁 **≤120**（L427）。当前 186 个物件 = 186+ draw call，**已超**
5. 补齐模块：楼梯踏步、栏杆立柱、门窗框、电缆桥架、消防栓、通风口、标牌

---

## 6. 复跑命令

```bash
# Blender 侧（本会话已验证）
#   生成 + 审计：whisper-models/gen_hall_modules.py
#   GLB 输出：   whisper-models/hall/glb/
#   展示渲染：   whisper-models/hall/showcase.png
# 项目侧
cd /d/dshr
ls unity/Assets/Resources/Models/hall/     # 10 个 GLB，516 KB
```
