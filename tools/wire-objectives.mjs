// 把**局内任务**（合同日志里的可选目标，ObjectiveSystem）接进产品：
//   · Boot 建系统 → StartMatch 按本局种子抽任务 → HUD/面板显示 → 局内事件上报进度
// **本文件不得出现反引号**（会截断 JS 模板，已失败 9 次）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① using + 字段 + 公开属性
if (!s.includes('_objectives')) {
  s = s.replace('using Whisper.Gameplay.Progression;   // Progression / Shop / TaskSystem（等级-商店-任务，恐鬼症对齐）',
`using Whisper.Gameplay.Progression;   // Progression / Shop / TaskSystem（等级-商店-任务，恐鬼症对齐）
using Whisper.Gameplay.Objectives;    // ObjectiveSystem（**局内任务**：合同日志里的可选目标，与每日任务是两套）`);
  s = s.replace('        Whisper.Gameplay.Power.PowerSystem _power;',
`        /// <summary>局内任务（每局按本局种子抽 N 条，结算时叠加奖励）。</summary>
        ObjectiveSystem _objectives;
        Whisper.Gameplay.Power.PowerSystem _power;`);
  s = s.replace('        /// <summary>电力。</summary>',
`        /// <summary>局内任务（合同日志）。</summary>
        public ObjectiveSystem Objectives => _objectives;
        /// <summary>电力。</summary>`);
  log.push('  ✓ using/字段/属性');
}

// ② Boot 里建实例
if (!s.includes('_objectives = new ObjectiveSystem')) {
  s = s.replace('            _interaction = new Whisper.Gameplay.Interaction.InteractionSystem(cfg, _power);',
`            _objectives = new ObjectiveSystem(cfg);
            _interaction = new Whisper.Gameplay.Interaction.InteractionSystem(cfg, _power);`);
  s = s.replace('            lines.AppendLine(_interaction.Describe());\n            WirePowerToLights();',
`            lines.AppendLine(_interaction.Describe());
            lines.AppendLine(_objectives.Describe().Split('\\n')[0]);
            WirePowerToLights();`);
  log.push('  ✓ Boot 建实例');
}

// ③ StartMatch 抽任务（用 MatchSeed；与天气/风向同一个确定性来源）
if (!s.includes('_objectives.BeginContract')) {
  const anchor = '            if (!_matchStarted)';
  if (s.includes(anchor)) {
    s = s.replace(anchor,
`            // 局内任务按**本局种子**抽 —— 与天气/风向同一来源（MatchSeed），保证联机可复现、
            // 且同一局重进不会换任务（BeginContract 对同种子是幂等的）。
            if (_objectives != null) _objectives.BeginContract((uint)MatchSeed ^ 0x5F3759DFu);

            if (!_matchStarted)`);
    log.push('  ✓ StartMatch 抽任务');
  } else log.push('  ! StartMatch 锚点未中（_matchStarted 不在预期位置）');
}

// ④ HUD 一行 + 面板显示
if (!s.includes('Objectives.OneLine')) {
  s = s.replace('                _monsters != null ? _monsters.Describe() : "怪物：—",',
`                _monsters != null ? _monsters.Describe() : "怪物：—",
                _objectives != null ? _objectives.OneLine() : "本局任务：—",`);
  log.push('  ✓ HUD 接一行');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
