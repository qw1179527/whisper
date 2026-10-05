// 把等级/商店/任务/电闸/互动接进组合根（**本文件不得出现反引号** —— 会截断 JS 模板，已失败 9 次）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 字段
if (!s.includes('Progression _progression')) {
  s = s.replace('        MenuScene _menu;',
`        MenuScene _menu;
        /// <summary>等级/经验/声望/钱/碎片（恐鬼症对齐 · docs/spec/phasmophobia-alignment.md §3.1）。</summary>
        Progression _progression;
        /// <summary>商店与已装备（§3.2）。</summary>
        Shop _shop;
        /// <summary>每日/每周任务（§3.3）。</summary>
        TaskSystem _tasks;
        /// <summary>电力：单总闸 + 各房间灯（§3.4）。</summary>
        Whisper.Gameplay.Power.PowerSystem _power;
        /// <summary>互动：鬼开关灯/扔物/敲击/关总闸（§3.5）。</summary>
        Whisper.Gameplay.Interaction.InteractionSystem _interaction;`);
  log.push('  ✓ 字段');
}

// ② 公开属性（主界面读）
if (!s.includes('public Progression Progression')) {
  s = s.replace('        public LevelData Level { get; private set; }',
`        /// <summary>等级档案（主界面与结算读写）。</summary>
        public Progression Progression => _progression;
        /// <summary>商店（主界面买/装备）。</summary>
        public Shop Shop => _shop;
        /// <summary>任务（主界面任务板）。</summary>
        public TaskSystem Tasks => _tasks;
        /// <summary>电力。</summary>
        public Whisper.Gameplay.Power.PowerSystem Power => _power;
        /// <summary>互动。</summary>
        public Whisper.Gameplay.Interaction.InteractionSystem Interaction => _interaction;

        public LevelData Level { get; private set; }`);
  log.push('  ✓ 公开属性');
}

// ③ 在 BuildMenu 之前初始化这些系统（Boot 的 TryLoadLevel 之后）
if (!s.includes('InitProgressionSystems')) {
  s = s.replace('        void BuildMenu(System.Text.StringBuilder lines)',
`        /// <summary>初始化等级/商店/任务/电力/互动（一次性；Boot 阶段调用）。</summary>
        void InitProgressionSystems(System.Text.StringBuilder lines)
        {
            var cfg = new Whisper.Gameplay.Config.GameConfigReader();
            _progression = new Progression(cfg);
            _shop = new Shop();
            _shop.Load(cfg);
            _tasks = new TaskSystem(cfg);
            // ⚠ 任务按**日期种子**生成，但 gate-physics 禁止 DateTime 参与玩法判定
            // （跨端不一致）→ 这里用"会话序号"作为 dayIndex 的**可信来源占位**：
            // 联机时它应由网络层同步；单机用 0 表示"今天"。取到官方正文后再决定真实日历口径。
            _tasks.RollForDay(0);
            _power = new Whisper.Gameplay.Power.PowerSystem(cfg);
            _interaction = new Whisper.Gameplay.Interaction.InteractionSystem(cfg, _power);

            lines.AppendLine(_progression.Describe());
            lines.AppendLine(_shop.Describe());
            lines.AppendLine(_tasks.Describe().Split('\\n')[0]);
            lines.AppendLine(_power.Describe());
            lines.AppendLine(_interaction.Describe());
        }

        void BuildMenu(System.Text.StringBuilder lines)`);
  s = s.replace('            BuildMenu(lines);\n            FinishBoot(t0, lines);',
                '            InitProgressionSystems(lines);\n            BuildMenu(lines);\n            FinishBoot(t0, lines);');
  log.push('  ✓ 初始化 + 接线');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
