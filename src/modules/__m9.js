/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m9 → m9.js
 * 职责：输入层：键盘 + 触屏 + 桌面鼠标。 首版有一个致命缺陷（用户报告"能转视角但无法移动"）： `consume()` 每帧调用 `recomputeKeys()`，而它**无条件用键盘状态覆盖** forward/strafe。 触屏刚在 pointermove 里设好的移动值，下一帧就被清成 0 —— 于是永远无法移动。 正确做法：**键盘与触屏各存一份状态**，输出时按绝对值取大者合并，互不覆盖。 触屏布局：左半屏拖动 = 移动轴；右半屏拖动 = 视角。 桌面：WASD/方向键移动 + 鼠标拖动转视角；点击画面锁定指针后可直接推鼠标转视角。
 * 来源：baseline/game-0.6.0.js 第 2427~2542 行（4886 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：无｜导出：createInput
 */
'use strict';

    /**
     * 输入层：键盘 + 触屏 + 桌面鼠标。
     *
     * 首版有一个致命缺陷（用户报告"能转视角但无法移动"）：
     * `consume()` 每帧调用 `recomputeKeys()`，而它**无条件用键盘状态覆盖** forward/strafe。
     * 触屏刚在 pointermove 里设好的移动值，下一帧就被清成 0 —— 于是永远无法移动。
     * 正确做法：**键盘与触屏各存一份状态**，输出时按绝对值取大者合并，互不覆盖。
     *
     * 触屏布局：左半屏拖动 = 移动轴；右半屏拖动 = 视角。
     * 桌面：WASD/方向键移动 + 鼠标拖动转视角；点击画面锁定指针后可直接推鼠标转视角。
     */
    function createInput(canvas, overlay) {
      const keys = { forward: 0, strafe: 0, run: false, crouch: false };
      const touchAxes = { forward: 0, strafe: 0, run: false, crouch: false };
      const look = { dx: 0, dy: 0 };
      const pointers = new Map();
      let wantPointerLock = false;
    
      const pick = (a, b) => (Math.abs(a) >= Math.abs(b) ? a : b);
      const merge = () => ({
        forward: pick(keys.forward, touchAxes.forward),
        strafe: pick(keys.strafe, touchAxes.strafe),
        run: keys.run || touchAxes.run,
        crouch: keys.crouch,
      });
    
      // ── 键盘 ──────────────────────────────────────────────
      const onKey = (e, down) => {
        const k = (e.key ?? '').toLowerCase();
        switch (k) {
          case 'w': case 'arrowup': e.preventDefault(); keys.forward = down ? 1 : 0; break;
          case 's': case 'arrowdown': e.preventDefault(); keys.forward = down ? -1 : 0; break;
          case 'd': case 'arrowright': e.preventDefault(); keys.strafe = down ? 1 : 0; break;
          case 'a': case 'arrowleft': e.preventDefault(); keys.strafe = down ? -1 : 0; break;
          case 'shift': keys.run = down; break;
          case 'c': case 'control': keys.crouch = down; break;
          default: break;
        }
      };
      window.addEventListener('keydown', (e) => onKey(e, true));
      window.addEventListener('keyup', (e) => onKey(e, false));
      window.addEventListener('blur', () => { keys.forward = 0; keys.strafe = 0; keys.run = false; keys.crouch = false; });
    
      // ── 指针（触屏 + 鼠标）─────────────────────────────────
      // 判定"是不是触摸"不能只看 pointerType：部分 WebView 的合成事件 pointerType 为空，
      // 那会把触摸当鼠标处理 → 玩家无法移动（正是用户报的故障）。设备支持触摸就按触摸分区。
      const deviceHasTouch = typeof window !== 'undefined' && ('ontouchstart' in window || (navigator.maxTouchPoints ?? 0) > 0);
      const isTouchEvent = (e) => e.pointerType === 'touch' || e.pointerType === 'pen' || (!e.pointerType && deviceHasTouch);
    
      canvas.addEventListener('pointerdown', (e) => {
        try { canvas.setPointerCapture(e.pointerId); } catch { /* 部分 WebView 不支持，忽略 */ }
        const touch = isTouchEvent(e);
        pointers.set(e.pointerId, {
          x0: e.clientX, y0: e.clientY, x: e.clientX, y: e.clientY,
          // 触屏按屏幕中线分区；鼠标一律当视角（移动靠键盘）
          side: touch && e.clientX < window.innerWidth / 2 ? 'move' : 'look',
        });
        if (!touch) wantPointerLock = true;
      });
    
      canvas.addEventListener('pointermove', (e) => {
        const p = pointers.get(e.pointerId);
        if (!p) return;
        // 指针锁定状态下用 movementX/Y（鼠标持续推着转视角）
        const locked = document.pointerLockElement === canvas;
        const dx = locked ? (e.movementX ?? 0) : e.clientX - p.x;
        const dy = locked ? (e.movementY ?? 0) : e.clientY - p.y;
    
        if (p.side === 'move') {
          const R = 70; // 触屏摇杆半径（px）
          const ox = e.clientX - p.x0;
          const oy = e.clientY - p.y0;
          touchAxes.strafe = Math.max(-1, Math.min(1, ox / R));
          touchAxes.forward = Math.max(-1, Math.min(1, -oy / R));
          touchAxes.run = Math.hypot(ox, oy) > R * 1.35;
        } else {
          look.dx += dx;
          look.dy += dy;
        }
        p.x = e.clientX;
        p.y = e.clientY;
      });
    
      const endPointer = (e) => {
        const p = pointers.get(e.pointerId);
        if (p && p.side === 'move') { touchAxes.forward = 0; touchAxes.strafe = 0; touchAxes.run = false; }
        pointers.delete(e.pointerId);
      };
      canvas.addEventListener('pointerup', endPointer);
      canvas.addEventListener('pointercancel', endPointer);
      canvas.addEventListener('pointerleave', endPointer);
      canvas.addEventListener('contextmenu', (e) => e.preventDefault());
    
      canvas.addEventListener('click', () => {
        if (wantPointerLock && typeof canvas.requestPointerLock === 'function' && document.pointerLockElement !== canvas) {
          const r = canvas.requestPointerLock();
          if (r && typeof r.catch === 'function') r.catch(() => {});
        }
      });
    
      /** 每帧取一次并清空累积视角位移 */
      function consume() {
        const axes = merge();
        const out = { ...axes, look: { dx: look.dx, dy: look.dy } };
        look.dx = 0;
        look.dy = 0;
        return out;
      }
    
      return { consume, get hasTouch() { return 'ontouchstart' in window; }, debug: { keys, touchAxes } };
    }
    
    module.exports = { createInput };
