#!/usr/bin/env node
/**
 * wire-lan-session.mjs — 把「真联机服务」接进组合根（可开黑头号阻塞）
 *
 * ## 缺陷
 * `unity/Assets/Scripts/Runtime/GameBootstrap.cs` 的 `TryInstallServices` 里写死：
 *     Services.Install(new LocalNetService(tickRate));
 * 即**本机回环桩**（`SendLocalPlayer` / `SendVoiceStimulus` 只写本地列表，不跨进程、不联网）。
 * 而真实现 `UdpV6NetService`（IPv6 直连 + 房间码 + 4 人 + 带宽预算）早已写好且同契约，
 * **没有任何代码路径会构造它** → 两台设备永远连不上。
 *
 * ## 改法（最小、可回退、默认行为不变）
 * · 新增 `NetIntent`（solo / host / join）+ `NetJoinCode` 两个公开字段：**默认 solo**，行为与现在完全一致；
 * · `TryInstallServices` 按意图装配：
 *     solo → `LocalNetService`（兜底，无网卡也能开局）
 *     host → `LanSession.TryHost(...)` 取本机地址编房间码并监听，码存 `NetRoomCode`
 *     join → `LanSession.TryJoin(...)` 按玩家输入的码直连
 * · 建房/加入失败**不阻断启动**：回落本机桩并把原因写进启动日志（网络失败不该让游戏起不来）。
 *
 * ## 纪律
 * 锚点必须是**真实存在的行**（先读再写）；找不到即停止不写文件；LF/CRLF 保持。
 *
 * 用法：node tools/wire-lan-session.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
const eol = raw.includes('\r\n') ? '\r\n' : '\n';
let src = raw;
const log = [];
const fail = (m) => { console.error('[wire-lan] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('NetIntent')) { console.log('[wire-lan] 已接入（检测到 NetIntent），跳过'); process.exit(0); }

// ── ① 新增两个公开字段（放在 LevelResourcePath 附近，它是既有的"启动参数"位）──
sub('        public string LevelResourcePath = "Levels/asylum_v1";',
  '        public string LevelResourcePath = "Levels/asylum_v1";\n' +
  '\n' +
  '        /// <summary>\n' +
  '        /// 联机意图：**默认单人**（行为与接入前完全一致 —— 不开 socket，走本机回环桩）。\n' +
  '        /// 菜单里点「多人联机」= Host（建房），点「加入房间」= Join（用 <see cref="NetJoinCode"/>）。\n' +
  '        /// 之所以做成显式意图而不是"自动联机"：真实现会 bind 端口，不该在大厅里偷偷开。\n' +
  '        /// </summary>\n' +
  '        public NetIntentKind NetIntent = NetIntentKind.Solo;\n' +
  '\n' +
  '        /// <summary>加入时要输入的房间码（仅 <see cref="NetIntentKind.Join"/> 用）。</summary>\n' +
  '        public string NetJoinCode;\n' +
  '\n' +
  '        /// <summary>建房成功后要发给朋友的房间码（UI 显示在菜单板右上角）；未建房为 null。</summary>\n' +
  '        public string NetRoomCode => LanSession.NetRoomCode;\n' +
  '\n' +
  '        /// <summary>联机装配结果的一句话说明（失败原因也在这里，便于真机 HUD 取证）。</summary>\n' +
  '        public string NetStatus { get; private set; } = "单人模式";',
  '① 新增联机意图字段 NetIntent / NetJoinCode / NetRoomCode / NetStatus');

// ── ② TryInstallServices 按意图装配 ─────────────────────────────────────
sub('                var tickRate = GameConfig.GetInt("network.tickRate", 60);\n' +
    '                Services.Install(new LocalNetService(tickRate));',
  '                var tickRate = GameConfig.GetInt("network.tickRate", 60);\n' +
  '                var batchEvery = GameConfig.GetInt("network.batchEveryTicks", 3);\n' +
  '                var sendHz = GameConfig.GetInt("network.transformSendHz", 10);\n' +
  '                var maxPlayers = GameConfig.GetInt("network.maxPlayers", 4);\n' +
  '\n' +
  '                // 默认单人：本机回环桩（无网卡/无权限也能开局）。\n' +
  '                // 只有明确建房/加入才构造真实现 —— 见 NetIntent 注释。\n' +
  '                INetService net = new LocalNetService(tickRate);\n' +
  '                NetStatus = "单人模式（本机回环桩）";\n' +
  '                if (NetIntent == NetIntentKind.Host)\n' +
  '                {\n' +
  '                    if (LanSession.TryHost(net, tickRate, batchEvery, sendHz, maxPlayers,\n' +
  '                                           out var hosted, out var hostCode, out var hostReason))\n' +
  '                    {\n' +
  '                        net = hosted;\n' +
  '                        NetStatus = $"已建房 · 房间码 {hostCode}（发给朋友即可加入）";\n' +
  '                    }\n' +
  '                    else\n' +
  '                    {\n' +
  '                        NetStatus = $"建房失败，已回落单人：{hostReason}";\n' +
  '                    }\n' +
  '                }\n' +
  '                else if (NetIntent == NetIntentKind.Join)\n' +
  '                {\n' +
  '                    if (LanSession.TryJoin(net, tickRate, batchEvery, sendHz, maxPlayers, NetJoinCode,\n' +
  '                                           out var joined, out var joinReason))\n' +
  '                    {\n' +
  '                        net = joined;\n' +
  '                        NetStatus = $"已加入房间 {LanSession.NetRoomCode}（等待主机数据）";\n' +
  '                    }\n' +
  '                    else\n' +
  '                    {\n' +
  '                        NetStatus = $"加入失败，已回落单人：{joinReason}";\n' +
  '                    }\n' +
  '                }\n' +
  '                lines.AppendLine(NetStatus);\n' +
  '                Services.Install(net);',
  '② TryInstallServices 按 NetIntent 装配真联机/兜底桩');

// ── ③ 意图枚举（放在文件末尾的命名空间内，public 供 UI 用）──────────────
{
  const i = src.lastIndexOf('}');
  if (i < 0) fail('未找到文件末尾的命名空间闭合括号');
  const kind = '\n' +
    '    /// <summary>联机意图（见 <c>GameBootstrap.NetIntent</c> 注释）。</summary>\n' +
    '    public enum NetIntentKind\n' +
    '    {\n' +
    '        /// <summary>单人：本机回环桩，不开 socket（默认）。</summary>\n' +
    '        Solo = 0,\n' +
    '        /// <summary>建房：取本机地址编房间码并监听 UDP。</summary>\n' +
    '        Host = 1,\n' +
    '        /// <summary>加入：按 <c>NetJoinCode</c> 直连主机。</summary>\n' +
    '        Join = 2,\n' +
    '    }\n';
  src = src.slice(0, i) + kind + src.slice(i);
  log.push('  ✓ ③ 新增 NetIntentKind 枚举');
}

console.log('[wire-lan] 把真联机服务接进组合根' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节`);
  console.log('  ⚠ 下一步必须跑：bash tools/unity-syntax-check.sh 与 bash tools/unity-tests.sh EditMode');
}
