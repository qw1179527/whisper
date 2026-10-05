// 把相机/后处理诊断写进**常驻 HUD 行**（Debug.Log 在这个构建里不可靠，实测抓不到）。
// 并把「画质」按钮的循环加一档「后处理关」——用于二分定位"黑屏是后处理造成的还是别的"。
// **本文件不得出现反引号**。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const log = [];

// ── ① MenuScene：HUD 常驻行加诊断 ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
  let s = fs.readFileSync(P, 'utf8');

  if (!s.includes('_camInfo = cam')) {
    // 上一轮插过 RefreshCamInfo，确保它在
    log.push('  · RefreshCamInfo 已存在');
  }

  // 把 _camInfo 挂到常驻 HUD 行（每 0.5s 刷新的那行）
  const oldHint = '_hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 模型 {2} · 触摸 {3} · 输入 [{4}]\\n按钮 {5}",\n                    GhostCount, GhostSpawned, _ghostModelInfo, _lastTouch, _inputState, _btnRects);';
  if (s.includes(oldHint)) {
    s = s.replace(oldHint,
`_hint.text = string.Format("灯闪中 · 鬼 {0}/刷出 {1} · 触摸 {2} · 输入 [{3}]\\n{4}\\n{5}",
                    GhostCount, GhostSpawned, _lastTouch, _inputState, _camInfo, _btnRects);`);
    log.push('  ✓ HUD 常驻行带相机/后处理诊断');
  } else log.push('  ! HUD 锚点未中');

  // 每次刷新诊断都更新一次 _camInfo（不只是开面板时）
  s = s.replace('            _nextLobbyRefresh -= dt;\n            if (_nextLobbyRefresh <= 0f) { _nextLobbyRefresh = 0.5f; RefreshLobby(); }',
`            _nextLobbyRefresh -= dt;
            if (_nextLobbyRefresh <= 0f) { _nextLobbyRefresh = 0.5f; RefreshLobby(); RefreshCamInfo(); }`);

  // ── ② 画质循环加「后处理关」这一档 ──
  const oldCycle = `            _qualityToggleCount++;
            if (_qualityToggleCount % 2 == 1) _boot.Quality.Select(_boot.Quality.NextTier());
            else _boot.Quality.SelectFrameRate(_boot.Quality.NextFrameRate());
            _boot.ApplyRenderQuality();`;
  if (s.includes(oldCycle)) {
    s = s.replace(oldCycle,
`            // 三拍循环：① 切画质档 ② 切帧率档 ③ 切后处理开关。
            // 第三拍是**二分定位工具**：0.1.47/48 开后处理后画面变黑，
            // 有这一拍就能在真机上一秒确认"黑屏是不是后处理造成的"，
            // 而不必再出两个包去对比（每个包 ~4 分钟）。
            // 长期它也是玩家要的"画质选项"的一部分（关后处理 = 省电模式）。
            _qualityToggleCount++;
            switch (_qualityToggleCount % 3)
            {
                case 1: _boot.Quality.Select(_boot.Quality.NextTier()); break;
                case 2: _boot.Quality.SelectFrameRate(_boot.Quality.NextFrameRate()); break;
                default:
                    if (_boot.PostFx != null) _boot.PostFx.enabled = !_boot.PostFx.enabled;
                    break;
            }
            _boot.ApplyRenderQuality();`);
    log.push('  ✓ 画质循环改为三拍（含后处理开关）');
  } else log.push('  ! 画质循环锚点未中');

  fs.writeFileSync(P, s, 'utf8');
}

// ── ③ PostFx：把"是否启用"也写进 Describe ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/PostFx.cs');
  let s = fs.readFileSync(P, 'utf8');
  s = s.replace('            => Problem != null ? "后处理：✗ " + Problem\n             : $"后处理：✓ 曝光 {_exposure:0.00} · 平均亮度 {AverageLuma:0.000}"',
                '            => Problem != null ? "后处理：✗ " + Problem\n             : $"后处理：{(enabled ? "开" : "关")} 曝光 {_exposure:0.00} 亮度 {AverageLuma:0.000}"');
  fs.writeFileSync(P, s, 'utf8');
  log.push('  ✓ PostFx.Describe 带开关状态');
}

console.log(log.join('\n'));
