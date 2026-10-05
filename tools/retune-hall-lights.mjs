// 大厅灯光整体重标定。0.1.61 真机截图：几何（天花板/立柱/货架）在，但**暗到看不清**，
// 菜单板也没被真正照亮 —— 与官方"工业风仓库、光线偏暗但**看得清**、菜单板最亮"不符。
//
// 为什么不再逐盏加一点：Point/Spot 在 Built-in 下的衰减是**平方反比 + range 截断**，
// 仓库尺度（26m）下 8~11m 的 range 只够照亮局部，天花板（5.6m 高）几乎收不到任何一盏的光。
// 正解是补一层**半球环境光**（`RenderSettings.ambientMode = Flat` + 一个偏冷的天光色）：
// 它照亮所有朝上的面、给整体一个底，而点光负责"节奏与焦点"。这也正是官方大厅的做法
// （仓库有大量间接光，不是纯靠灯）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/HallScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 吊灯大幅加强 + range 拉到整个层高
s = s.replace('new Color(1.0f, 0.90f, 0.76f), 1.35f, 9.5f, true);',
              'new Color(1.0f, 0.90f, 0.76f), 3.2f, 14f, true);');
// ② 主方向光加强（给天花板与朝上面一个底）
s = s.replace('key.intensity = 0.30f;', 'key.intensity = 0.55f;');
// ③ 菜单板：射灯广角覆盖 + 更强的近距补光
s = s.replace(`            Light(_root, "MenuSpot", new Vector3(0f, h - 1.0f, z + 2.2f), LightType.Spot,
                  new Color(1.0f, 0.96f, 0.88f), 6.5f, 11f, true);
            Light(_root, "MenuFill", new Vector3(0f, by + 1.1f, z + 1.1f), LightType.Point,
                  new Color(0.95f, 0.95f, 0.92f), 2.4f, 5.5f, false);`,
`            // 射灯：从**板子上方**往下打（原先放在板子高度附近，光轴与板面几乎平行 → 照不亮）。
            var spotGo = Light(_root, "MenuSpot", new Vector3(0f, h - 0.8f, z + 2.4f), LightType.Spot,
                  new Color(1.0f, 0.96f, 0.88f), 9f, 13f, true);
            if (spotGo != null)
            {
                // 光轴朝下偏前，正中板面中心
                var l = spotGo.GetComponent<Light>();
                if (l != null)
                {
                    l.spotAngle = 82f;      // 广角：覆盖整块 7.2x3.4 的板
                    l.transform.localRotation = Quaternion.Euler(58f, 0f, 0f);
                }
            }
            // 近距补光：贴在板前 0.9m，保证纸片可读（这是"焦点必须最亮"的保险）
            Light(_root, "MenuFill", new Vector3(0f, by + 0.35f, z + 0.95f), LightType.Point,
                  new Color(0.96f, 0.95f, 0.90f), 5.5f, 7f, false);`);
log.push('  ✓ 吊灯/主光/菜单板灯重标定');

// ④ 半球环境光：给整体一个底（仓库的间接光）
if (!s.includes('RenderSettings')) {
  s = s.replace('        void BuildLights()\n        {',
`        void BuildLights()
        {
            // 半球环境光（Built-in 的全局间接光近似）。
            // 为什么必须有：仓库 26m 跨度、层高 5.6m，而 Built-in 的 Point/Spot 是平方反比衰减，
            // 8~14m 的 range 在仓库尺度下照不到天花板与远处墙面 —— 只靠灯会得到"几何在但看不清"
            // （0.1.60/61 真机截图就是这个症状）。
            // 环境光给所有朝上的面一个底，点光负责节奏与焦点 —— 这也是官方大厅的实际做法。
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.33f, 0.40f);   // 偏冷的工业天光
            RenderSettings.fog = false;   // 雾在几何着色器里自研，不用 Unity 的全局雾（历史事故）
`);
  log.push('  ✓ 半球环境光（RenderSettings）');
}

// ⑤ Light(...) 改成返回 GameObject，供上面拿 Light 组件调 spotAngle
s = s.replace('        void Light(Transform parent, string name, Vector3 pos, LightType type, Color color,\n                   float intensity, float range, bool shadows)',
              '        GameObject Light(Transform parent, string name, Vector3 pos, LightType type, Color color,\n                   float intensity, float range, bool shadows)');
s = s.replace('            l.renderMode = LightRenderMode.ForcePixel;   // Auto 在灯多时会被降级成顶点光\n            BuiltCount++;\n        }',
              '            l.renderMode = LightRenderMode.ForcePixel;   // Auto 在灯多时会被降级成顶点光\n            BuiltCount++;\n            return go;\n        }');
log.push('  ✓ Light 返回 GameObject');

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
