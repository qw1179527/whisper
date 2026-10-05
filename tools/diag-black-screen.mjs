// 0.1.47 真机实测：开启后处理后**主界面 3D 走廊整片变黑**（只剩 UI）。
// 本补丁加两样东西，目标是"下轮出包就能定位"，而不是再猜一轮：
//   ① 逻辑层自检：CameraState（clearFlags / 位置 / 朝向）+ 亮度读回值，随 HUD 回合；
//   ② 眼部适应加**死区**：亮度读回在极暗时可能给出垃圾值 →
//      旧代码会朝"提亮"方向一路逼近上限（曝光 3.2 会是白屏，不是黑屏，但同样是错的）。
//      "画面已经很暗就不再加增益"是人眼的真实特征（暗适应不会把全黑变成白天）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const log = [];

// ── ① PostFx：死区 + 诊断字段 ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/PostFx.cs');
  let s = fs.readFileSync(P, 'utf8');

  if (!s.includes('MinAdaptLuma')) {
    s = s.replace('        /// <summary>最近一次读回的平均亮度（0..1，屏显量级）。</summary>\n        public float AverageLuma => Mathf.Pow(2f, _logLuma);',
`        /// <summary>最近一次读回的平均亮度（0..1，屏显量级）。</summary>
        public float AverageLuma => Mathf.Pow(2f, _logLuma);
        /// <summary>最近一次**原始**读回值（log2 域，未平滑）—— 诊断用。</summary>
        public float RawLogLuma { get; private set; }
        /// <summary>
        /// 亮度死区下限（log2 域）：画面比这更暗时眼部适应**不再加增益**。
        /// </summary>
        /// <remarks>
        /// 为什么必须有：0.1.47 真机把走廊整片变黑，而适应逻辑会朝"提亮"方向逼近
        /// （全黑画面 → 目标亮度远高于当前 → 曝光冲到上限）。人眼并不是这样工作的：
        /// **暗适应不会把全黑变成白天**。所以给一个死区——已经很暗就保持中性，
        /// 让"真正的黑暗"保持黑暗（这也是恐怖游戏该有的行为，不是妥协）。
        /// </remarks>
        public float MinAdaptLumaLog2 = -6.0f;`);
    log.push('  ✓ PostFx：RawLogLuma + 死区字段');
  }

  if (!s.includes('Mathf.Min(targetLog')) {
    s = s.replace('            float targetLog = Mathf.Log(Mathf.Max(KeyValue, 1e-3f), 2f) - _logLuma;',
`            RawLogLuma = v;
            // 死区：画面比 MinAdaptLumaLog2 还暗 → 适应不再继续提亮（人眼不是这么工作的）
            float effLog = Mathf.Max(_logLuma, MinAdaptLumaLog2);
            float targetLog = Mathf.Min(Mathf.Log(Mathf.Max(KeyValue, 1e-3f), 2f) - effLog, 0f);`);
    log.push('  ✓ PostFx：适应死区（targetLog 不得超过 0）');
  }
  fs.writeFileSync(P, s, 'utf8');
}

// ── ② GameBootstrap：相机自检日志 ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
  let s = fs.readFileSync(P, 'utf8');
  if (!s.includes('相机自检')) {
    s = s.replace('            Debug.Log("[Whisper] 画质已施加：" + _quality.Describe());',
`            Debug.Log("[Whisper] 画质已施加：" + _quality.Describe());
            // 相机自检：0.1.47 开后处理变黑时，"相机到底在哪、清屏模式是什么"是第一批要问的问题。
            // 这些值平时不打印（日志噪声），只在施加画质时打一次。
            if (_camera != null)
            {
                var cp = _camera.transform.position;
                var cf = _camera.transform.forward;
                Debug.Log(string.Format(
                    "[Whisper] 相机自检：pos=({0:F1},{1:F1},{2:F1}) forward=({3:F2},{4:F2},{5:F2}) clear={6} bg={7} hdr={8} depthTex={9} postFx={10}",
                    cp.x, cp.y, cp.z, cf.x, cf.y, cf.z, _camera.clearFlags, _camera.backgroundColor,
                    _camera.allowHDR, _camera.depthTextureMode, _postFx != null ? _postFx.enabled : false));
            }`);
    log.push('  ✓ GameBootstrap：相机自检');
  }
  fs.writeFileSync(P, s, 'utf8');
}

// ── ③ MenuScene：HUD 显示相机状态 + 后处理数值 ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
  let s = fs.readFileSync(P, 'utf8');
  if (!s.includes('_camInfo')) {
    s = s.replace('        GameObject _modalBg;',
`        GameObject _modalBg;
        /// <summary>相机/后处理诊断（真机黑屏排查用；release 下 Debug.Log 不可靠，只能走 HUD）。</summary>
        string _camInfo = "相机：—";`);
    s = s.replace('            _modal.text = text;',
`            // 每次开面板都刷新一次诊断（把"当前渲染到底什么状态"写在玩家看得到的地方）
            RefreshCamInfo();

            _modal.text = text;`);
    if (!s.includes('void RefreshCamInfo')) {
      s = s.replace('        /// <summary>关掉大号面板（点空白处）。</summary>',
`        /// <summary>刷新相机/后处理诊断行。</summary>
        /// <remarks>
        /// 为什么放 HUD 而不是只写日志：本工程 release 构建里 Debug.Log 时有时无（实测 0.1.24 全被剥离），
        /// 所以"画面为什么是黑的"这类问题必须能**在屏幕上**读到答案。
        /// </remarks>
        void RefreshCamInfo()
        {
            string cam = "相机：—";
            var c = _cam;
            if (c != null)
            {
                var p = c.transform.position;
                cam = string.Format("相机 ({0:F1},{1:F1},{2:F1}) clear={3} hdr={4} depth={5} fov={6:F0}",
                    p.x, p.y, p.z, c.clearFlags, c.allowHDR, c.depthTextureMode, c.fieldOfView);
            }
            string fx = _boot != null && _boot.PostFx != null
                ? _boot.PostFx.Describe() + string.Format(" raw={0:F2}", _boot.PostFx.RawLogLuma)
                : "后处理：未挂载";
            string q = _boot != null && _boot.Quality != null ? _boot.Quality.Describe() : "画质：—";
            _camInfo = cam + "\\n" + fx + "\\n" + q;
        }

        /// <summary>关掉大号面板（点空白处）。</summary>`);
    }
    log.push('  ✓ MenuScene：相机诊断 + RefreshCamInfo');
  }
  // 画质面板里带上诊断行
  s = s.replace('                + (_boot.PostFx != null ? _boot.PostFx.Describe() : "后处理：未挂载")',
                '                + _camInfo');
  fs.writeFileSync(P, s, 'utf8');
}

console.log(log.join('\n'));
