// 把渲染质量档位与后处理接进产品（用户永久约束 §2/§6）。
// **本文件不得出现反引号**（会截断 JS 模板）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const log = [];

// ── GameBootstrap：档位字段 + 施加 + 挂 PostFx ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
  let s = fs.readFileSync(P, 'utf8');

  if (!s.includes('RenderQuality _quality')) {
    s = s.replace('        ObjectiveSystem _objectives;',
`        ObjectiveSystem _objectives;
        /// <summary>渲染质量档位（低/高/顶级 + 60/90/120 帧）。</summary>
        Whisper.Gameplay.Render.RenderQuality _quality;
        /// <summary>后处理执行器（挂在主相机上）。</summary>
        PostFx _postFx;`);
    s = s.replace('        /// <summary>局内任务（合同日志）。</summary>',
`        /// <summary>渲染质量档位（主界面可切）。</summary>
        public Whisper.Gameplay.Render.RenderQuality Quality => _quality;
        /// <summary>后处理（HUD 取证用）。</summary>
        public PostFx PostFx => _postFx;
        /// <summary>局内任务（合同日志）。`);
    log.push('  ✓ 字段/属性');
  }

  if (!s.includes('InitRenderQuality')) {
    s = s.replace('            _objectives = new ObjectiveSystem(cfg);',
`            InitRenderQuality(cfg);
            _objectives = new ObjectiveSystem(cfg);`);
    s = s.replace('        /// <summary>把电力系统接到灯光上：总闸与房间开关的事件 → LightRig 的平滑开关。</summary>',
`        /// <summary>
        /// 初始化渲染质量与后处理（用户永久约束 §2/§6）。
        /// </summary>
        /// <remarks>
        /// 三条关键点，每条都是"不这么做就静默失效"：
        /// ① **vSyncCount 必须为 0** —— 移动端 vSync 会覆盖 targetFrameRate，
        ///    不关就会出现"设了 120 却锁在 60"（而且不报错）。
        /// ② **后处理必须挂在同一台相机上**，且 allowHDR 打开（辉光需要 >1 的亮部余量）。
        /// ③ **帧率与画质是两条独立的轴**（用户并列提出）：换画质不该动帧率，反之亦然。
        /// </remarks>
        void InitRenderQuality(Whisper.Gameplay.Config.GameConfigReader cfg)
        {
            _quality = new Whisper.Gameplay.Render.RenderQuality(cfg);
            foreach (var p in _quality.ConfigProblems) Debug.LogWarning("[Whisper] 画质配置：" + p);

            int fps = cfg.Int("render.defaultFrameRate", 60);
            _quality.SelectFrameRate(fps);
            string key = cfg.String("render.defaultTier", "high");
            _quality.Select(key == "low" ? Whisper.Gameplay.Render.QualityTier.Low
                        : key == "top" ? Whisper.Gameplay.Render.QualityTier.Top
                        : Whisper.Gameplay.Render.QualityTier.High);

            if (_camera != null)
            {
                _postFx = _camera.gameObject.GetComponent<PostFx>();
                if (_postFx == null) _postFx = _camera.gameObject.AddComponent<PostFx>();
                _postFx.Quality = _quality;
                _camera.allowHDR = true;   // 辉光要有 >1 的亮部余量，否则高光被截断成死白
            }
            ApplyRenderQuality();
        }

        /// <summary>把当前档位**真的**施加到 Unity 的全局设置上。</summary>
        public void ApplyRenderQuality()
        {
            if (_quality == null) return;
            var t = _quality.CurrentTier;

            Application.targetFrameRate = (int)_quality.FrameRate;
            // vSyncCount=0 是 targetFrameRate 生效的前提（见 InitRenderQuality 的注释①）
            QualitySettings.vSyncCount = 0;

            QualitySettings.pixelLightCount = t.PixelLightCount;
            QualitySettings.shadows = t.Shadows <= 0 ? ShadowQuality.Disable
                                    : t.Shadows == 1 ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.shadowResolution = t.ShadowResolution <= 0 ? ShadowResolution.Low
                                             : t.ShadowResolution == 1 ? ShadowResolution.Medium
                                             : t.ShadowResolution == 2 ? ShadowResolution.High
                                             : ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = t.ShadowDistanceM;
            QualitySettings.antiAliasing = t.AntiAliasing;
            QualitySettings.anisotropicFiltering = t.AnisotropicFiltering <= 0 ? AnisotropicFiltering.Disable
                                                 : t.AnisotropicFiltering == 1 ? AnisotropicFiltering.Enable
                                                 : AnisotropicFiltering.ForceEnable;
            if (_camera != null)
            {
                _camera.farClipPlane = t.FarClipM;
                _camera.allowHDR = true;
            }
            Debug.Log("[Whisper] 画质已施加：" + _quality.Describe());
        }

        /// <summary>把电力系统接到灯光上：总闸与房间开关的事件 → LightRig 的平滑开关。</summary>`);
    log.push('  ✓ InitRenderQuality + ApplyRenderQuality');
  }

  fs.writeFileSync(P, s, 'utf8');
}

