// 用**行级**方式补桩（regex/多行锚点在这份文件里反复失配，改成按行号区间插入才可靠）。
import fs from 'node:fs';

const P = 'native/unity-stubs/UnityStubs.cs';
const lines = fs.readFileSync(P, 'utf8').split('\n');

/** 在 `class <name>` 那个大括号块内、闭合花括号之前插入若干行 */
function insertIntoClass(src, className, injectLines) {
  let start = -1;
  for (let i = 0; i < src.length; i++) {
    if (src[i].trim() === `public class ${className} : Behaviour` || src[i].trim() === `public class ${className}`) { start = i; break; }
  }
  if (start < 0) return { ok: false, why: `找不到 class ${className}` };
  // 找该块的闭合（同缩进级别的 `    }`）
  let end = -1;
  for (let i = start + 1; i < src.length; i++) {
    if (src[i] === '    }') { end = i; break; }
  }
  if (end < 0) return { ok: false, why: `找不到 class ${className} 的闭合` };
  // 已存在则跳过
  const body = src.slice(start, end).join('\n');
  const already = injectLines.some((l) => l.trim() && body.includes(l.trim()));
  if (already) return { ok: true, why: '已存在，跳过' };
  const out = [...src.slice(0, end), ...injectLines, ...src.slice(end)];
  return { ok: true, out };
}

let cur = lines;
let done = 0;

// ① Light.range / spotAngle / shadows
{
  const r = insertIntoClass(cur, 'Light', [
    '        /// <summary>照射范围（出处 ScriptReference/Light-range）。主界面天花板灯与跳脸补光靠它。</summary>',
    '        public float range { get; set; }',
    '        public float spotAngle { get; set; }',
    '        public LightShadows shadows { get; set; }',
  ]);
  if (r.ok && r.out) { cur = r.out; done++; }
  console.log(`  Light: ${r.why ?? '已插入'}`);
}
// ② Button.targetGraphic
{
  const r = insertIntoClass(cur, 'Button', [
    '        /// <summary>按钮的图形目标（出处 ScriptReference/Selectable-targetGraphic）。</summary>',
    '        public Graphic targetGraphic { get; set; }',
  ]);
  if (r.ok && r.out) { cur = r.out; done++; }
  console.log(`  Button: ${r.why ?? '已插入'}`);
}
// ③ LightShadows 枚举（放在 Light 类之后）
if (!cur.some((l) => l.includes('enum LightShadows'))) {
  let at = -1;
  for (let i = 0; i < cur.length; i++) if (cur[i].trim() === 'public class Light : Behaviour') { at = i; break; }
  if (at >= 0) {
    let end = -1;
    for (let i = at + 1; i < cur.length; i++) if (cur[i] === '    }') { end = i; break; }
    cur = [...cur.slice(0, end + 1),
      '',
      '    /// <summary>阴影模式（出处 ScriptReference/LightShadows）。</summary>',
      '    public enum LightShadows { None, Hard, Soft }',
      ...cur.slice(end + 1)];
    done++;
    console.log('  LightShadows: 已插入');
  }
}
// ④ Material.SetFloat / EnableKeyword
{
  let at = -1;
  for (let i = 0; i < cur.length; i++) if (cur[i].trim() === 'public class Material : Object') { at = i; break; }
  if (at >= 0) {
    let end = -1;
    for (let i = at + 1; i < cur.length; i++) if (cur[i] === '    }') { end = i; break; }
    const body = cur.slice(at, end).join('\n');
    const add = [];
    if (!body.includes('SetFloat')) add.push('        /// <summary>设浮点属性（出处 ScriptReference/Material.SetFloat）。</summary>', '        public void SetFloat(string name, float value) { }');
    if (!body.includes('EnableKeyword')) add.push('        /// <summary>启用关键字（出处 ScriptReference/Material.EnableKeyword）。</summary>', '        public void EnableKeyword(string keyword) { }');
    if (add.length) { cur = [...cur.slice(0, end), ...add, ...cur.slice(end)]; done++; console.log('  Material: 已插入 SetFloat/EnableKeyword'); }
    else console.log('  Material: 已存在，跳过');
  }
}
// ⑤ GameObject.GetComponentInChildren
{
  let at = -1;
  for (let i = 0; i < cur.length; i++) if (cur[i].trim() === 'public class GameObject : Object') { at = i; break; }
  if (at >= 0) {
    let end = -1;
    for (let i = at + 1; i < cur.length; i++) if (cur[i] === '    }') { end = i; break; }
    const body = cur.slice(at, end).join('\n');
    if (!body.includes('GetComponentInChildren')) {
      cur = [...cur.slice(0, end),
        '        /// <summary>在子物体里找组件（出处 ScriptReference/GameObject.GetComponentInChildren）。</summary>',
        '        public T GetComponentInChildren<T>() => default;',
        ...cur.slice(end)];
      done++;
      console.log('  GameObject: 已插入 GetComponentInChildren');
    } else console.log('  GameObject: 已存在，跳过');
  }
}

fs.writeFileSync(P, cur.join('\n'), 'utf8');
console.log(`  ✓ 共 ${done} 处`);
