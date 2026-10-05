#!/usr/bin/env node
/**
 * tidy-board-camera.mjs — 去掉到位判定里重复计算的 wantPos/wantLook
 *
 * ## 为什么
 * `apply-ui-fixes-r8.mjs` 加"双条件到位判定"时，我在判定里**又算了一遍** `Vector3.Lerp(...)`，
 * 而紧随其后就是同一份 `wantPos/wantLook` 的正式计算 → **两份口径**。
 * 两份同式同参时结果一样，但一旦以后有人只改其中一处（例如给缓动换曲线），
 * 就会出现"判定用的目标"与"实际写入的目标"不是同一个 —— 那正是"抖屏"这类问题的经典成因。
 * 修法：只算一次，判定复用同两个变量。
 *
 * 用法：node tools/tidy-board-camera.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
if (!raw.includes('wantPosChk')) { console.log('[tidy] 已整理过，跳过'); process.exit(0); }

// ① 判定处：删掉重复计算，改用后面统一算出的 wantPos/wantLook
const judgementFrom = [
  '            var wantPosChk = Vector3.Lerp(posFree, posBoard, k);',
  '            var wantLookChk = Vector3.Lerp(lookFree, lookBoard, k);',
  '            bool blendSettled = Mathf.Abs(_boardBlend - target) < 0.0005f;',
  '            bool camSettled = ( _cam.transform.position - wantPosChk).sqrMagnitude < 0.000001f;',
  '            bool settled = blendSettled && camSettled;',
].join('\n');
if (raw.split(judgementFrom).length - 1 !== 1) {
  console.error('[tidy] ✗ 未找到判定块（可能已被改）');
  process.exit(1);
}

// ② 把正式计算**提前**到判定之前，判定直接复用它
const formalBlock = [
  '            var wantPos = Vector3.Lerp(posFree, posBoard, k);',
  '            var wantLook = Vector3.Lerp(lookFree, lookBoard, k);',
].join('\n');
if (raw.split(formalBlock).length - 1 !== 1) {
  console.error('[tidy] ✗ 未找到正式计算块（可能已被改）');
  process.exit(1);
}

let src = raw;
// 先删正式计算块（留一个占位，稍后在判定前插入）
src = src.replace(formalBlock + '\n', '');
// 再把判定块换成「先算、后判」
const judgementTo = [
  '            // 目标位姿**只算一次**，判定与写入复用同一份 —— 两份口径是"抖屏"这类问题的经典成因。',
  '            var wantPos = Vector3.Lerp(posFree, posBoard, k);',
  '            var wantLook = Vector3.Lerp(lookFree, lookBoard, k);',
  '            bool blendSettled = Mathf.Abs(_boardBlend - target) < 0.0005f;',
  '            bool camSettled = (_cam.transform.position - wantPos).sqrMagnitude < 0.000001f;',
  '            bool settled = blendSettled && camSettled;',
].join('\n');
src = src.replace(judgementFrom, judgementTo);

if (!checkOnly) fs.writeFileSync(FILE, src, 'utf8');
console.log(`[tidy] 到位判定与写入口径已统一（${raw.length} → ${src.length} 字节）`);
