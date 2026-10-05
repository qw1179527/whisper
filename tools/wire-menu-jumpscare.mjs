// 把主界面与跳脸接进组合根 `GameBootstrap.cs`。
// 用 node 改而不是 PowerShell（后者对 C# 里的引号与中文注释必然出错，本项目已踩多次）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 字段
if (!s.includes('JumpscareView _jumpscare;')) {
  s = s.replace('        PlayerBody _playerBody;',
`        PlayerBody _playerBody;
        /// <summary>跳脸视图（猎杀致死时播放）。</summary>
        JumpscareView _jumpscare;
        /// <summary>主界面（代码构建；arch-guard 只允许 Boot.unity 一个场景文件）。</summary>
        MenuScene _menu;
        PlayerController _playerControllerRef;`);
  log.push('  ✓ 字段');
} else log.push('  · 字段已存在');

// ② 玩家引用
if (!s.includes('_playerControllerRef = _player;')) {
  s = s.replace('                _player = playerGo.GetComponent<PlayerController>();',
`                _player = playerGo.GetComponent<PlayerController>();
                _playerControllerRef = _player;`);
  log.push('  ✓ 玩家引用');
}

// ③ 公开入口
const ANCHOR = '        public LevelData Level { get; private set; }';
if (!s.includes('public void OnMenuStartRequested()')) {
  const block = `        /// <summary>主界面请求开始对局（由 MenuScene 调用）。</summary>
        /// <remarks>
        /// 为什么做成公开方法而不是让 MenuScene 直接操作本对象的字段：
        /// 组合根是**唯一**知道"对局怎么开始"的地方（要关主界面、要保证玩家/怪物/HUD 都就绪）。
        /// 主界面只负责"用户点了什么"，不负责"怎么开局" —— 否则两处都要改。
        /// </remarks>
        public void OnMenuStartRequested()
        {
            if (_menu != null) _menu.gameObject.SetActive(false);
            // 猎杀/死亡状态复位：重开一局不能带着上一局的红闪与文字
            if (_jumpscare != null) _jumpscare.Reset();
        }

        /// <summary>猎杀致死入口（供怪物/网络层调用）：播跳脸。</summary>
        public void OnPlayerKilled(bool redEyes)
        {
            if (_jumpscare == null) return;
            _jumpscare.RedEyes = redEyes;
            _jumpscare.Play();
        }

        /// <summary>HUD/自检用：主界面与跳脸状态。</summary>
        public string DescribeMenuAndScare()
            => (_menu != null ? _menu.Describe() : "主界面：未构建") + " · "
             + (_jumpscare != null ? _jumpscare.Describe() : "跳脸：未构建");

${ANCHOR}`;
  if (s.includes(ANCHOR)) { s = s.replace(ANCHOR, block); log.push('  ✓ OnMenuStartRequested/OnPlayerKilled/Describe'); }
  else log.push('  ! Level 锚点未中');
} else log.push('  · 公开入口已存在');

// ④ 主界面构建（放在 Boot 里 HUD 之后）
if (!s.includes('AddComponent<MenuScene>()')) {
  const marker = '                lines.AppendLine(GhostModelPool.Describe());';
  if (s.includes(marker)) {
    s = s.replace(marker, marker + `
                // 主界面：代码构建（3D 空间 + 闪烁灯 + 右下角手电筒 + 概率刷鬼 + 右侧玩法选项）
                _menu = gameObject.AddComponent<MenuScene>();
                _menu.Build(transform);
                lines.AppendLine(_menu.Describe());`);
    log.push('  ✓ 主界面构建');
  } else log.push('  ! 主界面锚点未中（GhostModelPool.Describe 那行）');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
