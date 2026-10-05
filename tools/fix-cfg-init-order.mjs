#!/usr/bin/env node
/**
 * fix-cfg-init-order.mjs — 修"接线失败"的真根因：`_cfg` 初始化晚于关卡加载（v2 重写）
 *
 * ## 真机证据（0.1.79，画面正常、环境自洽）
 * HUD 明确显示：`玩法层：**未接线**（理智/猎杀/撤离不会推进）`
 * —— 这是我自己的**不静默设计**抓到的（若不写这条，我会误以为接线成功）。
 *
 * ## 根因（读码确认，两次修正后才找准）
 * 启动顺序（`Boot()`，458 行）：
 *   AppendBootHeader → TryLoadConfig(464) → TryInstallServices(465) → TryLoadLevel(466)
 *                    → InitProgressionSystems(473) → BuildMenu(474) → FinishBoot(478)
 * · 实例化 `GameSession` 的代码插在 `TryLoadLevel()`（844 行）里 → 那时**需要 `_cfg`**；
 * · 但 `_cfg` 原先只在 `InitProgressionSystems()`（473，**晚于** 466）里赋值；
 * · 且 `TryLoadConfig()`（763）用的是**静态** `GameConfig.LoadFromJson`，**从不建 `_cfg` 实例**。
 * ⇒ `if (Level != null && _cfg != null)` 判假 → 走了"未接线"分支。
 *
 * ## 我第一次的修法为什么错
 * 我把 `EnsureConfig()` 插在 `InitProgressionSystems`（473）**之前** —— 那仍在 `TryLoadLevel`（466）**之后**，修不到。
 * **教训：改依赖顺序前，先把调用链按行号读出来，别按"感觉先后"改。**
 *
 * ## 修法
 * 在 `TryLoadConfig()`（配置加载处，最早的一处）里建 `_cfg`，并让 `InitProgressionSystems` 复用它
 * （**全类只此一份**，避免两套配置口径）。
 *
 * 用法：node tools/fix-cfg-init-order.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[cfg-order] ✗ ' + m); process.exit(1); };

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('EnsureConfig')) { console.log('[cfg-order] 已应用，跳过'); process.exit(0); }
let s = raw;
const sub = (from, to, what) => {
  const n = s.split(from).length - 1;
  if (n !== 1) fail(`${what}：锚点命中 ${n} 次（应为 1）`);
  s = s.replace(from, to);
  log.push('  ✓ ' + what);
};

// ① TryLoadConfig 里建 _cfg（最早、且在 TryLoadLevel 之前）
sub('                GameConfig.LoadFromJson(cfgAsset.text);',
  [
    '                GameConfig.LoadFromJson(cfgAsset.text);',
    '                // 【依赖顺序】实例化 reader 供玩法层使用：TryLoadLevel（466 行）里的',
    '                // new GameSession(Level, _cfg, …) 需要它，而它原先只在 InitProgressionSystems',
    '                //（473 行，**晚于**关卡加载）里建 → 接线静默失败（0.1.79 真机 HUD 抓到）。',
    '                EnsureConfig();',
  ].join('\n'),
  '① TryLoadConfig 里建 _cfg（早于关卡加载）');

// ② EnsureConfig（幂等）
sub('        bool TryLoadConfig(System.Text.StringBuilder lines)',
  [
    '        /// <summary>',
    '        /// 确保全局唯一的配置读取器已建（**幂等**）。',
    '        /// 为什么单独抽出来：启动流程有两处需要它 —— 配置加载与进度系统初始化，',
    '        /// 而**关卡加载夹在中间**（它要建 GameSession）。抽出来 + 在最早的配置加载处调用，',
    '        /// 就不会再出现"接线时 cfg 还是 null"这类依赖顺序事故。',
    '        /// </summary>',
    '        void EnsureConfig()',
    '        {',
    '            if (_cfg == null) _cfg = new Whisper.Gameplay.Config.GameConfigReader();',
    '        }',
    '',
    '        bool TryLoadConfig(System.Text.StringBuilder lines)',
  ].join('\n'),
  '② 新增 EnsureConfig（幂等）');

// ③ InitProgressionSystems 复用（不新建第二个 reader）
sub('            var cfg = new Whisper.Gameplay.Config.GameConfigReader();\n            _cfg = cfg;   // 存下来给玩法层复用（同一个 reader，不新建第二个）',
  '            EnsureConfig();\n            var cfg = _cfg;   // 复用唯一那份（EnsureConfig 幂等；不新建第二个 reader）',
  '③ InitProgressionSystems 复用唯一那份 cfg');

if (!checkOnly) {
  const bak = FILE + '.bak-cfgorder';
  if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
  fs.writeFileSync(FILE, s, 'utf8');
}
console.log('[cfg-order] 修依赖顺序' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
