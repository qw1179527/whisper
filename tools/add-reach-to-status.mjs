#!/usr/bin/env node
/**
 * add-reach-to-status.mjs — 把「房间可达范围」写进建房 NetStatus
 *
 * ## 为什么
 * 用户要求「以跨地区联机为主」，而零信令路线下房间码决定可达范围（IPv6=跨地区，私网 IPv4=仅同 WiFi）。
 * 把范围写进 `NetStatus` 后，**真机截图即可直接读到**（本项目纪律：诊断信息必须能被截屏取证），
 * 不必等 UI 排版做完才能验证。
 *
 * ## 写法纪律（本会话第 4 次栽在 PowerShell 引号层数，故固定为脚本文件）
 * 凡涉及 `${...}` / 双引号的替换，一律落成 .mjs 再跑，不用 `node -e`：
 * PowerShell 会吃掉内部双引号、并把 `$` 当变量展开。
 *
 * 用法：node tools/add-reach-to-status.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
let src = raw;
const log = [];
const fail = (m) => { console.error('[reach-status] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('RoomReachJudge')) { console.log('[reach-status] 已接入，跳过'); process.exit(0); }

// 建房文案：附上可达范围（不夸大：私网码会明确写"仅同 WiFi/局域网内可加入"）
sub('status = $"已建房 · 房间码 {code}（发给朋友即可加入）";',
  'status = $"已建房 · 房间码 {code} · {RoomReachJudge.Describe(LanSession.Reach)}（发给朋友即可加入）";',
  '① 建房 NetStatus 附可达范围');

// 加入文案同理（客户端也要看到"这码是仅同网段的"）
sub('status = $"已加入房间 {LanSession.NetRoomCode}（等待主机数据）";',
  'status = $"已加入房间 {LanSession.NetRoomCode} · {RoomReachJudge.Describe(LanSession.Reach)}（等待主机数据）";',
  '② 加入 NetStatus 附可达范围');

console.log('[reach-status] 可达范围写进 NetStatus' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节`);
}
