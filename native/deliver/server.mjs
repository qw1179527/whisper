import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// 交付服务（本机 127.0.0.1）—— 每次请求重扫目录，且对缺失文件容错。
// 缺陷记录：旧版在启动时把文件清单与大小缓存进闭包，删除某个 APK 后整进程 ENOENT 崩掉。
const ROOT = path.dirname(fileURLToPath(import.meta.url));
const listApks = () =>
  fs.readdirSync(ROOT).filter((f) => f.endsWith('.apk')).map((f) => {
    let size = -1;
    try { size = fs.statSync(path.join(ROOT, f)).size; } catch { /* 不可读 */ }
    return { f, size };
  });

const srv = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  if (url === '/') {
    const items = listApks();
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
    res.end(`<!doctype html><meta charset=utf-8><meta name=viewport content="width=device-width,initial-scale=1">
<title>低语计划 · 本机构建产物</title>
<body style="font-family:system-ui;background:#0d0d0f;color:#f0e6d2;padding:20px;line-height:1.6">
<h2 style="margin:0 0 4px">Project Whisper · 本机构建产物</h2>
<p style="color:#8c7e66;margin:0 0 16px">点链接下载安装（需允许「安装未知应用」）</p>
<ul style="padding-left:18px">${items.map(({ f, size }) => `<li style="margin:8px 0"><a style="color:#5c8c6e;font-size:18px" href="/${encodeURIComponent(f)}">${f}</a> <small style="color:#8c7e66">${size > 0 ? (size / 1024).toFixed(1) + ' KB' : '不可读'}</small></li>`).join('') || '<li>（暂无产物）</li>'}</ul>
<p style="color:#8c7e66;font-size:13px;margin-top:18px">打开 App 即自动跑验证并把结果打屏：三接口契约装配、Level DSL 解析真实关卡（asylum_v1：10 房间 / 5 证据点 / 双撤离点）、DesignTokens 取值，以及 5 项负向验证（畸形关卡必须被拦）。</p>
</body>`);
    return;
  }
  const name = path.basename(url);
  const fp = path.join(ROOT, name);
  if (!name.endsWith('.apk') || !fs.existsSync(fp)) { res.writeHead(404); res.end('not found'); return; }
  res.writeHead(200, { 'content-type': 'application/vnd.android.package-archive', 'content-length': fs.statSync(fp).size });
  fs.createReadStream(fp).pipe(res);
});
srv.listen(8899, '127.0.0.1', () => console.log('deliver server on http://127.0.0.1:8899'));
