// 移除我上一步**重复添加**的桩成员（`Color.black/white`、`Resources.GetBuiltinResource`）。
//
// 教训：补桩前**必须先确认桩里有没有**（`black`/`white`/`GetBuiltinResource` 都已存在），
// 否则会出现 CS0102（重复定义）与 CS0229（二义性）—— 而二义性错误会**污染所有调用点**
// （GameBootstrap/HudBuilder/MonsterViews 都被连带报错），排查成本远高于补桩本身。
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
let s = fs.readFileSync(P, 'utf8');

const dupes = [
  `        /// <summary>整数通道构造（出处 ScriptReference/Color.Color）。</summary>
        public static Color black => new Color(0f, 0f, 0f);
        public static Color white => new Color(1f, 1f, 1f);
        public static Color red30 { get { return new Color(1f, 0f, 0f); } }
`,
  `        /// <summary>取内置资源（出处 ScriptReference/Resources.GetBuiltinResource）。
        /// 文本要字体：<c>Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf")</c>。</summary>
        public static T GetBuiltinResource<T>(string path) where T : Object => default;
`,
];
let n = 0;
for (const d of dupes) {
  if (s.includes(d)) { s = s.replace(d, ''); n++; }
  else console.log('  ! 未找到重复块（可能已删）');
}
fs.writeFileSync(P, s, 'utf8');
console.log(`  ✓ 移除 ${n} 处重复`);
