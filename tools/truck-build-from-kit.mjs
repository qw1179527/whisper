#!/usr/bin/env node
/**
 * truck-build-from-kit.mjs — 第三步：`TruckScene` 改为**用已登记的套件**装配（几何 + 真实材质）
 *
 * ## 数据（实测自 `truck_eurocargo.glb`，不是猜）
 * ```
 * materials:
 *   [0] chassis_metal  base=(0.200,0.210,0.230)  metal=0.90  rough=0.45
 *   [1] body_paint     base=(0.700,0.710,0.730)  metal=0.10  rough=0.32
 *   [2] trim_plastic   base=(0.100,0.100,0.120)  metal=0.00  rough=0.70
 *   [3] glass          base=(0.060,0.080,0.110)  metal=0.00  rough=0.15
 *   [4] plate          base=(0.920,0.920,0.880)  metal=0.00  rough=0.40
 *   [5] tyre_rubber    base=(0.030,0.030,0.035)  metal=0.00  rough=0.95
 * primitive → material 索引 = [0,1,2,3,4,5]   ← **一一对应**（Blender join 按材质槽拆 primitive）
 * ```
 *
 * ## 材质族映射（产品的 `MaterialFamily` 没有玻璃/橡胶族，实测只有
 *    Plaster / Concrete / Wood / Metal / RustMetal / Tile / Fabric）
 * | GLB 材质 | 产品族 | 理由 |
 * |---|---|---|
 * | chassis_metal | Metal | 强金属（0.90） |
 * | body_paint | Metal | 微金属车漆（0.10） |
 * | trim_plastic | Fabric | 非金属、粗糙 0.70 —— 最接近"哑光塑料"的既有族 |
 * | glass | **Tile** | 光滑平整面（本仓已在第 25 轮用同一判断处理过货车玻璃） |
 * | plate | Metal | 反光车牌 |
 * | tyre_rubber | Fabric | 非金属、极粗糙 0.95 |
 * 映射**按材质名**（不按下标）—— 下标会随导出顺序变，名字是稳定的。
 *
 * ## 行为
 * · 优先用套件；**套件缺失/解析失败则回退到原来的 Cube 拼装**（保留可玩性，不静默失败）；
 * · `Truck_SelfTest()` 暴露一行诊断（几何来源 + 部件数 + 材质数），供 HUD 关闭时落盘取证。
 *
 * 用法：node tools/truck-build-from-kit.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/TruckScene.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[truck-kit] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('TryBuildFromKit')) { console.log('[truck-kit] 已应用，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 装配顺序：先试套件，失败再走原来的程序化几何 ────────────────────
sub('            BuildBox(body, metal, glass);',
  `            // ── 优先用**已登记的套件**装配（几何 + 真实 PBR 材质）────────────────
            // 为什么优先套件：套件是在 Blender 里参数化建的（带倒角、32 边轮胎、侧板竖筋、
            // 挡泥板、后视镜、导流罩），并经过 gate-model M9/M10 校验；而下面的程序化路径
            // 只有 Cube + 3 种材质。套件缺失时**回退**程序化（不静默失败、不空白）。
            if (TryBuildFromKit()) { KitBuilt = true; }
            else
            {
            BuildBox(body, metal, glass);`,
  '① 优先套件装配');

sub(`            BuildInterior(metal, glass);
`, `            BuildInterior(metal, glass);
            }
`, '② 闭合套件分支');

// ── ③ 新增 TryBuildFromKit 与诊断 ─────────────────────────────────────
sub('        /// <summary>局部 → 世界。</summary>',
  `        /// <summary>本车几何是否来自已登记的套件（false = 走了程序化回退）。</summary>
        public bool KitBuilt { get; private set; }

        /// <summary>套件装配诊断一行（HUD 关闭时落盘取证用）。</summary>
        public string SelfTest()
            => KitBuilt
               ? $"货车几何：套件 {KitId}（部件 {_kitParts} · 材质 {_kitMats}）"
               : $"货车几何：**程序化回退**（套件 {KitId} 不可用）";

        const string KitId = "truck_eurocargo";
        int _kitParts, _kitMats;

        /// <summary>
        /// 用套件装配货车（几何来自 GLB，材质按**材质名**映射到本产品的 MaterialFamily）。
        /// 返回 false 表示套件不可用 —— 调用方应回退程序化几何。
        /// </summary>
        bool TryBuildFromKit()
        {
            var parts = Whisper.Gameplay.Level.KitMeshLibrary.GetParts(KitId);
            if (parts == null || parts.Length == 0) return false;
            var matIdx = Whisper.Gameplay.Level.KitMeshLibrary.GetPartMaterials(KitId);
            var kitMats = Whisper.Gameplay.Level.KitMeshLibrary.GetMaterials(KitId);
            _kitParts = parts.Length;
            _kitMats = kitMats != null ? kitMats.Length : 0;

            // 套件顶点已在"世界坐标"（构建时应用了 node 变换），故直接置于本车原点下。
            // 高度对齐：套件脚底在 y=0，与本车 FloorHeight 语义一致（样板即按此建的）。
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                var go = new GameObject($"TruckKit_{i}");
                go.transform.SetParent(_root, false);
                go.transform.localPosition = Vector3.zero;
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = parts[i];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialForPart(matIdx, kitMats, i);
            }
            return true;
        }

        /// <summary>
        /// 第 i 个部件的材质：优先用套件自带 PBR 参数，缺则用中性灰兜底。
        /// 映射**按材质名**（下标会随导出顺序变，名字稳定）。
        /// </summary>
        static Material MaterialForPart(int[] matIdx, Whisper.Gameplay.Level.GlbReader.KitMaterial[] mats, int part)
        {
            if (matIdx == null || mats == null || part >= matIdx.Length) return Fallback();
            int mi = matIdx[part];
            if (mi < 0 || mi >= mats.Length) return Fallback();
            var km = mats[mi];
            var col = new Color(km.R, km.G, km.B, 1f);
            var fam = FamilyOf(km.Name);
            return Whisper.Runtime.SceneMaterials.Lit(col, Mathf.Clamp(km.Roughness, 0.05f, 1f), fam);
        }

        static Material Fallback()
            => Whisper.Runtime.SceneMaterials.Lit(new Color(0.5f, 0.5f, 0.52f), 0.6f,
                   Whisper.Gameplay.Render.MaterialFamily.Metal);

        /// <summary>
        /// 套件材质名 → 本产品材质族。产品族没有玻璃/橡胶（实测：Plaster/Concrete/Wood/Metal/
        /// RustMetal/Tile/Fabric），按下表就近映射；未知名字落 Metal（中性、不会出现怪色）。
        /// </summary>
        static Whisper.Gameplay.Render.MaterialFamily FamilyOf(string name)
        {
            switch (name)
            {
                case "chassis_metal": return Whisper.Gameplay.Render.MaterialFamily.Metal;
                case "body_paint":    return Whisper.Gameplay.Render.MaterialFamily.Metal;
                case "plate":         return Whisper.Gameplay.Render.MaterialFamily.Metal;
                case "glass":         return Whisper.Gameplay.Render.MaterialFamily.Tile;    // 光滑平整面
                case "trim_plastic":  return Whisper.Gameplay.Render.MaterialFamily.Fabric;  // 哑光非金属
                case "tyre_rubber":   return Whisper.Gameplay.Render.MaterialFamily.Fabric;  // 极粗糙非金属
                default:              return Whisper.Gameplay.Render.MaterialFamily.Metal;
            }
        }

        /// <summary>局部 → 世界。</summary>`,
  '③ 新增 TryBuildFromKit / 材质映射 / 诊断');

if (!checkOnly) {
  const bak = FILE + '.bak-truckkit';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[truck-kit] 货车改为套件装配' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
