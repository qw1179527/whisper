#!/usr/bin/env node
/**
 * wire-truck-into-hall.mjs — 把货车接进大厅（否则 TruckScene 只是死代码）
 *
 * ## 为什么需要这一步
 * `TruckScene.cs` 建好了整车几何（驾驶室/厢体/坡道/车轮/指挥区/装备区），
 * 但**没有任何地方 new 它** —— 那等于没做。
 *
 * ## 关键：材质必须在本方法内现取
 * HallScene 里的材质**全是方法内局部变量**（`var boardMat = Mat(...)` 之类），没有任何材质字段。
 * 所以不能像第一版那样写 `_matBody`（那是我凭猜的字段名 —— 语法门禁抓不到，EditMode 必红）。
 * 正解：新增 `BuildTruck()` 方法，内部用类里已有的 `Mat(Color, roughness, MaterialFamily)` 现取材质，
 * 调用点只写一行 `BuildTruck();`。
 *
 * ## 停在哪
 * 大厅是工业风两层仓库（WidthM × LengthM）。菜单板在前墙（-Z）、地图板在左、商店电脑在右。
 * 货车停在**左侧靠前空地**、车头朝 +X（横停），车尾朝向出生点方向，便于从车尾上车。
 *
 * ## 大厅 vs 局内
 * §8 说"大厅等待时货车也停在那里，但除 00:00 计时器外所有屏幕关闭" ——
 * 故大厅这辆是**熄屏待命**；局内复用同一实例（`Truck` 属性）。
 *
 * 用法：node tools/wire-truck-into-hall.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/HallScene.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[truck] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('BuildTruck')) { console.log('[truck] 已接入，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 属性 ────────────────────────────────────────────────────────────
sub('        GameObject _pickPlate;',
  `        GameObject _pickPlate;

        /// <summary>
        /// 货车（移动基地/安全指挥中心，用户《补充说明》§8）。
        /// 大厅与局内**共用同一辆**：大厅里熄屏待命（除 "00:00" 计时器），局内是命令区+装备区+安全区。
        /// 共用同一实例，避免"两处各建一份慢慢漂移"（本项目在 UI 上已踩过这个坑）。
        /// </summary>
        public TruckScene Truck { get; private set; }`,
  '① HallScene 暴露 Truck 属性');

// ── ② BuildTruck 方法（材质在本方法内现取）──────────────────────────
{
  const anchor = '        void BuildMenuBoard()';
  const i = s.indexOf(anchor);
  if (i < 0) fail('未找到 BuildMenuBoard 锚点');
  const block = [
    '        /// <summary>',
    '        /// 建货车（§8）。材质在本方法内现取 —— HallScene 的材质都是方法内局部变量，',
    '        /// 没有可供外部引用的材质字段（第一版我凭猜写了 `_matBody`，语法门禁抓不到、EditMode 才红）。',
    '        /// 位置：左侧靠前空地、车头朝 +X（横停），车尾朝出生点方向便于上车。',
    '        /// </summary>',
    '        void BuildTruck()',
    '        {',
    '            var bodyMat = Mat(new Color(0.62f, 0.63f, 0.66f), 0.42f, MaterialFamily.Metal);   // 白色厢式车漆（Eurocargo 常见涂装）',
    '            var metalMat = Mat(new Color(0.28f, 0.29f, 0.31f), 0.55f, MaterialFamily.Metal);  // 底盘/轮毂/键盘',
    '            var glassMat = Mat(new Color(0.12f, 0.16f, 0.18f), 0.15f, MaterialFamily.Metal);  // 车窗与屏幕（暗、微反光）',
    '            float tx = -WidthM * 0.30f;',
    '            float tz = -LengthM * 0.08f;',
    '            Truck = new TruckScene(_root, new Vector3(tx, 0f, tz), 90f, bodyMat, metalMat, glassMat);',
    '        }',
    '',
  ].join('\n');
  s = s.slice(0, i) + block + s.slice(i);
  log.push('  ✓ ② 新增 BuildTruck()（材质本方法内现取）');
}

// ── ③ 调用点：放在建菜单板之后（此时相机与灯光都已就绪）──────────────
sub('            MenuBoardPos = new Vector3(0f, by, z - 0.05f);',
  `            MenuBoardPos = new Vector3(0f, by, z - 0.05f);

            // 货车（§8）：大厅里停着待命，局内复用同一实例。放在建板之后，确保灯光/相机已就绪。
            BuildTruck();`,
  '③ 调用 BuildTruck()');

if (!checkOnly) {
  const bak = FILE + '.bak-truck';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[truck] 货车接入大厅' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
