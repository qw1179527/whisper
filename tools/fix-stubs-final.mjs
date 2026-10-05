// 桩文件最终收口：**去重** + **补齐**，一次做完（前几次反复改坏，这次不猜锚点，直接按行判定）。
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
let lines = fs.readFileSync(P, 'utf8').split('\n');
const log = [];

// ── ① 去重：同名成员只保留**首次出现** ──
// 键 = 类的识别特征（出现该行即认为进入该类），值 = 该类内允许只出现一次的成员特征
const DEDUP = [
  { cls: 'public struct Vector2', keys: ['public static Vector2 one', 'public static Vector2 zero'] },
  { cls: 'public struct Vector3', keys: ['public static Vector3 one', 'public static Vector3 forward', 'public static Vector3 right', 'public static Vector3 up', 'public static Vector3 operator -(Vector3'] },
  { cls: 'public class Transform', keys: ['public Vector3 forward', 'public Vector3 right', 'public Vector3 up', 'public Quaternion localRotation', 'public System.Collections.Generic.IEnumerator<Transform> GetEnumerator'] },
  { cls: 'public class Resources', keys: ['public byte[] bytes'] },
  { cls: 'public class Application', keys: ['public static string streamingAssetsPath'] },
];
let curCls = null;
const seen = new Map();   // `${cls}|${key}` → true
const keep = [];
let removed = 0;
for (const line of lines) {
  const t = line.trim();
  const entered = DEDUP.find((d) => t.startsWith(d.cls));
  if (entered) curCls = entered.cls;
  else if (t === '}' || t.startsWith('    public ') || t.startsWith('    public static class') || t.startsWith('    public struct') || t.startsWith('    public class')) {
    // 离开当前类：遇到新的类型声明或顶层闭合
    if (t.startsWith('    public class') || t.startsWith('    public struct') || t.startsWith('    public static class') || t.startsWith('    public enum')) {
      if (!DEDUP.some((d) => t.startsWith(d.cls))) curCls = null;
    }
  }
  let drop = false;
  if (curCls) {
    const rule = DEDUP.find((d) => d.cls === curCls);
    const key = rule && rule.keys.find((k) => t.startsWith(k));
    if (key) {
      const id = curCls + '|' + key;
      if (seen.has(id)) { drop = true; removed++; }
      else seen.set(id, true);
    }
  }
  if (!drop) keep.push(line);
}
lines = keep;
log.push(`  ✓ 去重删除 ${removed} 行`);

// ── ② 补齐：用唯一整行锚点 ──
let s = lines.join('\n');
let added = 0;
function after(anchor, add, tag) {
  const hits = s.split(anchor).length - 1;
  if (hits !== 1) { log.push(`  ! ${tag}：锚点 ${hits} 次，跳过`); return; }
  if (s.includes(add[0].trim()) && add[0].trim().startsWith('public')) { log.push(`  · ${tag} 已存在`); return; }
  s = s.replace(anchor, anchor + '\n' + add.join('\n'));
  added++; log.push(`  ✓ ${tag}`);
}

after('        public static Vector2 zero => new Vector2(0, 0);',
  ['        public static Vector2 one => new Vector2(1, 1);'], 'Vector2.one');

after('        public static Vector3 zero => new Vector3(0, 0, 0);',
  ['        public static Vector3 one => new Vector3(1, 1, 1);',
   '        public static Vector3 forward => new Vector3(0, 0, 1);',
   '        public static Vector3 right => new Vector3(1, 0, 0);',
   '        public static Vector3 up => new Vector3(0, 1, 0);',
   '        /// <summary>一元负号（出处 ScriptReference/Vector3-operator_UnaryNegation）。',
   '        /// 桩里只有二元减号时，-pivot 会报 CS0023 —— 先查桩再改代码。</summary>',
   '        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);',
   '        public static Vector3 Cross(Vector3 a, Vector3 b)',
   '            => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);'], 'Vector3 成员');

after('        public static Quaternion identity => new Quaternion(0, 0, 0, 1);',
  ['        /// <summary>欧拉角（出处 ScriptReference/Quaternion-eulerAngles）。第一人称视角读写用它。</summary>',
   '        public Vector3 eulerAngles { get; set; }',
   '        public static Quaternion Euler(float x, float y, float z) => new Quaternion(x, y, z, 1f);',
   '        public static Quaternion Euler(Vector3 e) => new Quaternion(e.x, e.y, e.z, 1f);',
   '        public static Quaternion LookRotation(Vector3 forward) => identity;'], 'Quaternion 成员');

// Shader.SetGlobalFloat
{
  const i = s.indexOf('    public class Shader');
  if (i >= 0 && !s.slice(i, s.indexOf('\n    }', i)).includes('SetGlobalFloat')) {
    const end = s.indexOf('\n    }', i);
    s = s.slice(0, end) + '\n        /// <summary>设全局浮点（出处 ScriptReference/Shader.SetGlobalFloat）。雾密度用它。</summary>\n        public static void SetGlobalFloat(string name, float value) { }' + s.slice(end);
    added++; log.push('  ✓ Shader.SetGlobalFloat');
  }
}
// Application.streamingAssetsPath
{
  const i = s.indexOf('    public class Application');
  if (i >= 0 && !s.slice(i, s.indexOf('\n    }', i)).includes('streamingAssetsPath')) {
    const end = s.indexOf('\n    }', i);
    s = s.slice(0, end) + '\n        /// <summary>StreamingAssets 路径（出处 ScriptReference/Application-streamingAssetsPath）。</summary>\n        public static string streamingAssetsPath => null;' + s.slice(end);
    added++; log.push('  ✓ Application.streamingAssetsPath');
  }
}
// Mathf.RoundToInt
{
  const i = s.indexOf('    public static class Mathf');
  if (i >= 0) {
    const end = s.indexOf('\n    }', i);
    const body = s.slice(i, end);
    if (!body.includes('RoundToInt')) {
      s = s.slice(0, end) + '\n        public static int RoundToInt(float v) => (int)Math.Round(v);\n        public static float Round(float v) => (float)Math.Round(v);' + s.slice(end);
      added++; log.push('  ✓ Mathf.RoundToInt/Round');
    }
  }
}
// Camera：WorldToScreenPoint / ScreenPointToRay
{
  const i = s.indexOf('    public class Camera');
  if (i >= 0) {
    const end = s.indexOf('\n    }', i);
    const body = s.slice(i, end);
    const add = [];
    if (!body.includes('WorldToScreenPoint')) add.push('        /// <summary>世界 → 屏幕（出处 ScriptReference/Camera.WorldToScreenPoint）。门交互的屏幕命中用它。</summary>',
      '        public Vector3 WorldToScreenPoint(Vector3 position) => position;');
    if (!body.includes('ScreenPointToRay')) add.push('        /// <summary>屏幕 → 射线（出处 ScriptReference/Camera.ScreenPointToRay）。</summary>',
      '        public Ray ScreenPointToRay(Vector3 position) => new Ray();');
    if (add.length) { s = s.slice(0, end) + '\n' + add.join('\n') + s.slice(end); added++; log.push('  ✓ Camera 成员'); }
  }
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
const open = (s.match(/\{/g) || []).length, close = (s.match(/\}/g) || []).length;
console.log(`  ✓ 补齐 ${added} 处 · 行数 ${s.split('\n').length} · 花括号 { ${open} / } ${close} ${open === close ? '平衡 ✓' : '不平衡 ✗'}`);
