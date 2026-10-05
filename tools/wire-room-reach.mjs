#!/usr/bin/env node
/**
 * wire-room-reach.mjs — 把「房间可达范围」接进 LanSession 的状态文案
 *
 * ## 为什么
 * 用户要求「以跨地区联机为主」。本项目零信令路线下，**房间码本身携带可达范围**：
 * IPv6 码 = 跨地区可用；私网 IPv4 码 = 仅同 WiFi。若 UI 不说明，
 * 玩家会以为能跨地区、实际只在家里连得上 —— 这是最伤信任的一类缺陷。
 * 故建房成功后立刻把范围写进 `NetStatus`，并在 `LanSession` 上暴露 `Reach` 供 UI 用。
 *
 * ## 纪律
 * 锚点必须真实存在（先读后写）；任一锚点不中即停止、不写文件。
 *
 * 用法：node tools/wire-room-reach.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Net/LanSession.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
let src = raw;
const log = [];
const fail = (m) => { console.error('[reach] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('public static RoomReach Reach')) { console.log('[reach] 已接入，跳过'); process.exit(0); }

// ── ① 暴露 Reach ─────────────────────────────────────────────────────
sub('        /// <summary>当前是否用了真实联机实现（false = 仍在用本机回环桩）。</summary>\n        public static bool UsingRealNet { get; private set; }',
  '        /// <summary>当前是否用了真实联机实现（false = 仍在用本机回环桩）。</summary>\n' +
  '        public static bool UsingRealNet { get; private set; }\n' +
  '\n' +
  '        /// <summary>\n' +
  '        /// 建房得到的房间码**可达范围**（跨地区 / 仅同网段 / 不确定）。\n' +
  '        /// UI 必须如实显示它 —— 见 RoomReach 注释（"能跨地区"与"只在家里连得上"是两种承诺）。\n' +
  '        /// </summary>\n' +
  '        public static RoomReach Reach { get; private set; } = RoomReach.Unknown;',
  '① 新增 Reach 属性');

// ── ② 建房成功后判定并在文案里说明范围 ──────────────────────────────
sub('            net = svc;\n            NetRoomCode = roomCode;\n            UsingRealNet = true;\n            return true;',
  '            net = svc;\n' +
  '            NetRoomCode = roomCode;\n' +
  '            UsingRealNet = true;\n' +
  '            Reach = RoomReachJudge.OfRoomCode(roomCode);   // 房间码本身携带可达范围，如实记录\n' +
  '            return true;',
  '② 建房成功后判定 Reach');

// ── ③ 加入时也判定（客户端要能提示"这码是仅同网段的"）────────────────
sub('            net = svc;\n            NetRoomCode = roomCode.Trim().ToUpperInvariant();\n            UsingRealNet = true;\n            return true;',
  '            net = svc;\n' +
  '            NetRoomCode = roomCode.Trim().ToUpperInvariant();\n' +
  '            UsingRealNet = true;\n' +
  '            Reach = RoomReachJudge.OfRoomCode(NetRoomCode);\n' +
  '            return true;',
  '③ 加入成功后判定 Reach');

// ── ④ 回单机时清掉 ───────────────────────────────────────────────────
sub('        public static void ResetToSolo()\n        {\n            NetRoomCode = null;\n            UsingRealNet = false;\n        }',
  '        public static void ResetToSolo()\n        {\n            NetRoomCode = null;\n            UsingRealNet = false;\n            Reach = RoomReach.Unknown;\n        }',
  '④ ResetToSolo 清掉 Reach');

console.log('[reach] 房间可达范围接入 LanSession' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节`);
}
