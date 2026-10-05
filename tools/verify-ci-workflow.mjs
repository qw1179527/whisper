/**
 * verify-ci-workflow.mjs — 脚本层面校验 CI 出包工作流（**不靠记忆**）
 *
 * ## 为什么需要它
 * 「CI 仍能取到 Unity 版本并构建」这条验收，不能靠"我记得工作流里写了"。
 * 但 GitHub Actions 只在推送后才跑，而本仓库有未推送提交 + 历史凭据泄露记录，
 * 不能为了验证就推送。所以本脚本把**判据**落在工作流文件本身 + 工程侧可探测事实上：
 *   ① 工作流 YAML **结构与字段**齐全（steps 里有 GameCI、projectPath、buildMethod）
 *   ② `projectPath` 指向的目录真实存在，且有 `ProjectSettings/ProjectVersion.txt`
 *   ③ 该文件里声明的 Unity 版本 **与 unity-api-registry 台账一致**
 *   ④ `buildMethod` 声明的入口在工程里真的存在（不是记忆里的名字）
 *   ⑤ 工作流里含"包内版本校验"步骤（本轮新增，防止版本静默回落）
 *   ⑥ 工作流里含"包内套件取证"步骤（上一大类新增）
 *
 * 用法：node tools/verify-ci-workflow.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const WF = path.join(ROOT, '.github/workflows/unity-android.yml');
const problems = [];
const notes = [];

if (!fs.existsSync(WF)) { console.error(`✗ 找不到工作流：${WF}`); process.exit(1); }
const yml = fs.readFileSync(WF, 'utf8');
// 极简结构检查：不引 YAML 依赖（本机无 node_modules），只做"关键字段是否出现 + 缩进合理"的判断
const has = (s) => yml.includes(s);

// ① 结构
for (const [label, needle] of [
  ['on: 触发器', 'on:'],
  ['jobs:', 'jobs:'],
  ['GameCI 构建动作', 'game-ci/unity-builder@'],
  ['projectPath', 'projectPath:'],
  ['buildMethod', 'buildMethod:'],
]) {
  if (!has(needle)) problems.push(`工作流缺 ${label}（找不到 ${JSON.stringify(needle)}）`);
  else notes.push(`✓ 工作流含 ${label}`);
}

// ② projectPath 指向的目录 + ProjectVersion.txt
const m = yml.match(/projectPath:\s*(\S+)/);
const projRel = m ? m[1] : null;
let detected = null;
if (!projRel) problems.push('解析不出 projectPath');
else {
  const projAbs = path.join(ROOT, projRel);
  if (!fs.existsSync(projAbs)) problems.push(`projectPath 指向的目录不存在：${projRel}`);
  else notes.push(`✓ projectPath=${projRel} 存在`);
  const pv = path.join(projAbs, 'ProjectSettings/ProjectVersion.txt');
  if (!fs.existsSync(pv)) problems.push(`缺 ProjectVersion.txt（GameCI 靠它自动探测 Unity 版本）：${projRel}/ProjectSettings/ProjectVersion.txt`);
  else {
    const text = fs.readFileSync(pv, 'utf8');
    const mm = text.match(/m_EditorVersion:\s*(\S+)/);
    detected = mm ? mm[1] : null;
    if (!detected) problems.push('ProjectVersion.txt 里读不到 m_EditorVersion');
    else notes.push(`✓ GameCI 会探测到 Unity ${detected}`);
  }
}

// ③ 与 API 台账一致
const regPath = path.join(ROOT, 'data/unity-api-registry.json');
if (fs.existsSync(regPath) && detected) {
  const reg = JSON.parse(fs.readFileSync(regPath, 'utf8'));
  if (reg.unityVersion && reg.unityVersion !== detected)
    problems.push(`Unity 版本不一致：台账 ${reg.unityVersion} vs ProjectVersion ${detected}`);
  else notes.push(`✓ Unity 版本与台账一致（${detected}）`);
}

// ④ buildMethod 入口存在
const bm = yml.match(/buildMethod:\s*(\S+)/);
if (!bm) problems.push('解析不出 buildMethod');
else {
  const full = bm[1];                       // 形如 Whisper.Editor.BuildScript.BuildAndroid
  const parts = full.split('.');
  const method = parts.pop();
  const cls = parts.pop();
  const csDir = path.join(ROOT, projRel ?? 'unity', 'Assets/Editor');
  let found = false;
  if (fs.existsSync(csDir)) {
    for (const f of fs.readdirSync(csDir).filter((x) => x.endsWith('.cs'))) {
      const t = fs.readFileSync(path.join(csDir, f), 'utf8');
      if (t.includes(`class ${cls}`) && new RegExp(`(static\\s+)?(public\\s+)?void\\s+${method}\\s*\\(`).test(t)) { found = true; break; }
    }
  }
  if (!found) problems.push(`buildMethod 入口在工程里找不到：${full}（查过 ${projRel}/Assets/Editor/*.cs）`);
  else notes.push(`✓ buildMethod=${full} 入口存在`);
}

// ⑤⑥ 本轮/上一大类新增的两步
// 【判据收紧记录 · 两次】
//   第一次只查 `versionName` 字样 → 变异"把 versionName 改掉"仍通过（别处还有这个词）。
//   第二次查字面 `aapt2 dump badging` → **基线本身判红**：工作流用的是变量 `$AAPT2` + `dump badging`，
//   字面量根本不存在（判据过窄会把正常的判成坏的）。
//   现在查**本质**：读包内 badging 的能力 + "回落常量"这条判据，两者缺一即红。
const readsBadging = /dump\s+badging/.test(yml) && /aapt/i.test(yml);
if (!readsBadging) problems.push('工作流缺"包内版本校验"的实质能力（需 aapt/aapt2 dump badging 从包内读版本）——版本静默回落会没人发现');
else if (!/0\.1\.1/.test(yml)) problems.push('工作流的版本校验里没有"回落常量 0.1.1"这条判据（回落会被放过）');
else notes.push('✓ 工作流含包内版本校验：dump badging + 回落常量判据');
if (!has('verify-packed-kits.mjs')) problems.push('工作流缺"包内套件取证"步骤（tools/verify-packed-kits.mjs）');
else notes.push('✓ 工作流含包内套件取证（tools/verify-packed-kits.mjs）');

// ⑦ 真 Unity 测试回归必须在 CI 里跑（否则"本机桩绿、真 Unity 红"没人拦）
if (!/unity-test-runner@/.test(yml)) problems.push('工作流缺"真 Unity 测试回归"步骤（game-ci/unity-test-runner）——桩断言替代不了真 Unity');
else if (!/testMode:\s*all/.test(yml)) problems.push('工作流的测试步骤没覆盖两个平台（应 testMode: all 或分别跑 editmode/playmode）');
else notes.push('✓ 工作流含真 Unity 测试回归且覆盖两个平台（testMode: all）');

for (const n of notes) console.log('  ' + n);
if (problems.length) {
  console.error(`\n✗ CI 工作流校验失败（${problems.length} 项）：`);
  for (const p of problems) console.error('  - ' + p);
  process.exit(1);
}
console.log(`\n✓ CI 工作流校验通过（${notes.length} 项；Unity ${detected}）`);
