// 补齐 `native/unity-stubs/UnityStubs.cs` 的桩成员。
//
// ## 这次的纪律（前两次把文件改坏了，教训写死在这里）
// 1. **不做"找大括号再插入"** —— 那份文件里有嵌套类、注释里的花括号，按缩进找闭合必然错位
//    （我上一版把内容插到了文件首尾，直接语法崩 5 处）。
// 2. 改为**精确整块字符串替换**：锚点是文件里**逐字复制**下来的完整块，且**每块只出现一次**。
// 3. 替换前先 `includes` 检查，替换后**复核每块只命中一次**；任何一条失配就打印出来不静默。
// 4. 已确认（git 恢复后的干净版本）：`Light`/`Button`/`Material`/`GameObject`/`Mathf` 的原始块如下。
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
let s = fs.readFileSync(P, 'utf8');

const RULES = [
  // ① Light：range / spotAngle / shadows（出处 ScriptReference/Light）
  [
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
        /// <summary>照射范围（出处 ScriptReference/Light-range）。主界面天花板灯与跳脸补光靠它。</summary>
        public float range { get; set; }
        public float spotAngle { get; set; }
        public LightShadows shadows { get; set; }
    }

    /// <summary>阴影模式（出处 ScriptReference/LightShadows）。</summary>
    public enum LightShadows { None, Hard, Soft }`,
  ],
  // ② Material：SetFloat / EnableKeyword / HasProperty（出处 ScriptReference/Material.*）
  [
    `    public class Material : Object
    {
        public Material(Shader shader) { }
        public Color color { get; set; }
    }`,
    `    public class Material : Object
    {
        public Material(Shader shader) { }
        public Color color { get; set; }
        /// <summary>是否有该属性（出处 ScriptReference/Material.HasProperty）。
        /// 不同着色器属性名不同，**必须先问再设**，否则设值静默无效。</summary>
        public bool HasProperty(string name) => false;
        /// <summary>设颜色（出处 ScriptReference/Material.SetColor）。</summary>
        public void SetColor(string name, Color value) { }
        /// <summary>设浮点（出处 ScriptReference/Material.SetFloat）。</summary>
        public void SetFloat(string name, float value) { }
        /// <summary>启用着色器关键字（出处 ScriptReference/Material.EnableKeyword）。</summary>
        public void EnableKeyword(string keyword) { }
    }`,
  ],
  // ③ Button：targetGraphic（出处 ScriptReference/Selectable-targetGraphic）
  [
    `    public class Button : Behaviour
    {
        public ButtonClickedEvent onClick => null;
    }`,
    `    public class Button : Behaviour
    {
        public ButtonClickedEvent onClick => null;
        /// <summary>按钮的图形目标（出处 ScriptReference/Selectable-targetGraphic）。</summary>
        public Graphic targetGraphic { get; set; }
        public RectTransform GetComponent() => null;
    }`,
  ],
];

let applied = 0;
for (const [a, b] of RULES) {
  const hits = s.split(a).length - 1;
  if (hits !== 1) { console.log(`  ! 锚点命中 ${hits} 次（期望 1），跳过：${a.split('\n')[0].trim().slice(0, 52)}`); continue; }
  s = s.replace(a, b);
  applied++;
}

// ④ GameObject / Mathf / Transform 的单行追加（用**唯一整行**做锚点）
const LINE_RULES = [
  [`        public Renderer GetComponent() => null;`, null],   // 占位，下面按类块处理
];
// GameObject.GetComponentInChildren
{
  const anchor = `    public class GameObject : Object`;
  const i = s.indexOf(anchor);
  if (i >= 0) {
    const end = s.indexOf('\n    }', i);
    const body = s.slice(i, end);
    if (!body.includes('GetComponentInChildren')) {
      s = s.slice(0, end) + `\n        /// <summary>在子物体里找组件（出处 ScriptReference/GameObject.GetComponentInChildren）。</summary>` +
          `\n        public T GetComponentInChildren<T>() => default;` + s.slice(end);
      applied++;
    }
  }
}
// Mathf.Lerp / Clamp01 / PerlinNoise
{
  const anchor = `    public static class Mathf\n    {`;
  const i = s.indexOf(anchor);
  if (i >= 0) {
    const end = s.indexOf('\n    }', i);
    const body = s.slice(i, end);
    const add = [];
    if (!body.includes('Lerp(')) add.push('        /// <summary>线性插值（出处 ScriptReference/Mathf.Lerp）。</summary>', '        public static float Lerp(float a, float b, float t) => a + (b - a) * (t < 0f ? 0f : (t > 1f ? 1f : t));');
    if (!body.includes('Clamp01')) add.push('        /// <summary>钳到 [0,1]（出处 ScriptReference/Mathf.Clamp01）。</summary>', '        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);');
    if (!body.includes('PerlinNoise')) add.push('        /// <summary>Perlin 噪声（出处 ScriptReference/Mathf.PerlinNoise）。灯光"旧灯管抖动"用它。</summary>', '        public static float PerlinNoise(float x, float y) => 0.5f;');
    if (add.length) { s = s.slice(0, end) + '\n' + add.join('\n') + s.slice(end); applied++; }
  }
}
// Time.time
{
  const anchor = `    public static class Time\n    {`;
  const i = s.indexOf(anchor);
  if (i >= 0) {
    const end = s.indexOf('\n    }', i);
    const body = s.slice(i, end);
    if (!body.includes('float time')) {
      s = s.slice(0, end) + `\n        /// <summary>自游戏开始的时间（出处 ScriptReference/Time-time）。灯光抖动相位用它。</summary>` +
          `\n        public static float time => 0f;` + s.slice(end);
      applied++;
    }
  }
}
// Transform.forward
{
  const anchor = `    public class Transform : Component\n    {`;
  const i = s.indexOf(anchor);
  if (i >= 0) {
    const end = s.indexOf('\n    }', i);
    const body = s.slice(i, end);
    if (!body.includes('forward')) {
      s = s.slice(0, end) + `\n        /// <summary>朝向（出处 ScriptReference/Transform-forward）。跳脸要把脸贴在相机正前方。</summary>` +
          `\n        public Vector3 forward { get; set; }\n        public Vector3 right { get; set; }\n        public Vector3 up { get; set; }` + s.slice(end);
      applied++;
    }
  }
}

fs.writeFileSync(P, s, 'utf8');
console.log(`  ✓ 应用 ${applied} 处（整块替换 ${RULES.length} 条规则逐条校验过命中次数）`);
