// 给 `native/unity-stubs/UnityStubs.cs` 补主界面用到的成员。
//
// 【为什么要补桩而不是改代码】这些**都是真 Unity 里存在的成员**（Light.range / Mathf.Lerp /
// Mathf.PerlinNoise / Time.time），只是桩里没声明 → 语法门禁把"真实的代码"报成 CS1061/CS0117。
// 本项目的纪律：CS0117/CS1061/CS1503/CS0023/CS1579 这五类**先查桩、再改代码**。
// 补桩的附带收益：这些用法从此**真的被本机验证**（以前会被归为"Unity 缺失"而放行）。
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
let s = fs.readFileSync(P, 'utf8');
const rules = [];

// ① Light：range / spotAngle / shadows（出处 ScriptReference/Light）
rules.push([
  `    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
    }`,
  `    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
        /// <summary>照射范围（出处 ScriptReference/Light-range）。主界面天花板灯靠它铺满房间。</summary>
        public float range { get; set; }
        /// <summary>聚光灯锥角（出处 ScriptReference/Light-spotAngle）。</summary>
        public float spotAngle { get; set; }
        public LightShadows shadows { get; set; }
    }

    /// <summary>阴影模式（出处 ScriptReference/LightShadows）。</summary>
    public enum LightShadows { None, Hard, Soft }`,
]);

// ② Mathf：Lerp / PerlinNoise / Round
rules.push([
  `        /// <summary>指数（摇杆平滑用）。出处 docs.unity3d.com ScriptReference/Mathf.Exp.html</summary>
        public static float Exp(float v) => (float)Math.Exp(v);`,
  `        /// <summary>指数（摇杆平滑用）。出处 docs.unity3d.com ScriptReference/Mathf.Exp.html</summary>
        public static float Exp(float v) => (float)Math.Exp(v);
        /// <summary>线性插值（出处 ScriptReference/Mathf.Lerp）。</summary>
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        /// <summary>钳到 [0,1]（出处 ScriptReference/Mathf.Clamp01）。</summary>
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        /// <summary>Perlin 噪声（出处 ScriptReference/Mathf.PerlinNoise）。
        /// 灯光"旧灯管抖动"用它 —— 比随机数更像连续的电压波动。</summary>
        public static float PerlinNoise(float x, float y) => 0.5f;
        public static float Round(float v) => (float)Math.Round(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float PI => 3.14159265f;`,
]);

// ③ Time：time
rules.push([
  `        public static float deltaTime => 0f;`,
  `        public static float deltaTime => 0f;
        /// <summary>自游戏开始的时间（出处 ScriptReference/Time-time）。灯光抖动相位用它。</summary>
        public static float time => 0f;
        public static float fixedDeltaTime => 0f;
        public static float timeScale { get; set; }`,
]);

// ④ Resources.GetBuiltinResource<T>
rules.push([
  `    public class Resources`,
  `    public class Resources`,
]);
rules.push([
  `        public static T Load<T>(string path) where T : Object => default;`,
  `        public static T Load<T>(string path) where T : Object => default;
        /// <summary>取内置资源（出处 ScriptReference/Resources.GetBuiltinResource）。
        /// 文本要字体：<c>Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf")</c>。</summary>
        public static T GetBuiltinResource<T>(string path) where T : Object => default;`,
]);

let n = 0;
for (const [a, b] of rules) {
  if (a === b) continue;
  if (s.includes(a)) { s = s.replace(a, b); n++; }
  else console.log('  ! 未匹配：' + a.split('\n')[0].trim().slice(0, 60));
}
fs.writeFileSync(P, s, 'utf8');
console.log(`  ✓ 补 ${n} 处`);
