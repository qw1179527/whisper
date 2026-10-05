// 修主界面的两个模型问题（本文件**不得出现反引号** —— 它会截断 JS 模板，已失败 9 次）。
//
// ① 手电筒：原先用 props 的第 0 个部件（那是**点阵投影仪**，不是手电筒）→ 画面右下角出现一根莫名的黑条。
//    修法：用**元球/圆柱拼一个手电筒**（筒身 + 头 + 镜片自发光）。成本近零，且形状对。
// ② 主界面的鬼：原先调 ModelLibrary.InstantiateWhole("ghost") —— 那是**按部件拼装的旧模型**
//    （躯干/头/四肢分别实例化），在暗场+无灯下看起来就是一团黑。
//    新产出的 8 个 GEO-GhostBody_*（男女 × 4 体型，完整人形、元球生成）才是该用的。
//    修法：在 ModelLibrary 里把 ghostbody 注册成可用模型，主界面按匹配种子随机取一个。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const log = [];

// ── ① ModelLibrary：把 ghostbody 注册为可实例化的"整体模型" ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/ModelLibrary.cs');
  let s = fs.readFileSync(P, 'utf8');
  if (!s.includes('ghostbody')) {
    const a = '        public static readonly string[] KnownIds = { "player", "ghost" };';
    if (s.includes(a)) {
      s = s.replace(a,
`        /// <summary>可整体实例化的模型 id。
        /// 说明：player/ghost 是**按部件拼装**的旧模型；ghostbody 系列是新的**整块人形**（元球生成）。
        /// 主界面与对局都应优先用 ghostbody（见 GhostModelPool）。</summary>
        public static readonly string[] KnownIds = { "player", "ghost", "ghostbody" };`);
      log.push('  ✓ ModelLibrary 注册 ghostbody');
    } else log.push('  ! KnownIds 锚点未中');
    // InstantiateWhole 需要支持"模型目录下只有一个 glb"的形态：ghostbody 目录里是
    // GEO-GhostBody_<sex>_<frame>.glb，每个都是完整模型 —— 用 ResPath 直接加载。
    if (!s.includes('InstantiateSingleFile')) {
      const b = '        public static GameObject InstantiateWhole(string modelId, Transform parent)';
      if (s.includes(b)) {
        s = s.replace(b,
`        /// <summary>加载**单文件整模型**（如 Resources/Models/ghostbody/GEO-GhostBody_male_lanky.glb）。
        /// 与 InstantiateWhole 的区别：那条走 Parts 表按部件拼；这条直接读一个 glb。</summary>
        public static GameObject InstantiateSingleFile(string resPathNoExt, Transform parent, string name)
        {
            var asset = Resources.Load<TextAsset>(resPathNoExt);
            if (asset == null) { LastProblem = "找不到 " + resPathNoExt; return null; }
            if (!Whisper.Gameplay.Level.GlbReader.TryRead(asset.bytes, out var model, out var reason))
            { LastProblem = "解析失败 " + reason; return null; }
            var root = new GameObject(string.IsNullOrEmpty(name) ? "Model" : name);
            if (parent != null) root.transform.SetParent(parent, false);
            for (int i = 0; i < model.Primitives.Count; i++)
            {
                var prim = model.Primitives[i];
                var mesh = new Mesh();
                mesh.SetVertices(prim.Positions);
                if (prim.Normals != null) mesh.SetNormals(prim.Normals);
                if (prim.Uvs != null) mesh.SetUVs(0, prim.Uvs);
                mesh.SetTriangles(prim.Indices, 0);
                mesh.RecalculateBounds();
                var go = new GameObject("Part" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(modelId: "ghost");
            }
            return root;
        }

        public static GameObject InstantiateWhole(string modelId, Transform parent)`);
        log.push('  ✓ ModelLibrary.InstantiateSingleFile');
      } else log.push('  ! InstantiateWhole 锚点未中');
    }
  } else log.push('  · ModelLibrary 已注册');
  fs.writeFileSync(P, s, 'utf8');
}

