#!/usr/bin/env node
/**
 * add-official-sanity-drain.mjs — 把**官方理智流失公式**落进 `data/config.json`
 *
 * ## 为什么必须落进配置而不是写死在代码里
 * 用户永久约束：「机制数值一律以官方资料与 supplement 文档为准，不确定就标 design 并写进
 * `data/config.json` 的 `_src`」——**数值真源在 config、改数值不碰代码**（V9 §19.5）。
 *
 * ## 官方数据（出处：phasmophobia.su/knowledge-base/gameplay/sanity，2026-07-12 修订）
 * 被动流失（%/s）按地图大小 × 阶段：
 * | 地图 | Setup | 正常 |
 * |---|---|---|
 * | 小 | 0.09 | 0.12 |
 * | 中 | 0.05 | 0.08 |
 * | 大 | 0.03 | 0.05 |
 * 难度乘数：Amateur ×1 · Intermediate ×1.5 · Professional/Nightmare/Insanity ×2；
 * **Blood Moon 再 +1（即 ×2 之上再加一档）**；**单人被动流失减半**。
 * **Setup 阶段理智不会因任何来源低于 50%**（但挡不住鬼能力与诅咒道具）。
 *
 * 光照口径（**极易做错**）：
 * · 流失取决于房间**主光源**（天花板灯 + 墙上开关）→ **主灯全开时该房间被动流失 = 0**；
 * · **台灯 / 落地灯 / 电视 / 监视器 / 手电 / 设置里的亮度 都不能停止流失**；
 * · 大型暗区**即使开主灯也只降到 80%**；
 * · **火源 2 m 内按等级降低：I 约 33% · II 50% · III 66%**，但**不归零**。
 *
 * ## 幂等与兼容
 * · 新增 `sanity.drain.official` 子段（**不动**既有 `darknessPerSec` 等键 —— 已有代码与测试不受影响）；
 * · 已有键里与官方一致的（`allyDeath: -15`、`jumpscare: -10`）保持原样并在 `_src` 里注明"与官方一致"；
 * · 写完同步三份镜像（`data-mirror.mjs`），否则真机读不到。
 *
 * 用法：node tools/add-official-sanity-drain.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const CFG = path.join(ROOT, 'data/config.json');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(CFG, 'utf8');
const cfg = JSON.parse(raw);
if (cfg?.sanity?.drain?.official) { console.log('[sanity] 已存在 official 段，跳过'); process.exit(0); }
if (!cfg.sanity || !cfg.sanity.drain) { console.error('[sanity] ✗ 未找到 sanity.drain'); process.exit(1); }

cfg.sanity.drain.official = {
  _src: 'official：phasmophobia.su/knowledge-base/gameplay/sanity（2026-07-12 修订）。'
      + '数值原样抄录，未做本地化改动；用途见本段各字段注释。',
  _note: '被动流失 = 基础值(按地图大小×阶段) × 难度乘数 × 单人/血月修正；'
       + '主灯全开时该房间为 0；大暗区开主灯只降到 80%；火源按等级降低但不归零。',
  // 被动流失基础值（%/s，取正值；应用时按"扣减"处理）
  passivePerSec: {
    small: { setup: 0.09, normal: 0.12 },
    medium: { setup: 0.05, normal: 0.08 },
    large: { setup: 0.03, normal: 0.05 },
  },
  // 难度乘数（键名沿用本项目既有难度命名，未知难度落 amateur 档）
  difficultyMultiplier: {
    amateur: 1.0,
    intermediate: 1.5,
    professional: 2.0,
    nightmare: 2.0,
    insanity: 2.0,
  },
  // 血月：在难度乘数之上**再加 1**（原文：добавляет ещё ×1 к множителю drain）
  bloodMoonAdditive: 1.0,
  // 单人：被动流失减半
  soloPassiveMultiplier: 0.5,
  // Setup 阶段任何来源都不能把理智压到该值以下（鬼能力与诅咒道具除外）
  setupFloor: 50,
  // 光照口径（**布尔语义**，供 SanitySystem 调用方判断）
  light: {
    stopDrainRequires: 'mainLight',        // 只有主灯（天花板灯+墙上开关）能完全停止
    ignoreForDrain: ['deskLamp', 'floorLamp', 'tv', 'monitor', 'flashlight', 'brightnessSetting'],
    largeDarkZoneMinRatio: 0.8,            // 大暗区即使开主灯也只降到 80%
    fireRadiusM: 2.0,                      // 火源有效半径
    fireTierFactor: { 1: 0.33, 2: 0.5, 3: 0.66 },   // 按等级降低（非归零）
  },
  // 各来源扣减（与既有键的关系见 _src 说明；这里是官方原值）
  sources: {
    allyDeath: -15,          // 与既有 sanity.drain.allyDeath 一致
    ghostEventContact: -10,  // 撞上多数事件鬼影/云团
    bansheeEventOnTarget: -15,
    oniEventContact: -20,
    jinnAbility: -25,        // 附近且电闸开启
    yureiAbility: -15,       // 附近关门
    phantomPerSec: -0.5,     // 现身时心跳区内
    poltergeistPerItem: -2,  // 投掷爆炸：−2% × 物品数
    moroiCurse: 'doublesPassiveDrainAndIgnoresLightAndFire',
    tarotSun: 'to100', tarotMoon: 'to0', tarotWheel: 25,
  },
  _consistencyNotes: [
    '既有 sanity.drain.allyDeath = -15 与官方一致（无需改）',
    '既有 sanity.drain.jumpscare = -10 与官方 ghostEventContact 一致（无需改）',
    '既有 sanity.drain.darknessPerSec = -1 **不是官方值**（官方按地图大小与阶段分档，见 passivePerSec）',
    '既有 recover.sedative = 25 / extractionSafeZonePerSec = 2 **属本项目 design**，官方只说明"恢复比例随难度/等级"',
  ],
};

const bak = CFG + '.bak-san';
if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
if (!checkOnly) fs.writeFileSync(CFG, JSON.stringify(cfg, null, 2) + '\n', 'utf8');
console.log('[sanity] 官方理智流失公式已写入 config' + (checkOnly ? '（--check：不写文件）' : ''));
console.log('  · passivePerSec 三档 × 两阶段（小/中/大 × setup/normal）');
console.log('  · difficultyMultiplier 五档 · bloodMoonAdditive · soloPassiveMultiplier · setupFloor=50');
console.log('  · light 口径（只有主灯能停 / 台灯电视手电无效 / 大暗区 80% / 火源 2m 按等级）');
console.log('  · sources 九项（与既有 allyDeath/jumpscare 一致，已注明）');
console.log('  下一步：node tools/data-mirror.mjs --sync 然后跑门禁');
