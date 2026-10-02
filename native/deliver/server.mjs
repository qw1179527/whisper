import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// 交付服务（本机 127.0.0.1）。
// 设计要点（都来自踩过的坑）：
//   · **每次请求重扫目录**，且对不可读文件容错 —— 旧版把清单缓存进闭包，删掉某个 APK 后整进程 ENOENT 崩掉；
//   · 同时暴露 本机构建产物目录 与 DSH专用 共享目录，便于"基线 vs 新包"对照下载；
//   · 文案与产物同步（旧文案写"10 房间"，而关卡已重写为 11 房间 —— 这类过期描述会被当成事实）。
const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOTS = [
  { dir: HERE, label: '本机构建产物' },
  { dir: '/storage/emulated/0/DSH专用', label: 'DSH专用（共享目录）' },
];

const listAll = () => {
  const out = [];
  for (const r of ROOTS) {
    let files = [];
    try { files = fs.readdirSync(r.dir).filter((f) => f.endsWith('.apk')); } catch { continue; }
    for (const f of files) {
      let size = -1, mtime = 0;
      try { const st = fs.statSync(path.join(r.dir, f)); size = st.size; mtime = st.mtimeMs; } catch { /* 不可读 */ }
      out.push({ f, size, mtime, root: r.dir, label: r.label });
    }
  }
  return out.sort((a, b) => b.mtime - a.mtime);
};

const page = (items) => `<!doctype html><meta charset=utf-8><meta name=viewport content="width=device-width,initial-scale=1">
<title>低语计划 · 构建产物</title>
<body style="font-family:system-ui;background:#0d0d0f;color:#f0e6d2;padding:20px;line-height:1.7">
<h2 style="margin:0 0 4px">Project Whisper · 构建产物</h2>
<p style="color:#8c7e66;margin:0 0 16px">点链接下载安装（需允许「安装未知应用」）</p>
<ul style="padding-left:18px;list-style:none">${items.map(({ f, size, label }) =>
  `<li style="margin:10px 0"><a style="color:#7fb98a;font-size:18px" href="/f/${encodeURIComponent(f)}?r=${encodeURIComponent(label)}">${f}</a>
   <small style="color:#8c7e66"> · ${size > 0 ? (size / 1024).toFixed(1) + ' KB' : '不可读'} · ${label}</small></li>`).join('') || '<li>（暂无产物）</li>'}</ul>
<div style="color:#8c7e66;font-size:13px;margin-top:20px;border-top:1px solid #2a2a2e;padding-top:12px">
<b>怎么确认装的是新版</b>：进游戏后右上角有版本水印，形如
<code style="color:#c9a227">0.7.0-dev · build … · game.js 159516B · 68e52212</code>。
旧包（0.6.0 基线）没有这行水印。<br>
<b>两者关系</b>：0.6.0 是可玩基线；0.7.0-dev 是同一份灰盒代码 + 更新后的数据与版本标识（当前 Unity 主线仍在 CI 才可跑，
本机无 Unity 本体，所以这个包仍是 WebView 灰盒，不是 Unity 包）。
</div></body>`;

const srv = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  if (url === '/' || url === '/index.html') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
    res.end(page(listAll()));
    return;
  }
  if (url.startsWith('/f/')) {
    const name = path.basename(url.slice(3));
    for (const r of ROOTS) {
      const fp = path.join(r.dir, name);
      try {
        if (!fs.existsSync(fp)) continue;
        const st = fs.statSync(fp);
        res.writeHead(200, { 'content-type': 'application/vnd.android.package-archive', 'content-length': st.size });
        fs.createReadStream(fp).pipe(res);
        return;
      } catch { /* 换下一个根 */ }
    }
    res.writeHead(404); res.end('not found'); return;
  }
  res.writeHead(404); res.end('not found');
});
srv.listen(8899, '127.0.0.1', () => console.log('deliver server on http://127.0.0.1:8899'));