// ── ② MenuScene：手电筒改自建几何；鬼改用 ghostbody ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
  let s = fs.readFileSync(P, 'utf8');

  // 手电筒：替换掉"取 props[0]"那段
  const oldFl = s.match(/[ \t]*int built = 0;[\s\S]*?mr\.sharedMaterial = SceneMaterials\.Make\(new Color\(0\.11f, 0\.12f, 0\.14f\), 0\.42f\);\n            \}/);
  if (oldFl) {
    const newFl = [
      '            // 手电筒 = **自建几何**（筒身 + 灯头 + 自发光镜片）。',
      '            // 为什么不用 props 模型：props 的第 0 个部件是**点阵投影仪**（我先前拿它当"筒身"，',
      '            // 画面上就是一根莫名的黑条）。形状对不对比"用现成资产"重要。',
      '            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);',
      '            barrel.name = "FlashlightBarrel";',
      '            barrel.transform.SetParent(anchor.transform, false);',
      '            barrel.transform.localPosition = new Vector3(0f, 0f, 0.02f);',
      '            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);',
      '            barrel.transform.localScale = new Vector3(0.075f, 0.16f, 0.075f);',
      '            var bmr = barrel.GetComponent<MeshRenderer>();',
      '            if (bmr != null) bmr.sharedMaterial = SceneMaterials.Lit(new Color(0.12f, 0.13f, 0.155f), 0.42f);',
      '            var bcol = barrel.GetComponent<Collider>(); if (bcol != null) Object.Destroy(bcol);',
      '',
      '            var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);',
      '            head.name = "FlashlightHead";',
      '            head.transform.SetParent(anchor.transform, false);',
      '            head.transform.localPosition = new Vector3(0f, 0f, 0.20f);',
      '            head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);',
      '            head.transform.localScale = new Vector3(0.115f, 0.055f, 0.115f);',
      '            var hmr = head.GetComponent<MeshRenderer>();',
      '            if (hmr != null) hmr.sharedMaterial = SceneMaterials.Lit(new Color(0.16f, 0.17f, 0.195f), 0.35f);',
      '            var hcol = head.GetComponent<Collider>(); if (hcol != null) Object.Destroy(hcol);',
      '',
      '            // 镜片：**自发光**（手电筒"亮着"这件事必须一眼看出来，这是该道具的全部信息量）',
      '            var lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);',
      '            lens.name = "FlashlightLens";',
      '            lens.transform.SetParent(anchor.transform, false);',
      '            lens.transform.localPosition = new Vector3(0f, 0f, 0.255f);',
      '            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);',
      '            lens.transform.localScale = new Vector3(0.10f, 0.012f, 0.10f);',
      '            var lmr = lens.GetComponent<MeshRenderer>();',
      '            if (lmr != null) lmr.sharedMaterial = SceneMaterials.Emissive(new Color(0.92f, 0.95f, 1.0f), 3.2f);',
      '            var lcol = lens.GetComponent<Collider>(); if (lcol != null) Object.Destroy(lcol);',
      '            int built = 1;',
      '            if (built == 0)',
      '            {',
      '                var fb = GameObject.CreatePrimitive(PrimitiveType.Capsule);',
      '                fb.name = "FlashlightFallback";',
      '                fb.transform.SetParent(anchor.transform, false);',
      '                fb.transform.localScale = new Vector3(0.09f, 0.21f, 0.09f);',
      '            }',
    ].join('\n');
    s = s.replace(oldFl[0], newFl);
    log.push('  ✓ 手电筒改自建几何');
  } else log.push('  ! 手电筒段落未匹配');

  // 鬼：优先用 ghostbody
  const oldGhost = '            GameObject body = null;\n            try { body = ModelLibrary.InstantiateWhole("ghost", go.transform); }';
  if (s.includes(oldGhost)) {
    s = s.replace(oldGhost,
      '            // 优先用**新的整块人形模型**（ghostbody：男女 × 4 体型，元球生成的完整人形）。\n' +
      '            // 旧的按部件拼装模型（"ghost"）在暗场里就是一团黑，不作为首选。\n' +
      '            GameObject body = null;\n' +
      '            string picked = GhostModelPool.Pick(_rng ^ 0x51ED2701u, GhostSpawned);\n' +
      '            if (!string.IsNullOrEmpty(picked))\n' +
      '            {\n' +
      '                try { body = ModelLibrary.InstantiateSingleFile("Models/ghostbody/" + picked, go.transform, picked); }\n' +
      '                catch (System.Exception e) { Debug.LogWarning("[Whisper] ghostbody 加载失败：" + e.Message); }\n' +
      '            }\n' +
      '            if (body == null)\n' +
      '            {\n' +
      '                try { body = ModelLibrary.InstantiateWhole("ghost", go.transform); }\n' +
      '                catch (System.Exception e) { Debug.LogWarning("[Whisper] 旧鬼模型也失败：" + e.Message); }\n' +
      '            }');
    log.push('  ✓ 鬼改用 ghostbody');
  } else log.push('  ! 鬼加载锚点未中');

  fs.writeFileSync(P, s, 'utf8');
}

console.log(log.join('\n'));
