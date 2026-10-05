// 补齐主界面/跳脸用到的桩成员（**这次先读真实内容再改**，不再猜锚点）。
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
let s = fs.readFileSync(P, 'utf8');
const rules = [];

// ① Light：range / spotAngle / shadows（出处 ScriptReference/Light）
rules.push([
  `        public float intensity { get; set; }
        public Color color { get; set; }
    }
`,
  `        public float intensity { get; set; }
        public Color color { get; set; }
        /// <summary>照射范围（出处 ScriptReference/Light-range）。主界面天花板灯靠它铺满房间；
        /// 跳脸的脸部补光也用它。桩里此前没有 → 真实代码被误报 CS1061。</summary>
        public float range { get; set; }
        public float spotAngle { get; set; }
        public LightShadows shadows { get; set; }
    }

    /// <summary>阴影模式（出处 ScriptReference/LightShadows）。</summary>
    public enum LightShadows { None, Hard, Soft }
`,
]);

// ② Button.targetGraphic（出处 ScriptReference/Selectable-targetGraphic）
rules.push([
  `    public class Button : Behaviour
    {
        public ButtonClickedEvent onClick => null;
    }`,
  `    public class Button : Behaviour
    {
        public ButtonClickedEvent onClick => null;
        /// <summary>按钮的图形目标（出处 ScriptReference/Selectable-targetGraphic）。</summary>
        public Graphic targetGraphic { get; set; }
    }`,
]);

// ③ Transform.forward / right / up（出处 ScriptReference/Transform-forward）
rules.push([
  `        public Vector3 localScale { get; set; }`,
  `        public Vector3 localScale { get; set; }
        /// <summary>朝向（出处 ScriptReference/Transform-forward）。跳脸要把脸贴在相机正前方。</summary>
        public Vector3 forward { get; set; }
        public Vector3 right { get; set; }
        public Vector3 up { get; set; }`,
]);

// ④ GameObject.GetComponentInChildren<T>
rules.push([
  `        public T GetComponent<T>() => default;`,
  `        public T GetComponent<T>() => default;
        /// <summary>在子物体里找组件（出处 ScriptReference/GameObject.GetComponentInChildren）。</summary>
        public T GetComponentInChildren<T>() => default;`,
]);

// ⑤ Mathf.Lerp / Clamp01（若缺失则补）
if (!/public static float Lerp\(float a, float b, float t\)/.test(s)) {
  rules.push([
    `        public static float Max(float a, float b) => a > b ? a : b;`,
    `        public static float Max(float a, float b) => a > b ? a : b;
        /// <summary>线性插值（出处 ScriptReference/Mathf.Lerp）。跳脸的推进曲线用它。</summary>
        public static float Lerp(float a, float b, float t) => a + (b - a) * (t < 0f ? 0f : (t > 1f ? 1f : t));
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float PerlinNoise(float x, float y) => 0.5f;`,
  ]);
}

let n = 0;
for (const [a, b] of rules) {
  if (s.includes(a)) { s = s.replace(a, b); n++; }
  else console.log('  ! 未匹配：' + a.split('\n')[0].trim().slice(0, 56));
}
fs.writeFileSync(P, s, 'utf8');
console.log(`  ✓ 补 ${n}/${rules.length} 处`);
