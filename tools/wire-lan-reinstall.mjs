#!/usr/bin/env node
/**
 * wire-lan-reinstall.mjs — 开局按当前联机意图**重装** net 服务
 *
 * ## 缺陷
 * 服务是在 `Boot()`（`Start()`）阶段装好的，那时 `NetIntent` 还是 `Solo`；
 * 玩家在大厅里点「多人联机 / 加入房间」只改了意图，**没有第二次装配机会** →
 * 意图形同虚设，真机表现是"点开黑却仍是单机"。
 *
 * ## 修法
 * · 把「按意图建 net 服务」抽成 `BuildNetForIntent(INetService fallback, out string status)`（幂等，可反复调）；
 * · `OnMenuStartRequested()`（对局真正的起点）里判断：**当前服务类型与意图不符**就重装，
 *   用 `Services.Install(newNet, replace: true)` —— `Services` 默认拒绝静默覆盖（fail-fast），
 *   替换必须显式声明，这里正是"Host 迁移/开局重连"那种正当替换场景。
 *
 * ## 纪律
 * 锚点必须真实存在；不改动单人路径的行为（Solo 下类型相符 → 不重装）。
 *
 * 用法：node tools/wire-lan-reinstall.mjs [--check]
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
const fail = (m) => { console.error('[wire-lan2] ✗ ' + m); process.exit(1); };
const sub = (from, to, what) => {
  const n = src.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  src = src.replace(from, to);
  log.push('  ✓ ' + what);
};

if (src.includes('ApplyNetIntentAtMatchStart')) { console.log('[wire-lan2] 已接入，跳过'); process.exit(0); }

// ── ① 把装配逻辑抽成可复用方法（放在 TryInstallServices 之前）──────────
const anchor1 = '        /// <summary>② 三接口注入（组合根的职责；换 SDK 只换实现，接口与玩法代码不动）。</summary>';
if (!src.includes(anchor1)) fail('未找到 TryInstallServices 的文档注释锚点');
const helper = [
  '        /// <summary>',
  '        /// 按当前 <see cref="NetIntent"/> 建联机服务（幂等，可在 Boot 与开局各调一次）。',
  '        /// 失败**不抛异常**：回落传入的兜底实现，并把原因写进 status（真机 HUD 可读）。',
  '        /// </summary>',
  '        INetService BuildNetForIntent(INetService fallback, out string status)',
  '        {',
  '            var tickRate = GameConfig.GetInt("network.tickRate", 60);',
  '            var batchEvery = GameConfig.GetInt("network.batchEveryTicks", 3);',
  '            var sendHz = GameConfig.GetInt("network.transformSendHz", 10);',
  '            var maxPlayers = GameConfig.GetInt("network.maxPlayers", 4);',
  '            status = "单人模式（本机回环桩）";',
  '            if (NetIntent == NetIntentKind.Host)',
  '            {',
  '                if (LanSession.TryHost(fallback, tickRate, batchEvery, sendHz, maxPlayers,',
  '                                       out var hosted, out var code, out var why))',
  '                {',
  '                    status = $"已建房 · 房间码 {code}（发给朋友即可加入）";',
  '                    return hosted;',
  '                }',
  '                status = $"建房失败，已回落单人：{why}";',
  '            }',
  '            else if (NetIntent == NetIntentKind.Join)',
  '            {',
  '                if (LanSession.TryJoin(fallback, tickRate, batchEvery, sendHz, maxPlayers, NetJoinCode,',
  '                                       out var joined, out var why))',
  '                {',
  '                    status = $"已加入房间 {LanSession.NetRoomCode}（等待主机数据）";',
  '                    return joined;',
  '                }',
  '                status = $"加入失败，已回落单人：{why}";',
  '            }',
  '            return fallback;',
  '        }',
  '',
  '        /// <summary>',
  '        /// 开局时按**当前**意图重装 net（大厅里改的意图在 Boot 之后才生效，故必须有这一步）。',
  '        /// 类型相符就不动 —— 单人路径因此零开销、行为不变。',
  '        /// </summary>',
  '        void ApplyNetIntentAtMatchStart()',
  '        {',
  '            bool wantReal = NetIntent != NetIntentKind.Solo;',
  '            if (wantReal == LanSession.UsingRealNet) return;   // 意图与现状相符，不折腾',
  '            var fallback = new LocalNetService(GameConfig.GetInt("network.tickRate", 60));',
  '            var net = BuildNetForIntent(fallback, out var status);',
  '            Services.Install(net, replace: true);   // Services 默认拒绝静默覆盖，此处是正当替换',
  '            NetStatus = status;',
  '            AppendStatus(status);',
  '        }',
  '',
].join('\n');
src = src.replace(anchor1, helper + anchor1);
log.push('  ✓ ① 抽出 BuildNetForIntent + 新增 ApplyNetIntentAtMatchStart');

// ── ② TryInstallServices 改用抽出的方法（消除重复逻辑）──────────────────
{
  const i = src.indexOf('        bool TryInstallServices(System.Text.StringBuilder lines)');
  if (i < 0) fail('未找到 TryInstallServices 方法');
  const endMark = '                return true;\n            }\n            catch (System.Exception ex) { Fail(lines, $"接口注入失败（{ex.GetType().Name}）：{ex.Message}"); return false; }';
  const j = src.indexOf(endMark, i);
  if (j < 0) fail('未找到 TryInstallServices 的收尾锚点');
  const body = src.slice(i, j);
  if (!body.includes('NetIntent == NetIntentKind.Host')) fail('TryInstallServices 里的意图分支不在预期形状');
  const replaced = [
    '        bool TryInstallServices(System.Text.StringBuilder lines)',
    '        {',
    '            try',
    '            {',
    '                var net = BuildNetForIntent(new LocalNetService(GameConfig.GetInt("network.tickRate", 60)), out var netStatus);',
    '                NetStatus = netStatus;',
    '                lines.AppendLine(netStatus);',
    '                Services.Install(net);',
    '                Services.Install(new LocalVoiceService());',
    '                Services.Install(new LocalBackendService());',
    '                Services.Net.OnHostMigration += started =>',
    '                    Debug.Log($"[Whisper] Host 迁移 {(started ? "开始" : "结束")}（V9 §13.4：迁移期播「信号干扰」遮罩）");',
    '                lines.AppendLine(DescribeServices());',
    '',
  ].join('\n');
  src = src.slice(0, i) + replaced + src.slice(j);
  log.push('  ✓ ② TryInstallServices 改用 BuildNetForIntent（消除重复逻辑）');
}

// ── ③ 开局入口调用重装 ────────────────────────────────────────────────
sub('        public void OnMenuStartRequested()\n        {\n            if (_menu != null) _menu.gameObject.SetActive(false);',
  '        public void OnMenuStartRequested()\n        {\n            // 大厅里选的联机意图在这里才生效（Boot 时装的还是单人桩）——见 ApplyNetIntentAtMatchStart 注释。\n            ApplyNetIntentAtMatchStart();\n            if (_menu != null) _menu.gameObject.SetActive(false);',
  '③ OnMenuStartRequested 调用 ApplyNetIntentAtMatchStart');

console.log('[wire-lan2] 开局按意图重装 net' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
if (!checkOnly) {
  fs.writeFileSync(FILE, src, 'utf8');
  console.log(`  已写回：${raw.length} → ${src.length} 字节`);
  console.log('  ⚠ 必须跑：bash tools/unity-syntax-check.sh 与 bash tools/unity-tests.sh EditMode');
}