// ── MenuScene：加「画质」按钮 ──
{
  const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
  let s = fs.readFileSync(P, 'utf8');

  const oldItems = '            string[] items = { "开始调查（单人）", "商店", "每日任务", "本局任务", "创建房间（多人）", "加入房间（IPv6 直连）", "退出" };';
  const newItems = '            string[] items = { "开始调查（单人）", "商店", "每日任务", "本局任务", "画质", "创建房间（多人）", "加入房间（IPv6 直连）", "退出" };';
  if (s.includes(oldItems)) { s = s.replace(oldItems, newItems); log.push('  ✓ 加画质按钮'); }

  s = s.replace('                case 4: // 建房间',
                '                case 4: CycleQuality(); break;\n                case 5: // 建房间');
  s = s.replace('                case 5: // 加入', '                case 6: // 加入');
  s = s.replace('                case 6:\n                    Note("退出：真机上请用系统返回键"); break;',
                '                case 7:\n                    Note("退出：真机上请用系统返回键"); break;');

  if (!s.includes('void CycleQuality')) {
    s = s.replace('        /// <summary>局内任务面板：合同日志里的可选目标（与「每日任务」是两套系统）。</summary>',
`        /// <summary>
        /// 切画质档，再点一次切帧率档（低→高→顶级→低；60→90→120→60）。
        /// </summary>
        /// <remarks>
        /// 为什么做成"一个按钮两件事"而不是两个按钮：移动端竖排按钮已经 8 个，
        /// 再加会挤到屏幕外。所以约定：**偶数次点击切画质、奇数次点击切帧率**，
        /// 面板上把当前值写清楚（玩家看得到自己在切什么）。
        /// </remarks>
        void CycleQuality()
        {
            if (_boot == null || _boot.Quality == null) { Note("画质档不可用（组合根未就绪）"); return; }
            _qualityToggleCount++;
            if (_qualityToggleCount % 2 == 1) _boot.Quality.Select(_boot.Quality.NextTier());
            else _boot.Quality.SelectFrameRate(_boot.Quality.NextFrameRate());
            _boot.ApplyRenderQuality();
            Note("渲染设置\\n\\n" + _boot.Quality.Describe()
                + "\\n\\n（再点一次切换帧率档）\\n"
                + (_boot.PostFx != null ? _boot.PostFx.Describe() : "后处理：未挂载")
                + "\\n\\n点任意位置关闭");
        }

        /// <summary>局内任务面板：合同日志里的可选目标（与「每日任务」是两套系统）。</summary>`);
    s = s.replace('        GameObject _modalBg;', '        GameObject _modalBg;\n        /// <summary>画质按钮被点的累计次数（偶数切画质、奇数切帧率）。</summary>\n        int _qualityToggleCount;');
    log.push('  ✓ CycleQuality');
  }

  fs.writeFileSync(P, s, 'utf8');
}

console.log(log.join('\n'));
