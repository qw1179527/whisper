/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m10 → m10.js
 * 职责：声纹采集（浏览器 WebAudio 等价验证，V8 N1 的替代路径） 为什么要写成可替换件：本机没有 Unity/Vivox，真正的采集源待 Unity 侧决定 （V8 N1 优先级 ① Vivox 本地能量回调 ② external audio input ③ 轮盘降级）。 这里用 getUserMedia + AnalyserNode 走**同一条判定链**（core/src/voiceprint.mjs）， 因此校准参数与分档语义在换实现后依然可比——这正是 V9 §19 解耦架构的用处。 关键：必须关闭浏览器的自动增益/降噪/回声消除，否则 AGC 会把"耳语"拉成"正常"， 跨机型一致率实验就失去意义（同一句悄悄话在两台手机上必须是两个绝对电平）。
 * 来源：baseline/game-0.6.0.js 第 2542~2614 行（2417 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：无｜导出：createVoiceInput
 */
'use strict';

    /**
     * 声纹采集（浏览器 WebAudio 等价验证，V8 N1 的替代路径）
     *
     * 为什么要写成可替换件：本机没有 Unity/Vivox，真正的采集源待 Unity 侧决定
     * （V8 N1 优先级 ① Vivox 本地能量回调 ② external audio input ③ 轮盘降级）。
     * 这里用 getUserMedia + AnalyserNode 走**同一条判定链**（core/src/voiceprint.mjs），
     * 因此校准参数与分档语义在换实现后依然可比——这正是 V9 §19 解耦架构的用处。
     *
     * 关键：必须关闭浏览器的自动增益/降噪/回声消除，否则 AGC 会把"耳语"拉成"正常"，
     * 跨机型一致率实验就失去意义（同一句悄悄话在两台手机上必须是两个绝对电平）。
     */
    const FRAME_MS = 50;
    
    async function createVoiceInput(config) {
      const state = { db: -100, ready: false, error: null, status: 'init' };
      let analyser = null;
      let buf = null;
      let lastAt = 0;
    
      async function init() {
        if (!navigator.mediaDevices?.getUserMedia) throw new Error('本机浏览器无 getUserMedia');
        const stream = await navigator.mediaDevices.getUserMedia({
          audio: {
            echoCancellation: false,
            noiseSuppression: false,
            autoGainControl: false,
            channelCount: 1,
          },
        });
        const ctx = new (window.AudioContext ?? window.webkitAudioContext)();
        if (ctx.state === 'suspended') await ctx.resume();
        const src = ctx.createMediaStreamSource(stream);
        analyser = ctx.createAnalyser();
        analyser.fftSize = 2048;
        analyser.smoothingTimeConstant = 0.15;
        src.connect(analyser);
        buf = new Float32Array(analyser.fftSize);
        state.ready = true;
        state.status = 'live';
        state.context = ctx;
        state.stream = stream;
        await ctx.resume();
        return state;
      }
    
      /** 帧级 dBFS：50ms 内取最新一段时域样本的 RMS，转 dBFS（满量程参考 1.0） */
      function readDb() {
        if (!analyser) return -100;
        const now = performance.now();
        if (now - lastAt < FRAME_MS * 0.5) return state.db;
        lastAt = now;
        analyser.getFloatTimeDomainData(buf);
        let sum = 0;
        for (let i = 0; i < buf.length; i++) sum += buf[i] * buf[i];
        const rms = Math.sqrt(sum / buf.length);
        state.db = rms > 1e-7 ? 20 * Math.log10(rms) : -100;
        return state.db;
      }
    
      try {
        await init();
      } catch (err) {
        state.error = String(err.message ?? err);
        state.status = 'denied';
      }
    
      return { state, readDb, frameMs: FRAME_MS };
    }
    
    module.exports = { createVoiceInput };
