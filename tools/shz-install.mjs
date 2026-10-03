#!/usr/bin/env node
/**
 * shz-install.mjs — 以 adb(shell) 身份把 APK 流式安装到真机
 *
 * 为什么需要它（实测两个坑）：
 *   ① `pm install /storage/emulated/0/xxx.apk` 失败：
 *        "System server has no access to read file context u:object_r:fuse:s0"
 *      —— system_server 读不了 fuse（共享存储）上的文件，必须先落到 /data/local/tmp/。
 *   ② 但 shz 桥**不转发 stdin**，所以 `shz "cat > /data/local/tmp/x.apk" < apk` 拿不到内容。
 *   转机：pm 本身支持 `pm install -S <字节数>` 从 stdin 读包。Node 的 execFileSync 可以
 *   把 Buffer 作为 input 交给进程，于是 **APK 直接流进 pm，全程不需要中间文件**。
 *
 * 用法：node tools/shz-install.mjs <apk路径>
 *   -r 覆盖安装；失败时打印 pm 的原始输出（不吞错）。
 */
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';

const apk = process.argv[2];
if (!apk || !fs.existsSync(apk)) { console.error('用法: node tools/shz-install.mjs <apk路径>'); process.exit(2); }
const size = fs.statSync(apk).size;
const isShz = !process.env.DSH_ANDROID_USE_ADB;
const cmd = isShz ? 'shz' : 'adb';
const args = isShz
  ? [`pm install -r -S ${size}`]
  : ['shell', `pm install -r -S ${size}`];

console.log(`[install] ${apk}（${(size / 1048576).toFixed(1)} MB）经 ${cmd} 流式安装…`);
try {
  const out = execFileSync(cmd, args, {
    input: fs.readFileSync(apk),
    encoding: 'utf8',
    timeout: 300000,
    maxBuffer: 16 * 1024 * 1024,
  });
  const text = out.trim();
  console.log(text);
  process.exit(/Success/i.test(text) ? 0 : 1);
} catch (e) {
  const out = String(e.stdout ?? '') + String(e.stderr ?? '');
  console.error('[install] 失败：' + out.trim().slice(0, 2000));
  process.exit(1);
}
