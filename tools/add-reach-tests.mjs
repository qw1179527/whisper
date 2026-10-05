#!/usr/bin/env node
/**
 * add-reach-tests.mjs — 为「房间可达范围」补断言（跨地区要求的可判定化）
 *
 * ## 为什么这些断言值得存在
 * 用户要求「以跨地区联机为主」。零信令路线下**房间码决定可达范围**：
 * IPv6 码能跨地区、私网 IPv4 码只在同一 WiFi 内可达。
 * 如果判据错了，玩家会看到"可跨地区"却连不上 —— 而且**没有任何门禁会发现**（UI 文案不受编译门禁约束）。
 * 故把判据钉成断言：范围分类、房间码反解、以及"不夸大"的文案口径。
 *
 * 用法：node tools/add-reach-tests.mjs [--check]
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const FILE = path.join(ROOT, 'unity/Assets/Scripts/Tests/EditMode/LanSessionTests.cs');
const checkOnly = process.argv.includes('--check');

const raw = fs.readFileSync(FILE, 'utf8');
if (raw.includes('RoomReach_')) { console.log('[reach-tests] 已存在，跳过'); process.exit(0); }

const Q = String.fromCharCode(34);
const block = [
  '',
  '        // ── 房间可达范围（用户要求「跨地区为主」的可判定化）────────────────',
  '',
  '        [Test]',
  '        public void RoomReach_GlobalIPv6_IsCrossRegion()',
  '        {',
  '            // 本项目跨地区的主路径：全局 IPv6 端到端无 NAT（实测见 RoomCode 头注释）',
  '            Assert.AreEqual(RoomReach.CrossRegion, RoomReachJudge.OfAddress(' + Q + '2409:8a00:1234:5678::9' + Q + '));',
  '        }',
  '',
  '        [Test]',
  '        public void RoomReach_PrivateIPv4_IsSameNetworkOnly()',
  '        {',
  '            // 私网地址跨地区必然连不上 —— 必须如实标成「仅同网段」，不能标成可跨地区',
  '            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfAddress(' + Q + '192.168.1.44' + Q + '));',
  '            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfAddress(' + Q + '10.0.0.7' + Q + '));',
  '            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfAddress(' + Q + '172.20.3.9' + Q + '));',
  '        }',
  '',
  '        [Test]',
  '        public void RoomReach_PublicIPv4_IsUncertainNotCrossRegion()',
  '        {',
  '            // 实测移动网络是对称 NAT、打洞不可行 → 不能承诺跨地区（不夸大）',
  '            Assert.AreEqual(RoomReach.Uncertain, RoomReachJudge.OfAddress(' + Q + '203.0.113.9' + Q + '));',
  '        }',
  '',
  '        [Test]',
  '        public void RoomReach_LinkLocalAndLoopback_AreNotCrossRegion()',
  '        {',
  '            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfAddress(' + Q + 'fe80::1' + Q + '));',
  '            Assert.AreEqual(RoomReach.Unknown, RoomReachJudge.OfAddress(' + Q + '127.0.0.1' + Q + '));',
  '            Assert.AreEqual(RoomReach.Unknown, RoomReachJudge.OfAddress(' + Q + '乱码' + Q + '));',
  '            Assert.AreEqual(RoomReach.Unknown, RoomReachJudge.OfAddress(null));',
  '        }',
  '',
  '        [Test]',
  '        public void RoomReach_FromRoomCode_MatchesAddressJudgement()',
  '        {',
  '            // UI 是拿**房间码**判范围的，故反解口径必须与直接判地址一致',
  '            var v6 = RoomCode.Encode(' + Q + '2409:8a00:1234:5678::9' + Q + ', LanAddress.DefaultPort);',
  '            Assert.AreEqual(RoomReach.CrossRegion, RoomReachJudge.OfRoomCode(v6));',
  '            var v4 = RoomCode.Encode(' + Q + '192.168.1.44' + Q + ', LanAddress.DefaultPort);',
  '            Assert.AreEqual(RoomReach.SameNetwork, RoomReachJudge.OfRoomCode(v4));',
  '            Assert.AreEqual(RoomReach.Unknown, RoomReachJudge.OfRoomCode(' + Q + 'NOT-A-CODE' + Q + '));',
  '        }',
  '',
  '        [Test]',
  '        public void RoomReach_Describe_NeverOverPromises()',
  '        {',
  '            // 文案纪律：私网码不能出现「跨地区」，跨地区码必须提示双方都要 IPv6',
  '            var same = RoomReachJudge.Describe(RoomReach.SameNetwork);',
  '            Assert.IsFalse(same.Contains(' + Q + '跨地区' + Q + '), ' + Q + '私网码的文案不得出现「跨地区」：' + Q + ' + same);',
  '            var cross = RoomReachJudge.Describe(RoomReach.CrossRegion);',
  '            Assert.IsTrue(cross.Contains(' + Q + 'IPv6' + Q + '), ' + Q + '跨地区文案必须提示 IPv6 前提：' + Q + ' + cross);',
  '        }',
  '',
].join('\n');

const i = raw.lastIndexOf('    }');
if (i < 0) { console.error('[reach-tests] ✗ 未找到类闭合括号'); process.exit(1); }
const out = raw.slice(0, i) + block + raw.slice(i);
if (!checkOnly) fs.writeFileSync(FILE, out, 'utf8');
console.log(`[reach-tests] 新增 6 条断言（${raw.length} → ${out.length} 字节）`);
