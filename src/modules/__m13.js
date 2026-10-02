/* 低语计划 · 灰盒源树分模块产物（tools/split-modules.mjs 生成，勿手改）
 * 模块：__m13 → m13.js
 * 职责：启动引导（服务端与 APK 资产两种形态共用）。 两条加载路径： ① 若页面已内联 `window.__WHISPER_BOOTSTRAP__`（APK 资产形态，出包时由 tools/embed-bootstrap.mjs 注入）， 直接用内联数据，**零网络请求**； ② 否则走 HTTP（本机灰盒服务形态），并显式检查状态码——404 静默失败曾导致"点击开始无反应"。 绝对路径纪律：`/src/...` 在两种形态下都能解析（WebView 的资产源根 = https://appassets.../）。
 * 来源：baseline/game-0.6.0.js 第 3434~3476 行（1377 字节，逐字节搬移）
 * 包装改写（唯一改动，可审计）：mod.exports → module.exports；__req("__mN") → require("__mN")
 * 依赖：__m12｜导出：startGame
 */
'use strict';

    /**
     * 启动引导（服务端与 APK 资产两种形态共用）。
     *
     * 两条加载路径：
     *  ① 若页面已内联 `window.__WHISPER_BOOTSTRAP__`（APK 资产形态，出包时由 tools/embed-bootstrap.mjs 注入），
     *     直接用内联数据，**零网络请求**；
     *  ② 否则走 HTTP（本机灰盒服务形态），并显式检查状态码——404 静默失败曾导致"点击开始无反应"。
     *
     * 绝对路径纪律：`/src/...` 在两种形态下都能解析（WebView 的资产源根 = https://appassets.../）。
     */
    var __ns0 = require("__m12");
    var boot = __ns0.startGame;
    
    async function getJSON(url) {
      const res = await fetch(url, { cache: 'no-store' });
      if (!res.ok) throw new Error('加载失败：' + url + ' → HTTP ' + res.status);
      return res.json();
    }
    
    async function startGame() {
      const inline = globalThis.__WHISPER_BOOTSTRAP__;
      let config;
      let tokens;
      if (inline && inline.config && inline.designTokens) {
        config = inline.config;
        tokens = inline.designTokens;
      } else {
        config = await getJSON('/api/config');
        tokens = await getJSON('/api/design-tokens');
      }
      config.levelId = inline?.levelId ?? 'asylum_v1';
    
      // 关卡同样优先用内联数据（APK 形态下不再需要 /api/levels）
      if (inline?.level) globalThis.__WHISPER_LEVEL__ = inline.level;
      if (inline?.kitsBase64) globalThis.__WHISPER_KITS_B64__ = inline.kitsBase64;
    
      return boot(config, tokens);
    }
    
    module.exports = { startGame };
