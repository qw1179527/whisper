// 把「主界面先出、点开始再建对局」接好（node 改，避免 PowerShell 引号地狱）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 字段
if (!s.includes('bool _matchStarted;')) {
  s = s.replace('        MenuScene _menu;',
`        MenuScene _menu;
        /// <summary>对局是否已开始（幂等保护：主界面按钮可能被连点）。</summary>
        bool _matchStarted;`);
  log.push('  ✓ _matchStarted');
}

// ② 主界面按钮 → 真的开对局
s = s.replace('            if (_jumpscare != null) _jumpscare.Reset();\n        }',
              '            if (_jumpscare != null) _jumpscare.Reset();\n            StartMatch();\n        }');
if (s.includes('StartMatch();\n        }')) log.push('  ✓ 按钮接 StartMatch');

// ③ AppendStatus（HUD 追加文字；没有就日志兜底）
if (!s.includes('void AppendStatus(')) {
  s = s.replace('        /// <summary>建主界面（3D 空间 + 闪烁灯 + 右下角手电筒 + 概率刷鬼 + 右侧玩法选项）。</summary>',
`        /// <summary>把一段文字同时写进 HUD 与日志（HUD 可能尚未建好，故日志是兜底而不是唯一出路）。</summary>
        void AppendStatus(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log("[Whisper] " + text);
            if (_status != null) _status.text = text;
        }

        /// <summary>建主界面（3D 空间 + 闪烁灯 + 右下角手电筒 + 概率刷鬼 + 右侧玩法选项）。</summary>`);
  log.push('  ✓ AppendStatus');
}

// ④ 去掉 TrySpawnPlayer 里那次重复的 MenuScene 构建（现在由 BuildMenu 负责，只建一次）
const dupMenu = `
                // 主界面：代码构建（3D 空间 + 闪烁灯 + 右下角手电筒 + 概率刷鬼 + 右侧玩法选项）
                _menu = gameObject.AddComponent<MenuScene>();
                _menu.Build(transform);
                lines.AppendLine(_menu.Describe());`;
if (s.includes(dupMenu)) { s = s.replace(dupMenu, ''); log.push('  ✓ 去掉重复的主界面构建'); }
else log.push('  · 无重复构建（已清理或锚点不同）');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
for (const k of ['_matchStarted', 'public void StartMatch()', 'BuildMenu(lines)', 'StartMatch();', 'void AppendStatus(']) {
  console.log(`  ${k} → ${(s.split(k).length - 1)} 处`);
}
