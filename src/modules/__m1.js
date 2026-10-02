/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m1 → m1.js
 * 职责：声纹判定链（V9 §6 / V8 N2 整改：个人校准 + 相对电平） 已废弃（禁止复活）：V6 §12.2 的绝对 dBFS 硬阈值（-45/-25dB）。 原因：不同机型麦克风增益 / AGC 策略 / 握持姿势差异可达 10~25dB—— 同一句话在 A 机是"耳语"，在 B 机可能是"喊叫"，核心机制公平性被摧毁。 本模块是**纯函数 + 确定性状态机**，零依赖、零 DOM：浏览器侧只负责把 AnalyserNode 的 dBFS 帧喂进来（见 web/src/audio/voice-input.js）， C# 侧（unity/）必须通过同一批一致性向量 data/vectors/voice-classification.json。
 * 来源：baseline/game-0.6.0.js 第 523~999 行（18635 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：__m0｜导出：BAND_IDS, BAND_SOURCE_KEY, Ring, SILENCE_DBFS, VoiceBandClassifier, VoiceCalibrator, __internals, anchorFromFrames, bandToStimulus, crossDeviceAgreement, median, percentile, personaFixedIntensity
 */
'use strict';

    /**
     * 声纹判定链（V9 §6 / V8 N2 整改：个人校准 + 相对电平）
     *
     * 已废弃（禁止复活）：V6 §12.2 的绝对 dBFS 硬阈值（-45/-25dB）。
     * 原因：不同机型麦克风增益 / AGC 策略 / 握持姿势差异可达 10~25dB——
     *       同一句话在 A 机是"耳语"，在 B 机可能是"喊叫"，核心机制公平性被摧毁。
     *
     * 本模块是**纯函数 + 确定性状态机**，零依赖、零 DOM：浏览器侧只负责把
     * AnalyserNode 的 dBFS 帧喂进来（见 web/src/audio/voice-input.js），
     * C# 侧（unity/）必须通过同一批一致性向量 data/vectors/voice-classification.json。
     */
    var __ns0 = require("__m0");
    var cfg = __ns0.cfg;
    
    const BAND_IDS = ['whisper', 'normal', 'shout'];
    /** 分档 → 声纹刺激源 key（强度/半径取 data/config.json 的 stimulusSources） */
    const BAND_SOURCE_KEY = {
      whisper: 'voice_whisper',
      normal: 'voice_normal',
      shout: 'voice_shout',
    };
    
    /** dBFS 下限（静音底），避免 -Infinity 参与运算 */
    const SILENCE_DBFS = -100;
    
    const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
    const round4 = (v) => Math.round(v * 1e4) / 1e4;
    
    /** 中位数 */
    function median(xs) {
      if (xs.length === 0) return SILENCE_DBFS;
      const s = [...xs].sort((a, b) => a - b);
      const m = s.length >> 1;
      return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
    }
    
    /** 线性插值百分位（p ∈ [0,1]） */
    function percentile(xs, p) {
      if (xs.length === 0) return SILENCE_DBFS;
      const s = [...xs].sort((a, b) => a - b);
      if (s.length === 1) return s[0];
      const idx = clamp(p, 0, 1) * (s.length - 1);
      const lo = Math.floor(idx);
      const hi = Math.ceil(idx);
      if (lo === hi) return s[lo];
      return s[lo] + (s[hi] - s[lo]) * (idx - lo);
    }
    
    /**
     * 锚点统计：anchorStatistic = median_of_speech_frames_p50。
     *
     * 为什么不是 max / 不是 top-decile：某档采样期间本就有大量静默帧（人耳语前会停顿、
     * 会换气），拿 max 取到的是爆音与偶发峰值；拿 top-10% 取到的是"最响的那几帧"，
     * 会把耳语锚点拉低 4~6dB，进而把正常说话误判成喊叫（首轮测试实测到的真实缺陷）。
     * 正确做法：先按锚点无关的规则裁掉静默帧（低于整段中位数 −3dB 的帧），
     * 再取剩余语音帧的中位数——它代表"这一档的稳态电平"。
     */
    function anchorFromFrames(frames) {
      if (!frames || frames.length === 0) return SILENCE_DBFS;
      const valid = frames.filter((f) => Number.isFinite(f) && f > SILENCE_DBFS);
      if (valid.length === 0) return SILENCE_DBFS;
      const med = median(valid);
      const speech = valid.filter((f) => f >= med - 3);
      return median(speech.length ? speech : valid);
    }
    
    /** 固定容量环形缓冲（零 GC 抖动，符合 §26 性能约束"每帧零分配"精神） */
    class Ring {
      constructor(capacity) {
        this.capacity = capacity;
        this.buf = new Float32Array(capacity);
        this.n = 0;
        this.head = 0;
      }
      push(v) {
        this.buf[this.head] = v;
        this.head = (this.head + 1) % this.capacity;
        if (this.n < this.capacity) this.n++;
      }
      /** 返回按时间序的浅拷贝数组（仅统计时调用，非每帧） */
      values() {
        const out = new Array(this.n);
        for (let i = 0; i < this.n; i++) {
          const idx = (this.head - this.n + i + this.capacity * 2) % this.capacity;
          out[i] = this.buf[idx];
        }
        return out;
      }
      get last() {
        return this.n === 0 ? undefined : this.buf[(this.head - 1 + this.capacity) % this.capacity];
      }
      clear() {
        this.n = 0;
        this.head = 0;
      }
    }
    
    function framesFor(ms) {
      const frameMs = cfg('voiceCalibration.frameMs', 50);
      return Math.max(1, Math.round(ms / frameMs));
    }
    
    /**
     * 个人基线校准器：三步采样（耳语 / 正常 / 喊叫）→ 该设备三档锚点。
     * 第 0 局教学内完成，持久化到本地（settings 页可重新校准）。
     */
    class VoiceCalibrator {
      constructor(opts = {}) {
        const c = cfg('voiceCalibration', {});
        this.samplesPerPrompt = opts.samplesPerPrompt ?? c.samplesPerPrompt ?? 3;
        this.sampleFrames = framesFor(opts.sampleMs ?? c.sampleMs ?? 2000);
        this.prompts = c.prompts ?? ['whisper', 'normal', 'shout'];
        this.reset();
      }
    
      /**
       * 喂入一帧环境音（第 0 局教学的第 0 步：先静默 1.5 秒采环境底噪）。
       * 环境底噪与"玩家说话多响"无关，必须在采样语音之前独立采集。
       */
      pushAmbientFrame(dbfs) {
        if (!this.ambient) this.ambient = [];
        this.ambient.push(Number.isFinite(dbfs) ? dbfs : SILENCE_DBFS);
        return { ambientFrames: this.ambient.length };
      }
    
      ambientFloor(percentilePct = 60) {
        if (!this.ambient || this.ambient.length === 0) return null;
        return round4(percentile(this.ambient, percentilePct / 100));
      }
    
      reset() {
        this.current = null;
        this.collected = new Map();
        this.ambient = [];
        this.done = false;
        this.anchors = null;
      }
    
      /** 开始某档采样：'whisper' | 'normal' | 'shout' */
      startPrompt(promptId) {
        if (!this.prompts.includes(promptId)) throw new Error(`unknown prompt: ${promptId}`);
        this.current = promptId;
        if (!this.collected.has(promptId)) this.collected.set(promptId, []);
      }
    
      /**
       * 喂入一帧 dBFS。返回 {promptId, progress, complete}
       * 未处于采样态时返回 {ignored:true}
       */
      pushFrame(dbfs) {
        if (!this.current) return { ignored: true };
        const promptId = this.current;
        const arr = this.collected.get(promptId);
        arr.push(Number.isFinite(dbfs) ? dbfs : SILENCE_DBFS);
        const progress = clamp(arr.length / this.sampleFrames, 0, 1);
        const complete = arr.length >= this.sampleFrames;
        if (complete) {
          this.current = null;
          this.done = this.prompts.every((p) => (this.collected.get(p)?.length ?? 0) > 0);
          if (this.done) this.anchors = this.buildAnchors();
        }
        return { promptId, progress: round4(progress), complete };
      }
    
      /** 显式结束当前档（提前结束也接受，取已采到的帧） */
      endPrompt() {
        this.current = null;
        this.done = this.prompts.every((p) => (this.collected.get(p)?.length ?? 0) > 0);
        if (this.done) this.anchors = this.buildAnchors();
        return this.anchors;
      }
    
      /** 三档锚点（dBFS）；含环境底噪下限与单调化修正 */
      buildAnchors() {
        const raw = {};
        for (const p of this.prompts) raw[p] = anchorFromFrames(this.collected.get(p) ?? []);
        const anomalies = [];
        if (raw.normal < raw.whisper) { anomalies.push('normal<whisper'); raw.normal = raw.whisper; }
        if (raw.shout < raw.normal) { anomalies.push('shout<normal'); raw.shout = raw.normal; }
    
        // 锚点下限纪律：任何一档都必须比环境底噪高出 minSnrDb，否则该档在噪声里不可辨
        const minSnr = cfg('voiceCalibration.minAnchorSnrDb', 6);
        const amb = this.ambientFloor();
        if (amb != null) {
          if (raw.whisper < amb + minSnr) { anomalies.push('whisper_below_ambient+snr'); raw.whisper = amb + minSnr; }
          if (raw.normal < raw.whisper) raw.normal = raw.whisper;
          if (raw.shout < raw.normal) raw.shout = raw.normal;
        }
        // 注意：这里**不做** noiseFloorMaxDb 夹取。夹取会把高增益机型的"正常说话"压到
        // 与低增益机型相同的绝对电平，从而抹掉设备差异——那正是绝对阈值方案的老毛病。
        const anchors = {
          whisper: raw.whisper,
          normal: raw.normal,
          shout: raw.shout,
          anomalies,
          ambientDb: amb ?? null,
          capturedAt: this.capturedAt ?? null,
        };
        return anchors;
      }
    
      /** 完整性校验：用于 M0 的"三步采样 UI 原型"验收 */
      validate() {
        const problems = [];
        for (const p of this.prompts) {
          const n = this.collected.get(p)?.length ?? 0;
          if (n === 0) problems.push(`${p}: 未采样`);
          else if (n < this.sampleFrames * 0.5) problems.push(`${p}: 采样不足（${n}/${this.sampleFrames}）`);
        }
        if (this.anchors) {
          const { whisper, normal, shout } = this.anchors;
          const minSep = cfg('voiceCalibration.minAnchorSeparationDb', 6);
          if (normal - whisper < minSep) problems.push('耳语与正常说话间距 ' + (normal - whisper).toFixed(1) + 'dB < ' + minSep + 'dB（太窄：轻声会被判成正常）');
          if (shout - normal < minSep) problems.push('正常与喊叫间距 ' + (shout - normal).toFixed(1) + 'dB < ' + minSep + 'dB（太窄：正常说话会被判成喊叫）');
          if (shout - whisper < minSep * 2) problems.push('三档总跨度仅 ' + (shout - whisper).toFixed(1) + 'dB，麦克风动态范围过小或未按提示发声，建议重新校准');
        } else problems.push('未生成锚点');
        return { ok: problems.length === 0, problems };
      }
    
      toJSON() {
        return {
          anchors: this.anchors,
          samples: Object.fromEntries([...this.collected].map(([k, v]) => [k, v.length])),
        };
      }
    
      static fromJSON(obj) {
        const c = new VoiceCalibrator();
        c.anchors = obj?.anchors ?? null;
        c.done = !!c.anchors;
        return c;
      }
    }
    
    /**
     * 运行时分档器：滚动噪声底 + 峰值追踪 + 滞后 → 相对电平分档。
     */
    class VoiceBandClassifier {
      constructor(opts = {}) {
        const c = cfg('voiceCalibration', {});
        this.anchors = opts.anchors ?? null;
        this.noiseFloorWindowFrames = framesFor(opts.noiseWindowMs ?? c.noiseFloorWindowMs ?? 10000);
        this.noiseFloorPercentile = (opts.noisePercentile ?? c.noiseFloorPercentile ?? 10) / 100;
        this.noiseFastWindowFrames = framesFor(opts.noiseFastWindowMs ?? c.noiseFastWindowMs ?? 2000);
        this.noiseFastPercentile = (opts.noiseFastPercentile ?? c.noiseFastPercentile ?? 5) / 100;
        this.levelWindowFrames = framesFor(opts.levelWindowMs ?? c.levelWindowMs ?? 500);
        this.levelStatistic = opts.levelStatistic ?? c.levelStatistic ?? 'p90';
        this.hysteresisDb = opts.hysteresisDb ?? c.hysteresisDb ?? 2.0;
        // 安全上限：允许超出本人三档范围 ±clampCeil 倍，超出即夹取（仅防极端值，不参与分档语义）
        this.clampCeil = Math.abs(opts.clampCeil ?? c.clampCeil ?? 1.5);
        this.peakBoost = opts.peakBoost ?? c.peakBoost ?? 1.15;
        this.noiseRing = new Ring(this.noiseFloorWindowFrames);
        this.noiseFastRing = new Ring(this.noiseFastWindowFrames);
        this.levelRing = new Ring(this.levelWindowFrames);
        this.bands = null;
        this.currentBand = null;
        if (this.anchors) this.setAnchors(this.anchors);
      }
    
      /**
       * 动态范围归一化（V8 N2 "相对电平"的严格实现）。
       *
       * 物理定义（不依赖任何配置夹取）：
       *    0 = 本人「正常说话」锚点
       *   -1 = 本人「耳语」锚点（下侧除以 normal-whisper）
       *   +1 = 本人「喊叫」锚点（上侧除以 shout-normal）
       * 于是该设备三档锚点恒为 -1/0/+1，边界恒为 -0.5/+0.5——与绝对增益完全无关。
       *
       * 首轮实测的三个反例（全部固化为回归测试）：
       * ① 绝对夹取 ±24dB → 低增益机型喊叫(+16dB)被压平，正常说话被误判为喊叫；
       * ② 沿用旧的对称默认夹取 [0.25,3] → 负下限变成 +0.25，耳语/正常全被抬进"正常"档；
       * ③ 上下两侧共用一个 range → 耳语锚点不再映射到 -1，边界失去物理含义。
       * 结论：必须分段归一化，且安全夹取必须关于 0 对称。
       */
      _normalize(relDb, anchors) {
        const up = Math.max(1, anchors.shout - anchors.normal);
        const down = Math.max(1, anchors.normal - anchors.whisper);
        const v = relDb >= 0 ? relDb / up : relDb / down;
        const ceil = this.clampCeil;
        return Math.min(ceil, Math.max(-ceil, v));
      }
    
      setAnchors(anchors) {
        if (!anchors) return;
        this.anchors = anchors;
        const up = Math.max(1, anchors.shout - anchors.normal);
        const down = Math.max(1, anchors.normal - anchors.whisper);
        this.bands = {
          whisper: { lo: -Infinity, hi: -0.5 },
          normal: { lo: -0.5, hi: 0.5 },
          shout: { lo: 0.5, hi: Infinity },
          boundaries: [-0.5, 0.5],
          rangeDb: { up: round4(up), down: round4(down) },
        };
      }
    
      /**
       * 噪声底：长窗（10s 第10百分位）与快窗（2s 第5百分位）取小。
       * 快窗是关键——玩家一开口，长窗就再也看不到静默帧；快窗保证说话中途仍能估到底噪。
       */
      noiseFloor() {
        const cap = cfg('voiceCalibration.noiseFloorMaxDb', -30);
        const longFloor = this.noiseRing.n ? percentile(this.noiseRing.values(), this.noiseFloorPercentile) : null;
        const fastFloor = this.noiseFastRing.n ? percentile(this.noiseFastRing.values(), this.noiseFastPercentile) : null;
        if (longFloor == null && fastFloor == null) return this.anchors?.ambientDb ?? SILENCE_DBFS;
        const f = Math.min(longFloor ?? Infinity, fastFloor ?? Infinity);
        return Math.min(f, cap);
      }
    
      /** 当前窗口电平：levelStatistic（默认 p90）比 max 抗爆音、比 mean 抗停顿 */
      currentLevel() {
        if (this.levelRing.n === 0) return SILENCE_DBFS;
        const vals = this.levelRing.values();
        if (this.levelStatistic === 'max') return Math.max(...vals);
        if (this.levelStatistic === 'mean') return vals.reduce((a, b) => a + b, 0) / vals.length;
        if (this.levelStatistic === 'median') return median(vals);
        const m = /^p(\d{1,2})$/.exec(this.levelStatistic);
        return m ? percentile(vals, Number(m[1]) / 100) : Math.max(...vals);
      }
    
      /**
       * 喂入一帧，返回分档结果。
       * @param {number} dbfs 当前帧声压级
       * @param {{noiseFloor?:number, anchors?:object}} [opts] 可覆盖（测试与跨机型实验用）
       */
      push(dbfs, opts = {}) {
        const v = Number.isFinite(dbfs) ? dbfs : SILENCE_DBFS;
        this.noiseRing.push(v);
        this.noiseFastRing.push(v);
        this.levelRing.push(v);
        const anchors = opts.anchors ?? this.anchors;
        if (!anchors) return { band: null, reason: 'uncalibrated', levelDb: v };
        let bands = this.bands;
        if (anchors !== this.anchors) {
          const savedA = this.anchors;
          const savedB = this.bands;
          this.setAnchors(anchors);
          bands = this.bands;
          this.anchors = savedA;
          this.bands = savedB;
        }
        return this._classify(v, anchors, bands, opts);
      }
    
      _classify(v, anchors, bands, opts = {}) {
        const nf = opts.noiseFloor ?? this.noiseFloor();
        const ref = anchors.normal;
        const relDb = v - ref;
        const norm = this._normalize(relDb, anchors);
        const floorNorm = this._normalize(nf - ref, anchors);
        // 峰值：本帧与 500ms 窗口统计的较响者，再乘一点余量（比纯窗口统计更贴合"这一声有多响"）
        const stat = opts.peak ?? this.currentLevel();
        const peak = opts.peak ?? Math.max(v, stat) * this.peakBoost;
        const relPeak = this._normalize(peak - ref, anchors);
        const snrDb = round4(relDb - (nf - ref));
        const snrPeakDb = round4(peak - nf);
        const h = this.hysteresisDb / 2 / Math.max(1, anchors.shout - anchors.normal);
    
        // 可辨下限：说话电平不比实测噪声底高出 minRuntimeSnrDb 时，不得宣称任何分档。
        // 用峰值 SNR（而非单帧 SNR）判定——单帧抖动不应让一整句话"消失"。
        const minSnr = opts.minRuntimeSnrDb ?? cfg('voiceCalibration.minRuntimeSnrDb', 6);
        if (opts.peak == null && snrPeakDb < minSnr) {
          const changed = this.currentBand !== 'indistinguishable';
          this.currentBand = null;
          return {
            band: 'indistinguishable',
            bandIndex: -1,
            reason: 'snr_below_min',
            snrDb,
            snrPeakDb,
            minSnrDb: minSnr,
            levelDb: round4(v),
            relDb: round4(relDb),
            levelNorm: round4(norm),
            noiseFloorDb: round4(nf),
            changed,
            confidence: 0,
          };
        }
    
        const [b1, b2] = bands.boundaries;
    
        let band;
        const cur = this.currentBand;
        if (cur === 'whisper') band = relPeak >= b1 + h ? (relPeak >= b2 + h ? 'shout' : 'normal') : 'whisper';
        else if (cur === 'normal') band = relPeak >= b2 + h ? 'shout' : relPeak < b1 - h ? 'whisper' : 'normal';
        else if (cur === 'shout') band = relPeak < b1 - h ? 'whisper' : relPeak < b2 - h ? 'normal' : 'shout';
        else band = relPeak < b1 ? 'whisper' : relPeak < b2 ? 'normal' : 'shout';
    
        // tie-break 纪律：落在边界上取更响的一档（宁可被听见，不可被漏判）
        if (relPeak === b1) band = 'normal';
        if (relPeak === b2) band = 'shout';
    
        const changed = band !== this.currentBand;
        this.currentBand = band;
    
        // 置信度：离最近边界的归一化距离（用于跨机型一致率与校准质量分析）
        const dist = Math.min(Math.abs(relPeak - b1), Math.abs(relPeak - b2));
        return {
          band,
          bandIndex: BAND_IDS.indexOf(band),
          levelDb: round4(v),
          relDb: round4(relDb),
          levelNorm: round4(norm),
          relPeakNorm: round4(relPeak),
          noiseFloorDb: round4(nf),
          floorNorm: round4(floorNorm),
          snrDb: round4(relDb - (nf - ref)),
          boundaryNorm: [round4(b1), round4(b2)],
          dynamicRangeDb: bands.rangeDb ?? null,
          confidence: round4(Math.min(1, dist / 0.35)),
          changed,
        };
      }
    
      reset(keepAnchors = true) {
        this.noiseRing.clear();
        this.noiseFastRing.clear();
        this.levelRing.clear();
        this.currentBand = null;
        if (!keepAnchors) {
          this.anchors = null;
          this.bands = null;
        }
      }
    }
    
    /**
     * 跨机型一致率：同一段录音在三台设备上的分档结果一致率（M0 硬指标 ≥90%）。
     * @param {Record<string, string[]>} perDevice 设备名 → 逐帧分档序列
     */
    function crossDeviceAgreement(perDevice) {
      const devices = Object.keys(perDevice);
      if (devices.length < 2) return { agreement: null, reason: 'need>=2 devices', frames: 0 };
      const n = Math.min(...devices.map((d) => perDevice[d].length));
      if (n === 0) return { agreement: 0, frames: 0 };
      let agree = 0;
      for (let i = 0; i < n; i++) {
        const first = perDevice[devices[0]][i];
        if (devices.every((d) => perDevice[d][i] === first)) agree++;
      }
      return { agreement: round4(agree / n), frames: n, devices: devices.length };
    }
    
    /** 无麦人格包固定强度（天然免疫设备差异，V9 §8） */
    function personaFixedIntensity(personaId) {
      const p = cfg(`personaPacks.${personaId}`, null);
      if (!p) throw new Error(`unknown persona: ${personaId}`);
      return p.fixedIntensity;
    }
    
    /** 分档 → StimulusEvent 强度（走配置表，不硬编码） */
    function bandToStimulus(band, opts = {}) {
      // 不可操作分档（indistinguishable / null）返回 null 而不是抛错：
      // 运行时"说话电平低于噪声底"是常态，抛错会让整帧崩溃（实测在帧 102 抛 unknown band）。
      const key = BAND_SOURCE_KEY[band];
      if (!key) return null;
      const src = cfg(`stimulusSources.${key}`);
      if (!src) return null;
      let intensity = src.intensity;
      if (opts.personaId) {
        const p = cfg(`personaPacks.${opts.personaId}`);
        if (opts.mute) intensity = p.fixedIntensity;
        else intensity = Math.round(intensity * (p.stimulusMultiplier ?? 1));
      }
      // 人耳听觉阈值近似：强度不得低于噪声底推导的可听阈值（避免"喊叫"被判成无声）
      const minAudible = opts.minAudibleIntensity ?? null;
      if (minAudible != null) intensity = Math.max(intensity, minAudible);
      return { sourceKey: key, intensity, radiusM: src.radiusM, type: src.type, globalBroadcast: !!src.globalBroadcast };
    }
    
    const __internals = { clamp, round4, framesFor };
    
    module.exports = { median, percentile, anchorFromFrames, crossDeviceAgreement, personaFixedIntensity, bandToStimulus, Ring, VoiceCalibrator, VoiceBandClassifier, BAND_IDS, BAND_SOURCE_KEY, SILENCE_DBFS, __internals };
