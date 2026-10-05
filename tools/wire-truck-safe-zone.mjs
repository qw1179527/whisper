#!/usr/bin/env node
/**
 * wire-truck-safe-zone.mjs — 货车安全区（用户《补充说明》§8）
 *
 * ## 规格原文（§8）
 * > **车尾门与安全区**：车尾键盘控制坡道开关；启动合约、所有玩家加载完毕后可开坡道下车。
 * > **货车及其周围是安全区**：鬼无法进入，在车内不掉理智
 *
 * ## 接入点（读码所得，不是猜）
 * `Gameplay/Session/GameSession.cs:189`：
 * ```csharp
 * Sanity.Tick(dt, args.TorchOn, inSafeZone: false, lightsOut: args.LightsOut);
 * ```
 * **`inSafeZone` 一直硬编码 false** —— 也就是说"安全区不给理智保护"这件事从来没接上，
 * 而 `SanitySystem.Tick` 本身**早就支持** `inSafeZone`（有恢复分支）。这是典型的
 * "能力已存在但没接线"（本项目已多次出现：`SendLocalPlayer` 零调用者、`UdpV6NetService` 从未构造）。
 *
 * ## 本脚本做三件事
 * 1. `TruckScene` 增加 `SafeZoneBounds`（车体 + 外扩，§8 说"货车及其周围"）与 `IsInSafeZone(world)`；
 * 2. `HallScene` 暴露 `TruckSafeZone`（世界 AABB），供局内查询；
 * 3. `GameSession` 新增 `TruckSafeCenter/TruckSafeSize` 与 `InSafeZone` 计算，
 *    把 `Sanity.Tick(..., inSafeZone: InSafeZone, ...)` 接上；
 *    并把"怪物不得进入安全区"做成**可查询的判据**（`MonsterMayEnter(x,z)`），供怪物寻路使用。
 *
 * ## 为什么不直接改怪物寻路
 * 怪物 AI 在 `Whisper.Gameplay.Monsters`，它不知道货车在哪（Gameplay 层不该依赖 Runtime 的几何）。
 * 正解：**Gameplay 只持有"安全区矩形"这个纯数据**，Runtime 每帧把货车矩形喂进来。
 * 这样分层不破（Gameplay 不反向依赖 Runtime），也便于将来换成多辆/多个安全区。
 *
 * 用法：node tools/wire-truck-safe-zone.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[safe] ✗ ' + m); process.exit(1); };

function editFile(rel, pairs, tag) {
  const p = path.join(ROOT, rel);
  let s = fs.readFileSync(p, 'utf8');
  if (s.includes(tag)) { log.push('  · ' + rel + ' 已改，跳过'); return; }
  for (const [from, to] of pairs) {
    const n = s.split(from).length - 1;
    if (n !== 1) fail(`${rel}：锚点命中 ${n} 次（应为 1）→ ${from.slice(0, 70)}`);
    s = s.replace(from, to);
  }
  if (!checkOnly) fs.writeFileSync(p, s, 'utf8');
  log.push('  ✓ ' + rel);
}

// ── ① TruckScene：安全区矩形 + 判定 ──────────────────────────────────
editFile('unity/Assets/Scripts/Runtime/TruckScene.cs', [[
  '        /// <summary>世界点是否在货车内部（含车厢地板以上、顶棚以下）。</summary>',
  `        /// <summary>
        /// 安全区外扩（米）。§8 原文是"**货车及其周围**是安全区"，故不是只有车厢内部。
        /// 取 2.5 m：够覆盖坡道落地区与绕车一圈，又不会把"刚下车就被保护"扩散太远
        /// （那会让玩家在车边站着刷理智，破坏"必须回车上"的张力）。**design 值**。
        /// </summary>
        public const float SafeZoneMargin = 2.5f;

        /// <summary>安全区世界 AABB（车体 + 外扩）。局内每帧读它做理智与怪物判定。</summary>
        public Bounds SafeZoneBounds => new Bounds(
            WorldBounds.center + Vector3.up * 0.5f,
            new Vector3(WorldBounds.size.x + SafeZoneMargin * 2f,
                        WorldBounds.size.y,
                        WorldBounds.size.z + SafeZoneMargin * 2f));

        /// <summary>世界点是否在安全区内（§8：鬼无法进入，在车内不掉理智）。</summary>
        public bool IsInSafeZone(Vector3 world) => SafeZoneBounds.Contains(world);

        /// <summary>世界点是否在货车内部（含车厢地板以上、顶棚以下）。</summary>`,
]], 'SafeZoneBounds');

// ── ② HallScene：把安全区暴露给局内 ──────────────────────────────────
editFile('unity/Assets/Scripts/Runtime/HallScene.cs', [[
  '        public TruckScene Truck { get; private set; }',
  `        public TruckScene Truck { get; private set; }

        /// <summary>
        /// 货车安全区（世界 AABB）。局内每帧把它喂给玩法层做理智与怪物判定 ——
        /// 玩法层（Gameplay）**不反向依赖** Runtime 的几何，只收纯数据。
        /// 货车未建时返回一个"空且在地底"的矩形，保证任何点都不在内（fail-safe：宁可不保护也不误保护）。
        /// </summary>
        public Bounds TruckSafeZone => Truck != null
            ? Truck.SafeZoneBounds
            : new Bounds(new Vector3(0f, -1000f, 0f), Vector3.zero);`,
]], 'TruckSafeZone');

// ── ③ GameSession：接线 inSafeZone ───────────────────────────────────
editFile('unity/Assets/Scripts/Gameplay/Session/GameSession.cs', [
  [
    '        public float PlayerX { get; set; }\n        public float PlayerZ { get; set; }',
    `        public float PlayerX { get; set; }
        public float PlayerZ { get; set; }

        /// <summary>
        /// 货车安全区（世界 AABB），由 Runtime 每帧写入（见 HallScene.TruckSafeZone）。
        /// 为什么是纯数据而不是引用 TruckScene：Gameplay 层不反向依赖 Runtime 的几何 ——
        /// 保持"玩法不依赖具体车辆实现"，将来换多辆车或多个安全区也不用改玩法层。
        /// 默认值放在地底且尺寸为零：**任何点都不在内**（fail-safe：宁可不保护，也不误保护）。
        /// </summary>
        public Vector3 TruckSafeCenter { get; set; } = new Vector3(0f, -1000f, 0f);
        public Vector3 TruckSafeSize { get; set; } = Vector3.zero;

        /// <summary>玩家当前是否在货车安全区内（§8：鬼无法进入，在车内不掉理智）。</summary>
        public bool PlayerInSafeZone
        {
            get
            {
                if (TruckSafeSize.x <= 0f || TruckSafeSize.y <= 0f || TruckSafeSize.z <= 0f) return false;
                float dx = Math.Abs(PlayerX - TruckSafeCenter.x);
                float dz = Math.Abs(PlayerZ - TruckSafeCenter.z);
                return dx <= TruckSafeSize.x * 0.5f && dz <= TruckSafeSize.z * 0.5f;
            }
        }

        /// <summary>
        /// 怪物是否**允许**进入某点。§8：安全区内鬼无法进入 —— 故安全区内一律返回 false。
        /// 供怪物寻路在选下一个路径点时过滤（而不是让怪物撞墙后自己放弃，那会产生抖动）。
        /// </summary>
        public bool MonsterMayEnter(float x, float z)
        {
            if (TruckSafeSize.x <= 0f || TruckSafeSize.z <= 0f) return true;
            float dx = Math.Abs(x - TruckSafeCenter.x);
            float dz = Math.Abs(z - TruckSafeCenter.z);
            return !(dx <= TruckSafeSize.x * 0.5f && dz <= TruckSafeSize.z * 0.5f);
        }`,
  ],
  [
    '            Sanity.Tick(dt, args.TorchOn, inSafeZone: false, lightsOut: args.LightsOut);',
    `            // 【§8 安全区】inSafeZone 原先是硬编码 false —— 而 SanitySystem.Tick 早就支持它
            // （有 inSafeZone 恢复分支），只是从来没接线。这正是"能力已存在但没接上"的又一例。
            Sanity.Tick(dt, args.TorchOn, inSafeZone: PlayerInSafeZone, lightsOut: args.LightsOut);`,
  ],
], 'GameSession inSafeZone');

console.log('[safe] 货车安全区接线' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
console.log('  下一步：Runtime 每帧把 HallScene.TruckSafeZone 写进 GameSession（见 wire-truck-safezone-feed.mjs）');
