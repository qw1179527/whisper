// 把 UI 画布创建抽成**幂等**的 BuildCanvas()，并从 BuildOptions() 调用。
//
// 【为什么】画布原先在 `BuildOptions()` 里就地建、`_canvas` 也只在那里赋值。
// 本轮 3D 场景换成 HallScene 后 BuildRoom() 不再被调用，而我把 BuildBoardUi() 排在了
// BuildOptions() 之前 → `_canvas` 是 null → BuildBoardUi() **静默返回** →
// "进入菜单板操作视角但一个选项都不显示"（0.1.67 的 HUD 诊断 `板UI:未建` 抓到）。
//
// 修法不是"把调用顺序挪对"，而是**让依赖自足**：谁要用画布谁就先确保它存在。
// 顺序一改就失效的代码，下次换场景还会再犯。
//
// 行号定位（不依赖注释文本匹配 —— 这个文件我已被字符串锚点坑过三次）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
const lines = fs.readFileSync(P, 'utf8').split('\n');
const NL = '\n';

if (lines.some((l) => l.includes('void BuildCanvas()'))) {
  console.log('  · BuildCanvas 已存在');
  process.exit(0);
}

// 找 BuildOptions 起始与其内部画布段落（从 `var canvasGo = new GameObject("MenuCanvas");`
// 到 `_canvas = canvas;` 后的 EventSystem 块结束）
let buildOptionsAt = -1, canvasGoAt = -1, eventBlockEnd = -1;
for (let i = 0; i < lines.length; i++) {
  if (buildOptionsAt < 0 && lines[i].includes('void BuildOptions()')) buildOptionsAt = i;
  if (canvasGoAt < 0 && lines[i].includes('var canvasGo = new GameObject("MenuCanvas");')) canvasGoAt = i;
  if (canvasGoAt >= 0 && eventBlockEnd < 0 && lines[i].includes('es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();')) {
    // 再往后找该 if 块的收尾 `}`
    for (let k = i + 1; k < i + 6; k++) if (lines[k].trim() === '}') { eventBlockEnd = k; break; }
  }
}
if (buildOptionsAt < 0 || canvasGoAt < 0 || eventBlockEnd < 0) {
  console.error(`  ✗ 定位失败 buildOptions=${buildOptionsAt} canvasGo=${canvasGoAt} end=${eventBlockEnd}`);
  process.exit(1);
}

// 取出画布段落原文（缩进 12 空格），改成方法体（缩进 12 保持）
const body = lines.slice(canvasGoAt, eventBlockEnd + 1);

const method = [
  '        // ───────────────────────── UI 画布 ─────────────────────────',
  '        /// <summary>',
  '        /// 建 UI 画布（**幂等**：已有就直接返回）。',
  '        /// </summary>',
  '        /// <remarks>',
  '        /// 原先画布是在 BuildOptions() 里就地建的，`_canvas` 也只在那里赋值。',
  '        /// 本轮 3D 场景换成 HallScene 后 BuildRoom() 不再被调用，而 BuildBoardUi() 排在了',
  '        /// BuildOptions() 之前 → `_canvas` 为 null → BuildBoardUi() 静默返回 →',
  '        /// "进入菜单板操作视角但一个选项都不显示"（0.1.67 HUD 诊断 `板UI:未建` 抓到）。',
  '        /// 修法不是"把调用顺序挪对"，而是**让依赖自足**：谁要用画布谁就先确保它存在 ——',
  '        /// 顺序一改就失效的代码，下次换场景还会再犯一遍。',
  '        /// </remarks>',
  '        void BuildCanvas()',
  '        {',
  '            if (_canvas != null) return;',
  ...body,
  '        }',
  '',
  '        // ───────────────────────── 右侧玩法选项 ─────────────────────────',
  '        void BuildOptions()',
  '        {',
  '            BuildCanvas();',
];

// 替换：把 [buildOptions 的注释行 .. eventBlockEnd] 换成 method（注释行单独保留在上面）
const startReplace = buildOptionsAt - 1;   // 含上一行 `// ───── 右侧玩法选项 ─────`
const out = [
  ...lines.slice(0, startReplace),
  ...method,
  ...lines.slice(eventBlockEnd + 1),
];
fs.writeFileSync(P, out.join(NL), 'utf8');
console.log(`  ✓ 抽出 BuildCanvas()（原画布段落 ${body.length} 行）并在 BuildOptions 里调用`);
