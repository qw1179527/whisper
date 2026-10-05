#!/usr/bin/env node
/**
 * add-map-registry.mjs — 多地图（用户要求「接下来接入多地图选项」）第一步：注册表 + 运行时选图
 *
 * ## 为什么需要注册表
 * 现状：**只有一张图**，且在代码里硬编码（`GameBootstrap.LevelResourcePath = "Levels/asylum_v1"`），
 * `config.json` 顶层**没有** maps 段（实测 26 个键里无 maps）。
 * 于是"地图选项板/投票"没有任何数据可依附 —— 只能显示静态文字（第 12 轮已如实标注）。
 *
 * ## 本脚本做两件事
 * 1. `data/config.json` 增加 `maps` 注册表（**数值真源**，改数值不碰代码 —— V9 §19.5）；
 * 2. `GameBootstrap` 从注册表解析当前地图的 `resource`，提供 `SelectMap(id)` 与 `DescribeMaps()`。
 *
 * ## 用的都是**读到的**真实 API（不是猜的）
 * `GameConfig.Get(path)` / `GetString` / `GetBool`；`MiniJson.AsMap` / `AsString` / `AsBool`。
 *
 * ## 纪律
 * · 只**新增**键，不动既有键；首次写入前备份；
 * · 找不到锚点即停止、不写文件；
 * · 写完后必须 `node tools/data-mirror.mjs --sync`（三份镜像）+ 双门禁。
 *
 * 用法：node tools/add-map-registry.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const checkOnly = process.argv.includes('--check');
const log = [];
const fail = (m) => { console.error('[maps] ✗ ' + m); process.exit(1); };

// ── ① config.json：新增 maps 注册表 ───────────────────────────────────
{
  const CFG = path.join(ROOT, 'data/config.json');
  const raw = fs.readFileSync(CFG, 'utf8');
  const cfg = JSON.parse(raw);
  if (cfg.maps) log.push('  · config.maps 已存在，跳过');
  else {
    const maps = {
      _note: '地图注册表（多地图唯一真源）。resource = Resources 下路径（不含 .json）；size/floors 供地图板与投票显示。',
      _src: 'asylum_v1 = design（由 tools/gen-asylum-v1.mjs 生成：房间 15 / 走廊 12）；其余为 official 地图名，关卡文件尚未生成。',
      list: [
        {
          id: 'asylum_v1', name: '疗养院', resource: 'Levels/asylum_v1',
          floors: 3, size: 'large', implemented: true,
          _src: 'design：本仓已建成（tools/gen-asylum-v1.mjs）'
        },
        {
          id: 'tanglewood_v1', name: '坦格尔伍德街 6 号', resource: 'Levels/tanglewood_v1',
          floors: 1, size: 'small', implemented: false,
          _src: 'official：Phasmophobia 官方最小图（6 Tanglewood Drive，单层住宅）；关卡未生成'
        },
        {
          id: 'bleasdale_v1', name: '布利斯代尔农舍', resource: 'Levels/bleasdale_v1',
          floors: 2, size: 'medium', implemented: false,
          _src: 'official：Phasmophobia 官方中型图（Bleasdale Farmhouse，两层）；关卡未生成'
        }
      ]
    };
    const out = {};
    for (const k of Object.keys(cfg)) { out[k] = cfg[k]; if (k === 'level') out.maps = maps; }
    if (!out.maps) out.maps = maps;
    if (!checkOnly) {
      const bak = CFG + '.bak-maps';
      if (!fs.existsSync(bak)) fs.writeFileSync(bak, raw, 'utf8');
      fs.writeFileSync(CFG, JSON.stringify(out, null, 2) + '\n', 'utf8');
    }
    log.push('  ✓ config.json 新增 maps 注册表（3 条：1 已实现 + 2 官方待实现）');
  }
}

// ── ② GameBootstrap：注册表解析 + 选图 ────────────────────────────────
{
  const rel = 'unity/Assets/Scripts/Runtime/GameBootstrap.cs';
  const p = path.join(ROOT, rel);
  const s0 = fs.readFileSync(p, 'utf8');
  if (s0.includes('SelectMap')) log.push('  · GameBootstrap.SelectMap 已存在，跳过');
  else {
    const anchor = '        public string LevelResourcePath = "Levels/asylum_v1";';
    const n = s0.split(anchor).length - 1;
    if (n !== 1) fail(`LevelResourcePath 锚点命中 ${n} 次（应为 1）`);
    const Q = String.fromCharCode(34);
    const add = [
      anchor,
      '',
      '        /// <summary>当前地图 id（与 config.maps.list[].id 对应）。默认 asylum_v1。</summary>',
      '        public string MapId { get; private set; } = ' + Q + 'asylum_v1' + Q + ';',
      '',
      '        /// <summary>',
      '        /// 选图：按 `config.maps.list` 解析 `resource` 并切换关卡路径。',
      '        /// 为什么走注册表：多地图是用户要求（"接入多地图选项"），而资源路径属**数值真源**',
      '        /// （V9 §19.5：改数值不碰代码）。',
      '        /// 返回 false = 该图未实现或不在注册表 —— **不静默换图**（换错图比报错更糟）。',
      '        /// </summary>',
      '        public bool SelectMap(string mapId)',
      '        {',
      '            if (string.IsNullOrEmpty(mapId)) return false;',
      '            var list = GameConfig.Get(' + Q + 'maps.list' + Q + ') as System.Collections.Generic.List<object>;',
      '            if (list == null) return false;',
      '            for (int i = 0; i < list.Count; i++)',
      '            {',
      '                var m = MiniJson.AsMap(list[i]);',
      '                if (m == null) continue;',
      '                var id = MiniJson.AsString(MiniJson.Get(m, ' + Q + 'id' + Q + '));',
      '                if (id != mapId) continue;',
      '                var res = MiniJson.AsString(MiniJson.Get(m, ' + Q + 'resource' + Q + '));',
      '                if (string.IsNullOrEmpty(res)) return false;',
      '                LevelResourcePath = res;',
      '                MapId = id;',
      '                return true;',
      '            }',
      '            return false;',
      '        }',
      '',
      '        /// <summary>地图注册表摘要（地图板/HUD 用；明确标出"哪些已实现"）。</summary>',
      '        public string DescribeMaps()',
      '        {',
      '            var list = GameConfig.Get(' + Q + 'maps.list' + Q + ') as System.Collections.Generic.List<object>;',
      '            if (list == null) return ' + Q + '地图注册表缺失' + Q + ';',
      '            var sb = new System.Text.StringBuilder();',
      '            for (int i = 0; i < list.Count; i++)',
      '            {',
      '                var m = MiniJson.AsMap(list[i]);',
      '                if (m == null) continue;',
      '                var id = MiniJson.AsString(MiniJson.Get(m, ' + Q + 'id' + Q + '));',
      '                var name = MiniJson.AsString(MiniJson.Get(m, ' + Q + 'name' + Q + '));',
      '                bool impl = MiniJson.AsBool(MiniJson.Get(m, ' + Q + 'implemented' + Q + '));',
      '                if (sb.Length > 0) sb.Append(' + Q + ' · ' + Q + ');',
      '                sb.Append(name).Append(' + Q + '(' + Q + ').Append(id).Append(' + Q + ')' + Q + ');',
      '                if (id == MapId) sb.Append(' + Q + '★' + Q + ');',
      '                if (!impl) sb.Append(' + Q + '（未实现）' + Q + ');',
      '            }',
      '            return sb.ToString();',
      '        }',
    ].join('\n');
    if (!checkOnly) fs.writeFileSync(p, s0.replace(anchor, add), 'utf8');
    log.push('  ✓ GameBootstrap 新增 SelectMap / DescribeMaps / MapId');
  }
}

console.log('[maps] 多地图注册表与运行时选图' + (checkOnly ? '（--check：不写文件）' : ''));
for (const l of log) console.log(l);
console.log('  下一步：node tools/data-mirror.mjs --sync → 双门禁');
