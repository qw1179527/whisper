#!/usr/bin/env node
/**
 * fix-safe-zone-expose.mjs — 修 expose 脚本的字段名臆测错误（本轮第三次同类）
 *
 * ## 错在哪
 * 我在 `GameBootstrap` 里写了 `_hall.TruckSafeZone`，但 **`GameBootstrap` 没有 `_hall` 字段**：
 * grep 全仓只有 `MenuScene.cs:82 HallScene _hall;` 与 `:172 _hall = new HallScene(...)`。
 * 也就是说 **HallScene 由 MenuScene 持有**，GameBootstrap 拿不到它。
 *
 * ## 这次的根因（与前两次同一族）
 * · 第 1 次：`_matBody/_matMetal` —— 凭印象的材质字段（实际是方法内局部变量）
 * · 第 2 次：`MiniJson` / `using Whisper.Gameplay.Level` —— 忘了命名空间
 * · 第 3 次：`_hall` —— 凭印象的宿主字段
 * **共同点：我在"写代码"时假定了一个我没读过的结构。**
 * 纪律（写进注释）：**用某类型的成员前，先 grep 该成员在仓里的真实声明**；
 * 跨类引用尤其如此 —— 语法门禁不做名字解析，只有 EditMode 会红，代价是整轮返工。
 *
 * ## 修法：由持有方推送（push），而不是让组合根去抓（pull）
 * `GameBootstrap` 不保存 `HallScene` 引用（那是 UI 层的持有关系，反向依赖会破分层）。
 * 改为：
 *   · `GameBootstrap.SetTruckSafeZone(Bounds)` —— 由 `MenuScene`（持有 HallScene 的一方）
 *     在货车建好后以及每帧（车不动，故建好后一次即可，但保留每帧调用的能力）推给它；
 *   · `TruckSafeZone` / `PlayerInTruckSafeZone` 读这份**纯数据**，fail-safe 默认在地底且零尺寸。
 *
 * 用法：node tools/fix-safe-zone-expose.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
const checkOnly = process.argv.includes('--check');

let s = fs.readFileSync(FILE, 'utf8');
const log = [];
const fail = (m) => { console.error('[fixsafe] ✗ ' + m); process.exit(1); };

const from = [
  '        public Bounds TruckSafeZone => _hall != null',
  '            ? _hall.TruckSafeZone',
  '            : new Bounds(new Vector3(0f, -1000f, 0f), Vector3.zero);',
].join('\n');
if (s.split(from).length - 1 !== 1) fail('未找到待修的 TruckSafeZone 实现（可能已修）');
const to = [
  '        /// <summary>货车安全区数据（由 MenuScene 推送，见 SetTruckSafeZone）。默认在地底且零尺寸。</summary>',
  '        Bounds _truckSafeZone = new Bounds(new Vector3(0f, -1000f, 0f), Vector3.zero);',
  '',
  '        public Bounds TruckSafeZone => _truckSafeZone;',
  '',
  '        /// <summary>',
  '        /// 由**持有 HallScene 的一方**（MenuScene）把货车安全区推给组合根。',
  '        /// 为什么用推送而不是让 GameBootstrap 去抓：`GameBootstrap` 没有 HallScene 引用',
  '        /// （全仓只有 `MenuScene.cs:82 HallScene _hall;`），让它去抓等于反向依赖 UI 层、破坏分层。',
  '        /// 推送的是**纯数据**（世界 AABB），玩法层因此不依赖任何几何实现。',
  '        /// </summary>',
  '        public void SetTruckSafeZone(Bounds zone) { _truckSafeZone = zone; }',
].join('\n');
s = s.replace(from, to);

// 顺带把 PlayerInTruckSafeZone 里对 b 的用法保持（它读 TruckSafeZone，已改成字段，无需再改）
if (!checkOnly) fs.writeFileSync(FILE, s, 'utf8');
console.log('[fixsafe] 改为推送式（组合根不持有 HallScene）' + (checkOnly ? '（--check）' : ''));
