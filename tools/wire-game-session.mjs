#!/usr/bin/env node
/**
 * wire-game-session.mjs — P0：把 `GameSession` 第一次实例化进对局
 *
 * ## 背景（第 17 轮实测结论）
 * `GameSession`（理智→猎杀→取证→撤离的完整玩法层）**全仓零实例化**：
 * ```
 * grep "GameSession"      → 仅 3 处，全在 GameSession.cs 自身
 * grep "new GameSession(" → 零命中
 * ```
 * 局内实际跑的是 `GameBootstrap` 自己的简化逻辑。所以"可开黑核心循环"缺的是**接线**，不是功能。
 *
 * ## 本轮的接线策略：**并行驱动 + 可观测**，不立刻替换既有逻辑
 * 理由：直接替换是高风险操作（出生点/证据点/怪物实例化/阶段推进/撤离结算五处一起换），
 * 而"接线了但没验证"正是本项目反复踩的坑（`SendLocalPlayer` 零调用者、`UdpV6NetService` 从未构造）。
 * 所以分两步：
 *   ① **本轮**：实例化 + 每帧 `Tick` + 把玩家位置喂进去 + **把 Session 状态写进 HUD 诊断**（证明它在跑）；
 *   ② **下一轮**：逐项对照，把 `GameBootstrap` 的简化逻辑换成 Session 的结论，每换一项跑一次门禁。
 *
 * ## 数据源（都实测确认过，不猜）
 * · 构造参数 `LevelData Level` ← `GameBootstrap.Level`（`LevelLoader.Load` 产物，`:795`）
 * · 构造参数 `GameConfigReader cfg` ← `InitProgressionSystems` 里的 `new GameConfigReader()`（`:496`）；
 *   本脚本把它存进 `_cfg` 字段复用（**不新建第二个 reader**，避免两套配置口径）
 * · `MatchSeed` ← `GameBootstrap.MatchSeed`（public set）
 * · `VoiceAnchors` 是 `struct`（`VoiceBandClassifier.cs:8`），传 null 走默认锚点
 * · **`GameBootstrap` 没有 `_hall`**（那是 `MenuScene` 的字段）—— 安全区仍走推送式
 *
 * 用法：node tools/wire-game-session.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[session] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('TickSession')) { console.log('[session] 已接线，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ── ① 字段与公开属性 ──────────────────────────────────────────────────
sub('        public LevelData Level { get; private set; }',
  `        public LevelData Level { get; private set; }

        /// <summary>
        /// **玩法层**（理智→猎杀→取证→撤离）。第 17 轮发现它此前**全仓零实例化**，
        /// 本类第一次把它接进对局。接线策略见 wire-game-session.mjs 头注释：
        /// 先并行驱动 + 可观测，再逐项替换本类的简化逻辑（避免"接线了但没验证"）。
        /// </summary>
        public Whisper.Gameplay.Session.GameSession Session { get; private set; }

        /// <summary>配置读取器（与 Progression/Shop/TaskSystem 共用同一份，避免两套配置口径）。</summary>
        Whisper.Gameplay.Config.GameConfigReader _cfg;`,
  '① 新增 Session 属性与 _cfg 字段');

// ── ② 复用同一个 cfg ──────────────────────────────────────────────────
sub('            var cfg = new Whisper.Gameplay.Config.GameConfigReader();\n            _progression = new Progression(cfg);',
  `            var cfg = new Whisper.Gameplay.Config.GameConfigReader();
            _cfg = cfg;   // 存下来给玩法层复用（同一个 reader，不新建第二个）
            _progression = new Progression(cfg);`,
  '② 存下 cfg 供玩法层复用');

// ── ③ 实例化玩法层（放在关卡加载成功之后）────────────────────────────
sub('                lines.AppendLine(SetupGhostRoom());',
  `                lines.AppendLine(SetupGhostRoom());

                // ── P0 接线：把玩法层实例化进对局（第 17 轮发现此前零实例化）──────────
                // 放在这里的原因：此时 Level 已加载成功、cfg 已就绪、matchSeed 已定。
                // 用**同一个 cfg 与同一个 MatchSeed**，保证与 Progression/TaskSystem 的随机流派一致。
                if (Level != null && _cfg != null)
                {
                    Session = new Whisper.Gameplay.Session.GameSession(Level, _cfg, MatchSeed);
                    lines.AppendLine("玩法层：已接线（GameSession 实例化）· 房间 " + Level.Rooms.Count);
                }
                else
                {
                    // 不静默：接线失败必须看得见（本项目无数次"静默失效"的教训）
                    lines.AppendLine("玩法层：**未接线**（Level 或 cfg 为空）—— 理智/猎杀/撤离不会推进");
                }`,
  '③ 实例化 GameSession');

// ── ④ 每帧驱动 ────────────────────────────────────────────────────────
sub('        void Update()\n        {\n            TickInteractionAndTasks();',
  `        void Update()
        {
            TickInteractionAndTasks();
            TickSession();`,
  '④ Update 里驱动玩法层');

// ── ⑤ TickSession 实现 ────────────────────────────────────────────────
sub('        void TickInteractionAndTasks()',
  `        /// <summary>
        /// 驱动玩法层（本轮为**并行驱动**：既有简化逻辑照旧跑，玩法层同时推进并暴露状态）。
        /// 玩家位置每帧喂进去 —— 玩法层需要知道玩家在哪（安全区、证据点邻近、房间停留都在用它）。
        /// </summary>
        void TickSession()
        {
            if (Session == null) return;
            if (_player != null) { Session.PlayerX = _player.X; Session.PlayerZ = _player.Z; }
            // 安全区：组合根存的是 Bounds（Runtime 有 Unity），玩法层收**裸浮点**
            // （Whisper.Gameplay 不引用 UnityEngine —— 实测 CS0246）。
            var safe = _truckSafeZone;
            Session.TruckSafeCenterX = safe.center.x;
            Session.TruckSafeCenterZ = safe.center.z;
            Session.TruckSafeSizeX = safe.size.x;
            Session.TruckSafeSizeZ = safe.size.z;
            Session.Tick(Time.deltaTime);
        }

        void TickInteractionAndTasks()`,
  '⑤ 新增 TickSession（喂位置 + 安全区 + Tick）');

if (!checkOnly) {
  const bak = FILE + '.bak-session';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[session] 玩法层接线' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
