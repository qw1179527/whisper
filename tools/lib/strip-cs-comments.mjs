// 剥掉 C# 注释，只留代码 —— 供各门禁做"文本正则"匹配时使用。
//
// ## 为什么要有这个模块
// `gate-physics` 与 `gate-code` 都栽过同一个坑：**直接对文件原文做正则**，
// 于是"注释里解释纪律"被当成"违反纪律"。2026-10-05 实测的误报：
// ```
// gate-physics（4 处）：
//   InteractionSystem.cs:48   「……这样 gate-physics 的"禁 UnityEngine.Random / DateTime"纪律不会被绕过」
//   TaskSystem.cs:59-61       「gate-physics 禁止 DateTime.Now/UtcNow……所以把 dayIndex 作为参数传进来」
//   ProceduralTextures.cs:28  「全部用整数哈希 + xorshift，不用 UnityEngine.Random（本工程门禁禁止）」
//   GameBootstrap.cs:533      「确定性随机源（xorshift32）—— 禁 UnityEngine.Random」
// gate-code（2 处）：
//   GhostModelPool.cs:49-51   「【本工程的 `Resources.Load` 约定：保留 `.glb`】……」
//   ModelLibrary.cs:169,185   「为什么需要：真机上 `Resources.Load` 失败时……」/「只给文件名」
// ```
// **误报比漏报更坏**：它会训练人**不敢在注释里记录纪律**，而本工程的纪律正是靠注释传承的。
//
// ## 为什么不能简单地"砍掉 // 之后的内容"
// `"http://a//b"`、`@"C:\x"`、`'/'` 里都有斜杠。粗暴处理会把**真代码**当注释吞掉，
// 那就从"误报"翻成**漏报**（更危险）。所以必须用状态机逐字符走。
//
// ## 用法与边界（**重要**）
// 只在检查「**代码行为**」时用它。检查「**注释内容**」时**必须用原文**：
//   · C3 悬挂标记（TODO/FIXME）—— 标记本来就在注释里，剥了就永远查不到
//   · C7 类缺 `<summary>` —— 要的正是注释
// 用错方向的后果：前者变成"门禁永远绿"，后者变成"门禁永远红"。

/**
 * 把 C# 源码里的注释替换成**等长空格**（保留换行），返回"只有代码"的文本。
 * 等长替换的好处：行号列宽与原文一一对应，报错位置不用换算。
 *
 * 处理的状态：代码 / 行注释 / 块注释 / 普通字符串 / 逐字字符串（`@"…"`、`$@"…"`、`@$"…"`）/ 字符字面量。
 *
 * @param {string} src C# 源码原文
 * @returns {string} 注释被空格顶替后的文本
 */
export function stripCsComments(src) {
  let out = '';
  let i = 0;
  const n = src.length;
  let st = 'code'; // code | line | block | str | vstr | chr
  while (i < n) {
    const c = src[i];
    const c2 = src[i + 1];
    if (st === 'code') {
      if (c === '/' && c2 === '/') { st = 'line'; out += '  '; i += 2; continue; }
      if (c === '/' && c2 === '*') { st = 'block'; out += '  '; i += 2; continue; }
      // 逐字字符串：@"…" / $@"…" / @$"…"
      if ((c === '@' && c2 === '"')
        || (c === '$' && c2 === '@' && src[i + 2] === '"')
        || (c === '@' && c2 === '$' && src[i + 2] === '"')) {
        const len = (c === '@' && c2 === '"') ? 2 : 3;
        st = 'vstr'; out += src.substr(i, len); i += len; continue;
      }
      if (c === '"') st = 'str';
      else if (c === "'") st = 'chr';
      out += c; i++; continue;
    }
    if (st === 'line') {
      if (c === '\n') { st = 'code'; out += '\n'; i++; continue; }
      out += ' '; i++; continue;
    }
    if (st === 'block') {
      if (c === '*' && c2 === '/') { st = 'code'; out += '  '; i += 2; continue; }
      out += (c === '\n') ? '\n' : ' '; i++; continue;
    }
    if (st === 'str') {
      if (c === '\\') { out += src.substr(i, 2); i += 2; continue; }
      if (c === '"') st = 'code';
      out += c; i++; continue;
    }
    if (st === 'vstr') {
      if (c === '"' && c2 === '"') { out += '""'; i += 2; continue; } // "" = 转义的双引号
      if (c === '"') st = 'code';
      out += c; i++; continue;
    }
    // chr
    if (c === '\\') { out += src.substr(i, 2); i += 2; continue; }
    if (c === "'") st = 'code';
    out += c; i++; continue;
  }
  return out;
}

/**
 * 剥离器自检：4 个正反例。**每次运行门禁都该跑一遍**。
 *
 * 为什么必须有：剥离器一旦"砍多了"，就把**真代码**当注释吞掉 →
 * 门禁从"误报"翻成**漏报**（更危险，且不报错、静默变绿）。
 * 所以要用反例钉住它 —— 尤其是第 4 条：字符串里的 `//` 不能吞掉后面的真代码。
 *
 * @param {(msg: string) => void} fail 失败回调（各门禁的 `bad()`）
 * @returns {number} 失败条数（0 = 全中）
 */
export function selfTestStripCsComments(fail) {
  const probe = /Random\s*\.\s*(?:Range|value)|DateTime\s*\.\s*(?:Now|UtcNow)/;
  const cases = [
    ['var x = Random.Range(0, 1);', true, '代码里的调用必须留下'],
    ['// Random.Range 禁用（注释）', false, '行注释里的必须剥掉'],
    ['/* DateTime.Now 不可复现 */ var y = 1;', false, '块注释里的必须剥掉'],
    ['var s = "http://a//b"; var z = Random.Range(0,1);', true, '字符串里的 // 不能吞掉后面的真代码'],
  ];
  let n = 0;
  for (const [src, shouldKeep, why] of cases) {
    const got = probe.test(stripCsComments(src));
    if (got !== shouldKeep) {
      fail(`注释剥离器自检失败（${why}）：输入 ${JSON.stringify(src)} → 期望${shouldKeep ? '保留' : '剥掉'}却相反`);
      n++;
    }
  }
  return n;
}
