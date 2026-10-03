// GitHub Actions secret 加密：libsodium-wrappers 官方 crypto_box_seal
// 为什么绕这一圈：本机没有 libsodium，pnpm 在 Android 上因文件锁装不了依赖；
// 于是手工把 npm 包(vendor 进 nm/)按 node_modules 布局摆好，再用 require 解析。
const path = require('path');
const Module = require('module');
// 把 nm/ 当作 node_modules 根
const NM = path.join(__dirname, 'nm');
const orig = Module._resolveFilename;
Module._resolveFilename = function (request, ...rest) {
  if (request === 'libsodium') {
    return path.join(NM, 'libsodium', 'dist', 'modules', 'libsodium.js');
  }
  return orig.call(this, request, ...rest);
};
const sodium = require(path.join(NM, 'libsodium-wrappers', 'dist', 'modules', 'libsodium-wrappers.js'));

async function sealSecret(publicKeyB64, value) {
  await sodium.ready;
  const rpk = sodium.from_base64(publicKeyB64, sodium.base64_variants.ORIGINAL);
  return sodium.to_base64(sodium.crypto_box_seal(sodium.from_string(value), rpk), sodium.base64_variants.ORIGINAL);
}
module.exports = { sealSecret, sodium };
