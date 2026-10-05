// 把 MenuScene 的 3D 场景换成 HallScene（工业风两层仓库，用户永久约束 §4）。
// 旧的 BuildRoom/BuildFlashlight/BuildGhost 不再调用（保留代码以便对照，但**不再在旧界面上花时间**）。
// **本文件不得出现反引号**。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 字段
if (!s.includes('HallScene _hall')) {
  s = s.replace('        /// <summary>组合根（读等级/商店/任务；主界面不自己持有档案，避免两份真相源）。</summary>\n        GameBootstrap _boot;',
`        /// <summary>组合根（读等级/商店/任务；主界面不自己持有档案，避免两份真相源）。</summary>
        GameBootstrap _boot;
        /// <summary>主界面 3D 场景：工业风两层仓库（替代旧的走廊）。</summary>
        HallScene _hall;`);
  log.push('  ✓ 字段');
}

// ② Build：用 HallScene 取代 BuildRoom + BuildFlashlight + PlaceMenuCamera
const oldBuild = `            BuildRoom();
            BuildFlashlight();
            BuildOptions();
            PlaceMenuCamera();
            BuildShopCache();`;
const newBuild = `            // ── 3D 场景：**工业风两层仓库**（用户 2026-10-05 §4：主界面 3D 场景整体更换）──
            // 旧实现是 BuildRoom()（一条 26m 走廊）+ BuildFlashlight()（右下角手电筒）——
            // 那是为"恐怖走廊"玩法准备的，与官方大厅（可探索的仓库）没有可复用部分。
            // 用户原话：「不要再在旧界面上花时间了」→ 整体替换，旧方法保留但不再调用。
            _hall = new HallScene(_root, _cam);
            _hall.Build();
            BuildOptions();
            BuildShopCache();`;
if (s.includes(oldBuild)) { s = s.replace(oldBuild, newBuild); log.push('  ✓ Build 换成 HallScene'); }
else log.push('  ! Build 锚点未中');

// ③ 旧的刷鬼/相机方法调用点处理：TrySpawnGhost 原在 Build 里，现在大厅不需要"刷鬼"（用户 §4 新大厅是等待区）
//    但保留调用会在大厅里随机出现鬼 —— 与"大厅是安全等待区"冲突。改为不再调用。
s = s.replace(`            // 刷鬼放在**相机就位之后**：鬼的位置是相对房间坐标的，摆相机再刷鬼，
            // 首帧就能看见（顺序反了会让第一只鬼像是在"被相机追着跑"）。
            TrySpawnGhost(true);`,
`            // 【不再刷鬼】官方大厅是**等待区/安全区**（无鬼）。旧的"远处黑暗中刷红眼鬼"
            // 属于旧玩法的氛围设计，本次随 3D 场景一起下线。
            // 但**鬼的模型池仍保留**（对局里要用），见 GhostModelPool / MonsterViews。`);
log.push('  ✓ 大厅不再刷鬼');

// ④ Update 里驱动大厅动效
if (!s.includes('_hall.Tick')) {
  s = s.replace('            _nextLobbyRefresh -= dt;\n            if (_nextLobbyRefresh <= 0f) { _nextLobbyRefresh = 0.5f; RefreshLobby(); RefreshCamInfo(); }',
`            // 大厅环境动效（灵球漂浮等）
            if (_hall != null) _hall.Tick(dt);

            _nextLobbyRefresh -= dt;
            if (_nextLobbyRefresh <= 0f) { _nextLobbyRefresh = 0.5f; RefreshLobby(); RefreshCamInfo(); }`);
  log.push('  ✓ Update 驱动大厅');
}

// ⑤ 相机诊断行里带上大厅信息
s = s.replace('            string q = _boot != null && _boot.Quality != null ? _boot.Quality.Describe() : "画质：—";\n            _camInfo = cam + "\\n" + fx + "\\n" + q;',
`            string q = _boot != null && _boot.Quality != null ? _boot.Quality.Describe() : "画质：—";
            string hall = _hall != null ? _hall.Describe() : "大厅：未建";
            _camInfo = cam + "\\n" + fx + "\\n" + q + "\\n" + hall;`);
log.push('  ✓ 诊断带大厅信息');

// ⑥ 灯光闪烁逻辑引用 _ceilingLight（旧走廊的天花板灯）—— 大厅没有这个字段，必须防 null
s = s.replace('            if (_dipLeft > 0f)\n            {\n                _dipLeft -= dt;',
`            // 【兼容旧逻辑】旧走廊的"天花板灯骤暗"用 _ceilingLight；大厅用多盏点光，
            // 没有单一 _ceilingLight → 下面整段在没有灯时直接跳过（否则空引用会把 Update 打断）。
            if (_ceilingLight == null) { _dipLeft = 0f; _dipTimer = 0f; }
            if (_ceilingLight != null && _dipLeft > 0f)
            {
                _dipLeft -= dt;`);
s = s.replace('            else\n            {\n                _dipTimer -= dt;\n                // 平时有轻微抖动（旧灯管），让"不停闪烁"这句话成立\n                _ceilingLight.intensity = LightBase * (0.90f + 0.10f * Mathf.PerlinNoise(Time.time * 7f, 0.3f));',
`            else if (_ceilingLight != null)
            {
                _dipTimer -= dt;
                // 平时有轻微抖动（旧灯管），让"不停闪烁"这句话成立
                _ceilingLight.intensity = LightBase * (0.90f + 0.10f * Mathf.PerlinNoise(Time.time * 7f, 0.3f));`);
log.push('  ✓ 旧灯光闪烁对大厅安全');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
