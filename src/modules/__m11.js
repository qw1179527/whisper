/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m11 → m11.js
 * 职责：HUD（V9 §11 设计系统）：全部代码构建，零编辑器（§19.1 C2），Token 直接落为样式。 信息层级：Layer0 致命（怪物仇恨闪烁）/ Layer1 战术（理智环·轮盘·声纹）/ Layer2 状态（电量·证据）
 * 来源：baseline/game-0.6.0.js 第 2614~2834 行（13347 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：无｜导出：createHud
 */
'use strict';

    /**
     * HUD（V9 §11 设计系统）：全部代码构建，零编辑器（§19.1 C2），Token 直接落为样式。
     * 信息层级：Layer0 致命（怪物仇恨闪烁）/ Layer1 战术（理智环·轮盘·声纹）/ Layer2 状态（电量·证据）
     */
    function createHud(config, tokens) {
      const C = tokens.color;
      const el = document.getElementById('hud');
      const css = (o) => Object.entries(o).map(([k, v]) => k + ':' + v).join(';');
    
      el.insertAdjacentHTML('beforeend', `
        <div id="top" style="${css({ position: 'absolute', top: '0', left: '0', right: '0', display: 'flex', gap: '10px', padding: '10px 12px', alignItems: 'flex-start', pointerEvents: 'none' })}">
          <div id="sanity" style="${css({ background: 'rgba(8,8,8,0.55)', border: '1px solid ' + C.hudDim, padding: '8px 10px', minWidth: '150px' })}">
            <div style="${css({ color: C.hudDim, fontSize: '10px', letterSpacing: '0.18em' })}">理智 SANITY</div>
            <div id="sanityBar" style="${css({ height: '8px', background: '#201d1a', margin: '6px 0 3px' })}"><div id="sanityFill" style="${css({ height: '100%', width: '100%', background: C.mold })}"></div></div>
            <div id="sanityText" style="${css({ color: C.paper, fontSize: '11px' })}">镇定 100</div>
          </div>
          <div id="status" style="${css({ background: 'rgba(8,8,8,0.55)', border: '1px solid ' + C.hudDim, padding: '8px 10px', color: C.paper, fontSize: '11px', lineHeight: '1.6' })}">
            <div>手电 <span id="battery">120</span>s</div>
            <div>证据 <span id="evidence">0</span>/<span id="evidenceTotal">5</span></div>
            <div>用时 <span id="clock">0:00</span></div>
          </div>
          <div style="flex:1"></div>
          <div id="mic" style="${css({ background: 'rgba(8,8,8,0.55)', border: '1px solid ' + C.hudDim, padding: '8px 10px', minWidth: '186px', color: C.paper, fontSize: '11px' })}">
            <div style="${css({ color: C.hudDim, fontSize: '10px', letterSpacing: '0.18em' })}">声纹 VOICEPRINT</div>
            <div id="micDb" style="${css({ fontFamily: 'monospace', fontSize: '12px' })}">-- dBFS</div>
            <div id="micBand" style="${css({ fontWeight: '700', letterSpacing: '0.08em' })}">未校准</div>
            <div id="micBar" style="${css({ height: '6px', background: '#201d1a', marginTop: '5px', position: 'relative' })}"><div id="micFill" style="${css({ height: '100%', width: '0%', background: C.signal })}"></div></div>
          </div>
        </div>
    
        <div id="log" style="${css({ position: 'absolute', left: '12px', bottom: '96px', width: '300px', color: C.paper, fontSize: '11px', lineHeight: '1.5', textShadow: '0 0 6px #000', pointerEvents: 'none' })}"></div>
    
        <div id="minimapWrap" style="${css({ position: 'absolute', right: '10px', bottom: '10px', border: '1px solid ' + C.hudDim, background: 'rgba(8,8,8,0.6)' })}">
          <canvas id="minimap" width="240" height="240" style="display:block"></canvas>
        </div>
    
        <div id="hint" style="${css({ position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%,-50%)', color: C.blood, fontSize: '30px', fontWeight: '700', letterSpacing: '0.2em', opacity: '0', transition: 'opacity 160ms', textShadow: '0 0 20px #000', pointerEvents: 'none' })}">它听见你了</div>
    
        <div id="crosshair" style="${css({ position: 'absolute', top: '50%', left: '50%', width: '3px', height: '3px', margin: '-1.5px 0 0 -1.5px', background: C.hudDim, pointerEvents: 'none' })}"></div>
    
        <div id="calib" style="${css({ position: 'absolute', inset: '0', background: 'rgba(6,6,7,0.86)', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', color: C.paper, textAlign: 'center', padding: '24px' })}">
          <div style="${css({ fontSize: '11px', letterSpacing: '0.24em', color: C.hudDim })}">第 0 局 · 声纹校准</div>
          <div id="calibTitle" style="${css({ fontSize: '26px', margin: '14px 0 6px', fontWeight: '700' })}">保持安静</div>
          <div id="calibDesc" style="${css({ fontSize: '13px', color: C.bone, maxWidth: '420px', lineHeight: '1.7' })}">正在采集环境底噪——这一步决定"多小声才算耳语"。</div>
          <div id="calibBar" style="${css({ width: '320px', height: '6px', background: '#201d1a', marginTop: '18px' })}"><div id="calibFill" style="${css({ height: '100%', width: '0%', background: C.signal })}"></div></div>
          <div id="calibLive" style="${css({ fontFamily: 'monospace', fontSize: '12px', marginTop: '10px', color: C.hudDim })}">-- dBFS</div>
          <div id="calibAnchors" style="${css({ marginTop: '16px', fontSize: '11px', color: C.bone, fontFamily: 'monospace' })}"></div>
          <button id="calibSkip" style="${css({ marginTop: '20px', background: 'transparent', color: C.hudDim, border: '1px solid ' + C.hudDim, padding: '8px 14px', fontSize: '12px', letterSpacing: '0.1em' })}">跳过校准（用无麦人格包固定强度）</button>
        </div>
    
        <div id="end" style="${css({ position: 'absolute', inset: '0', background: 'rgba(6,6,7,0.9)', display: 'none', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', color: C.paper, textAlign: 'center', padding: '24px' })}">
          <div id="endTitle" style="${css({ fontSize: '30px', fontWeight: '700', letterSpacing: '0.16em' })}"></div>
          <div id="endBody" style="${css({ marginTop: '14px', fontSize: '13px', lineHeight: '1.8', color: C.bone, maxWidth: '460px' })}"></div>
          <button id="again" style="${css({ marginTop: '22px', background: 'transparent', color: C.paper, border: '1px solid ' + C.hudDim, padding: '10px 18px', fontSize: '13px', letterSpacing: '0.1em' })}">再来一局</button>
        </div>
      `);
    
      const missing = [];
      const $ = (id) => {
        const el = document.getElementById(id);
        if (!el) missing.push(id);
        return el;
      };
      const logLines = [];
      const sanityBands = config.sanity.bands;
    
      $('calibSkip')?.addEventListener('click', () => window.dispatchEvent(new Event('whisper:skip-calibration')));
      $('again')?.addEventListener('click', () => location.reload());
      if ($('evidenceTotal')) $('evidenceTotal').textContent = String(config.evidenceTotal ?? 5);
    
      const phases = {
        ambient: ['保持安静', '正在采集环境底噪——这一步决定"多小声才算耳语"。'],
        whisper: ['用耳语说一句话', '像在队友耳边说悄悄话那样，持续到进度条走完。'],
        normal: ['用平常音量说话', '正常聊天音量，不要刻意压低也不要喊。'],
        shout: ['喊一声', '放开音量喊——受惊尖叫同样计费，所以要先量出你的上限。'],
        done: ['校准完成', ''],
      };
    
      function setPhase(phase, calibrator) {
        if (!calibrator && !$('calib')) return;
        const box = $('calib');
        if (phase === 'done') { box.style.display = 'none'; return; }
        box.style.display = 'flex';
        const [title, desc] = phases[phase] ?? ['', ''];
        $('calibTitle').textContent = title;
        $('calibDesc').textContent = desc;
        const a = calibrator?.anchors;
        $('calibAnchors').textContent = a
          ? '耳语 ' + a.whisper.toFixed(1) + ' / 正常 ' + a.normal.toFixed(1) + ' / 喊叫 ' + a.shout.toFixed(1) + ' dBFS　底噪 ' + (a.ambientDb ?? 0).toFixed(1)
          : '';
      }
    
      function setCalibProgress(frac) {
        $('calibFill').style.width = Math.round(Math.max(0, Math.min(1, frac)) * 100) + '%';
      }
    
      function flashHeard(id, det) {
        const names = { stitcher: '缝匠', whisperer: '低语者', coroner: '收殓人' };
        log('【' + names[id] + ' 听见了】距离 ' + det.distanceM.toFixed(1) + 'm（阈值 ' + det.effectiveThreshold.toFixed(1) + '）');
        const hint = $('hint');
        hint.textContent = names[id] + '听见你了';
        hint.style.opacity = '1';
        clearTimeout(hint._t);
        hint._t = setTimeout(() => { hint.style.opacity = '0'; }, 1200);
      }
    
      function log(msg) {
        logLines.push(msg);
        if (logLines.length > 6) logLines.shift();
        $('log').innerHTML = logLines.map((l) => '<div>' + l.replace(/</g, '&lt;') + '</div>').join('');
      }
    
      const mm = $('minimap');
      const mmCtx = mm.getContext('2d');
      const CANVAS_STATE = { level: null, state: null, monsters: null };
      const T = tokens.color;
    
      function drawMinimap() {
        const { level, state, monsters } = CANVAS_STATE;
        if (!level) return;
        const S = mm.width / (level.gridSize.w + 2);
        mmCtx.clearRect(0, 0, mm.width, mm.height);
        mmCtx.fillStyle = 'rgba(10,10,11,0.85)';
        mmCtx.fillRect(0, 0, mm.width, mm.height);
        for (let z = 0; z < level.gridSize.h; z++) {
          for (let x = 0; x < level.gridSize.w; x++) {
            const ch = level.grid[z][x];
            if (ch === '#') continue;
            mmCtx.fillStyle = ch === '+' ? T.signal : ch === 'L' ? T.blood : 'rgba(216,207,187,0.20)';
            mmCtx.fillRect((x + 1) * S, (z + 1) * S, S, S);
          }
        }
        mmCtx.fillStyle = T.rust;
        for (const r of level.rooms) {
          mmCtx.strokeStyle = 'rgba(216,207,187,0.16)';
          mmCtx.strokeRect((r.rect.x0 + 1) * S, (r.rect.z0 + 1) * S, (r.rect.x1 - r.rect.x0) * S, (r.rect.z1 - r.rect.z0) * S);
        }
        for (const e of level.evidencePoints) {
          mmCtx.fillStyle = state.collected?.has(e.id) ? 'rgba(240,230,210,0.35)' : T.paper;
          mmCtx.fillRect((e.pos.x + 1) * S - 2, (e.pos.z + 1) * S - 2, 4, 4);
        }
        for (const ep of level.extractionPoints) {
          mmCtx.fillStyle = ep.safe ? T.mold : T.blood;
          mmCtx.fillRect((ep.pos.x + 1) * S - 3, (ep.pos.z + 1) * S - 3, 6, 6);
        }
        for (const id of Object.keys(monsters ?? {})) {
          const p = monsters[id].brain.position;
          mmCtx.fillStyle = monsters[id].brain.state === 'chase' ? T.danger : 'rgba(139,30,30,0.65)';
          mmCtx.beginPath();
          mmCtx.arc((p.x + 1) * S, (p.z + 1) * S, 3.5, 0, Math.PI * 2);
          mmCtx.fill();
        }
        const px = (state.pos.x + 1) * S, pz = (state.pos.z + 1) * S;
        mmCtx.fillStyle = T.signal;
        mmCtx.beginPath();
        mmCtx.arc(px, pz, 3, 0, Math.PI * 2);
        mmCtx.fill();
        mmCtx.strokeStyle = T.signal;
        mmCtx.beginPath();
        mmCtx.moveTo(px, pz);
        mmCtx.lineTo(px + Math.sin(state.yaw) * 12, pz - Math.cos(state.yaw) * 12);
        mmCtx.stroke();
      }
    
      function update(payload) {
        if (!$('sanityFill')) return;
        const { state, level, voiceInfo, monsterStates, calibration, classifier } = payload;
        CANVAS_STATE.level = level;
        CANVAS_STATE.state = state;
        CANVAS_STATE.monsters = monsterStates;
    
        const pct = Math.max(0, Math.min(100, state.sanity));
        $('sanityFill').style.width = pct + '%';
        const band = sanityBands.find((b) => pct >= b.min && pct <= b.max) ?? sanityBands[0];
        $('sanityFill').style.background = band.id === 'composed' ? T.mold : band.id === 'uneasy' ? T.signal : band.id === 'fear' ? T.rust : T.blood;
        $('sanityText').textContent = band.label + ' ' + Math.round(pct);
    
        $('battery').textContent = Math.round(state.battery);
        $('evidence').textContent = String(state.evidence);
        const m = Math.floor(state.elapsed / 60), s = Math.floor(state.elapsed % 60);
        $('clock').textContent = m + ':' + String(s).padStart(2, '0');
    
        const db = state.debug.voice?.levelDb ?? state.debug.lastDb ?? -100;
        $('micDb').textContent = (db > -99 ? db.toFixed(1) : '--') + ' dBFS';
        const bandNames = { whisper: '耳语', normal: '正常说话', shout: '喊叫', indistinguishable: '不可辨（低于噪声底+6dB）', null: '静默' };
        if (calibration.phase !== 'done') {
          $('micBand').textContent = '校准中…';
          setCalibProgress(calibration.framesInPhase / (calibration.phase === 'ambient' ? 30 : calibration.phaseFrames));
          $('calibLive').textContent = (db > -99 ? db.toFixed(1) : '--') + ' dBFS';
        } else {
          const cls = classifier;
          $('micBand').textContent = cls?.anchors ? (bandNames[voiceInfo?.band ?? null] ?? '—') : '未校准（无麦模式：固定强度 ' + config.personaPacks.recruit.fixedIntensity + '）';
          const rel = voiceInfo?.relPeakNorm;
          $('micFill').style.width = rel == null ? '0%' : Math.round(Math.max(0, Math.min(1, (rel + 1.2) / 2.4)) * 100) + '%';
        }
    
        drawMinimap();
      }
    
      function endScreen(state, level) {
        const box = $('end');
        if (!box) { console.warn('HUD 缺少 #end 元素'); return; }
        box.style.display = 'flex';
        if (state.extracted) {
          $('endTitle').textContent = '撤离成功';
          $('endTitle').style.color = T.mold;
          $('endBody').innerHTML = '撤离点：' + state.extracted.label + '（×' + state.extracted.rewardScale + '）<br>证据 ' + state.evidence + '/' + level.evidencePoints.length + '　用时 ' + state.elapsed.toFixed(1) + 's<br>残响碎片 ' + Math.round(state.evidence * 200 + 150 + (state.elapsed < 600 ? 100 : 0)) ;
        } else if (state.caught || state.sanity <= 0) {
          $('endTitle').textContent = state.caught ? '被抓住了' : '理智崩溃';
          $('endTitle').style.color = T.blood;
          $('endBody').innerHTML = '证据 ' + state.evidence + '/' + level.evidencePoints.length + '　用时 ' + state.elapsed.toFixed(1) + 's<br>死亡归因：最后一次声纹事件由 HUD 日志给出——被听见就是被抓住的原因。';
        }
      }
    
      return { setPhase, update, log, flashHeard, endScreen, setCalibProgress };
    }
    
    module.exports = { createHud };
