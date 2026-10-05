// 修「主界面按钮点不动」——根因：代码构建的 Canvas **没有 EventSystem**。
//
// 真机实测：0.1.22 里按钮渲染正常、但点击毫无反应（用户确认「点按钮也没反应」）。
// 原因：`GraphicRaycaster` 只负责"把点击变成 UI 事件"，真正派发点击的是 **EventSystem**
// （+ 一个 InputModule）。场景是 Boot.unity 且 UI 全部代码构建 → **没有任何地方会创建 EventSystem**
// → 射线永远不派发 → 按钮静默失效（不报错、不打日志，所以最难查）。
//
// 同时补两件取证用的东西：
//   · 按钮回调打日志（真机上能区分"没点到"与"点到了但逻辑没生效"）
//   · 3D 空间加一盏正对房间的方向光兜底（Unlit 材质不吃灯，但主界面里我用的是标准材质；
//     全黑说明光没起作用，先把"看得见"保证住）
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① EventSystem：没有它按钮就点不动
if (!s.includes('EventSystem')) {
  const anchor = '            canvasGo.AddComponent<GraphicRaycaster>();';
  if (s.includes(anchor)) {
    s = s.replace(anchor, anchor + `

            // ⚠⚠ 没有 EventSystem 时 GraphicRaycaster **完全不派发点击** —— 按钮渲染正常但点不动，
            // 而且**不报错、不打日志**（真机实测：0.1.22 就是这么失效的，极难查）。
            // 本工程的 UI 全部代码构建、场景只有 Boot.unity，所以没有任何地方会替我们建它，必须自己补。
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(_root, false);
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }`);
    log.push('  ✓ EventSystem + StandaloneInputModule');
  } else log.push('  ! GraphicRaycaster 锚点未中');
} else log.push('  · EventSystem 已存在');

// ② 按钮回调打日志
if (!s.includes('主界面按钮')) {
  const a2 = '        void OnOption(int index, string label)\n        {';
  if (s.includes(a2)) {
    s = s.replace(a2, a2 + `
            // 真机取证用：区分「没点到」与「点到了但逻辑没生效」——只靠截图分不出来。
            Debug.Log("[Whisper] 主界面按钮 " + index + " 被点击：" + label);`);
    log.push('  ✓ 按钮日志');
  } else log.push('  ! OnOption 锚点未中');
}

// ③ 3D 空间：加一盏方向光兜底（保证"看得见"），并把天花板灯强度提高一档
if (!s.includes('MenuKeyLight')) {
  const a3 = '            // 补充微光：让"远处"还能隐约看到墙的轮廓（纯黑会让玩家以为贴图丢了）';
  if (s.includes(a3)) {
    s = s.replace(a3, `            // 主方向光：**兜底照明**。实测 0.1.22 主界面 3D 空间全黑（点光没照出来），
            // 先用一盏方向光把空间"点亮到看得见"，再谈氛围 —— 恐怖感可以靠调暗实现，
            // 但"全黑看不出有没有场景"会让玩家以为游戏坏了。
            var keyGo = new GameObject("MenuKeyLight");
            keyGo.transform.SetParent(room, false);
            keyGo.transform.localPosition = new Vector3(w * 0.2f, h, 0f);
            keyGo.transform.localRotation = Quaternion.Euler(58f, -22f, 0f);
            var keyLight = keyGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 0.55f;
            keyLight.color = new Color(0.72f, 0.76f, 0.86f);

${a3}`);
    log.push('  ✓ 方向光兜底');
  } else log.push('  ! 补充微光锚点未中');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
console.log(`  EventSystem → ${s.split('EventSystem').length - 1} 处 · MenuKeyLight → ${s.split('MenuKeyLight').length - 1} 处`);
