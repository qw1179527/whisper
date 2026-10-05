#!/usr/bin/env node
/**
 * fix-safezone-layering.mjs — 安全区在玩法层用裸浮点，不用 UnityEngine.Vector3
 *
 * ## 真编译报的（本轮第二次靠 EditMode 兜住语法门禁的漏网错误）
 * ```
 * GameSession.cs(69,16): error CS0246: The type or namespace name 'Vector3' could not be found
 * …(这属于程序集 Whisper.Gameplay)
 * ```
 * **`Whisper.Gameplay` 程序集不引用 UnityEngine**（它只做纯逻辑：理智、猎杀、证据、结算），
 * 而我在 `GameSession` 里加了 `Vector3 TruckSafeCenter/TruckSafeSize` —— 破了分层。
 *
 * ## 这条发现本身很有价值（写进注释）
 * 本项目的分层是真的：**玩法层零 UnityEngine 依赖**。所以：
 * · Runtime 层（有 Unity）持有几何（货车/AABB/相机）；
 * · 玩法层只收**裸浮点**（`SafeCenterX/Z`、`SafeSizeX/Z`）——
 *   这也解释了为什么 `SanitySystem.Tick(..., inSafeZone: bool, ...)` 收的是个 bool 而不是坐标：
 *   分层从设计上就把"几何判断"留在 Unity 侧，把"后果"留在玩法侧。
 *
 * ## 修法
 * `GameSession` 的四个字段改成裸浮点；`PlayerInSafeZone` 与 `MonsterMayEnter` 用裸浮点算。
 * Runtime 侧的 `GameBootstrap.SetTruckSafeZone(Bounds)` 保留（那里有 Unity，可以用 Bounds），
 * 它把 Bounds 拆成四个浮点后再转交给玩法层。
 *
 * 用法：node tools/fix-safezone-layering.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[layer] ✗ ' + m); process.exit(1); };

function edit(rel, pairs, tag) {
  const p = path.join(ROOT, rel);
  let s = fs.readFileSync(p, 'utf8');
  for (const [from, to] of pairs) {
    const n = s.split(from).length - 1;
    if (n !== 1) fail(`${rel}：锚点命中 ${n} 次（应为 1）→ ${from.slice(0, 70)}`);
    s = s.replace(from, to);
  }
  if (!checkOnly) fs.writeFileSync(p, s, 'utf8');
  log.push('  ✓ ' + tag);
}

// ── GameSession：Vector3 → 裸浮点 ────────────────────────────────────
edit('unity/Assets/Scripts/Gameplay/Session/GameSession.cs', [[
  `        public Vector3 TruckSafeCenter { get; set; } = new Vector3(0f, -1000f, 0f);
        public Vector3 TruckSafeSize { get; set; } = Vector3.zero;`,
  `        /// ⚠ 这里的类型是**裸浮点而不是 Vector3**：\`Whisper.Gameplay\` 程序集**不引用 UnityEngine**
        /// （实测 CS0246：'Vector3' could not be found）。这是本项目的分层约定 ——
        /// **玩法层零 Unity 依赖**：几何判断留在 Runtime（有 Unity）侧，玩法层只收数值与 bool。
        /// 也因此 \`SanitySystem.Tick(..., inSafeZone: bool, ...)\` 收的是 bool 而不是坐标。
        public float TruckSafeCenterX { get; set; }
        public float TruckSafeCenterZ { get; set; } = -1000f;   // 默认远在地底 → 任何点都不在内（fail-safe）
        public float TruckSafeSizeX { get; set; }
        public float TruckSafeSizeZ { get; set; }`,
]], 'GameSession 字段改裸浮点');

edit('unity/Assets/Scripts/Gameplay/Session/GameSession.cs', [[
  `                if (TruckSafeSize.x <= 0f || TruckSafeSize.y <= 0f || TruckSafeSize.z <= 0f) return false;
                float dx = Math.Abs(PlayerX - TruckSafeCenter.x);
                float dz = Math.Abs(PlayerZ - TruckSafeCenter.z);
                return dx <= TruckSafeSize.x * 0.5f && dz <= TruckSafeSize.z * 0.5f;`,
  `                if (TruckSafeSizeX <= 0f || TruckSafeSizeZ <= 0f) return false;
                float dx = Math.Abs(PlayerX - TruckSafeCenterX);
                float dz = Math.Abs(PlayerZ - TruckSafeCenterZ);
                return dx <= TruckSafeSizeX * 0.5f && dz <= TruckSafeSizeZ * 0.5f;`,
]], 'PlayerInSafeZone 用裸浮点');

edit('unity/Assets/Scripts/Gameplay/Session/GameSession.cs', [[
  `            if (TruckSafeSize.x <= 0f || TruckSafeSize.z <= 0f) return true;
            float dx = Math.Abs(x - TruckSafeCenter.x);
            float dz = Math.Abs(z - TruckSafeCenter.z);
            return !(dx <= TruckSafeSize.x * 0.5f && dz <= TruckSafeSize.z * 0.5f);`,
  `            if (TruckSafeSizeX <= 0f || TruckSafeSizeZ <= 0f) return true;
            float dx = Math.Abs(x - TruckSafeCenterX);
            float dz = Math.Abs(z - TruckSafeCenterZ);
            return !(dx <= TruckSafeSizeX * 0.5f && dz <= TruckSafeSizeZ * 0.5f);`,
]], 'MonsterMayEnter 用裸浮点');

// ── GameBootstrap：把 Bounds 拆成四个浮点再交给玩法层 ─────────────────
edit('unity/Assets/Scripts/Runtime/GameBootstrap.cs', [[
  '        public void SetTruckSafeZone(Bounds zone) { _truckSafeZone = zone; }',
  `        public void SetTruckSafeZone(Bounds zone)
        {
            _truckSafeZone = zone;
            // 转交给玩法层时**拆成裸浮点**：Whisper.Gameplay 不引用 UnityEngine（见 GameSession 的字段注释）。
            if (_session != null)
            {
                _session.TruckSafeCenterX = zone.center.x;
                _session.TruckSafeCenterZ = zone.center.z;
                _session.TruckSafeSizeX = zone.size.x;
                _session.TruckSafeSizeZ = zone.size.z;
            }
        }`,
]], 'SetTruckSafeZone 转交裸浮点');

console.log('[layer] 安全区分层纠正' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
console.log('  ⚠ 若 GameBootstrap 里没有 _session 字段，下一步会报错 —— 届时按"零实例化"结论处理（见文档 P0）。');
