// 把电闸（PowerSystem）与互动（InteractionSystem）接进玩法循环：
//   · LightRig 已有的 SetRoomLights/SetAllLights 由 PowerSystem 的事件驱动（灯随总闸与房间开关）
//   · 每帧 Tick 互动（鬼开关灯/扔物/敲击/关总闸）
// **本文件不得出现反引号**（会截断 JS 模板）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/GameBootstrap.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① LightRig 引用字段
if (!s.includes('_lightRig')) {
  s = s.replace('        Whisper.Gameplay.Interaction.InteractionSystem _interaction;',
`        Whisper.Gameplay.Interaction.InteractionSystem _interaction;
        /// <summary>关卡灯光（由电力系统驱动；它已有平滑开关过渡，不重复实现）。</summary>
        Whisper.Gameplay.Level.LightRig _lightRig;
        /// <summary>互动节拍用的确定性随机源（禁 UnityEngine.Random；见 gate-physics）。</summary>
        uint _interactRng = 0x1BADB002u;
        float _ghostRoomDwellAcc;`);
  log.push('  ✓ 字段');
}

// ② InitProgressionSystems 里：把 LightRig 与电力/互动接通
if (!s.includes('WirePowerToLights')) {
  s = s.replace('            lines.AppendLine(_interaction.Describe());\n        }',
`            lines.AppendLine(_interaction.Describe());
            WirePowerToLights();
            PlaceBreaker();
        }

        /// <summary>把电力系统接到灯光上：总闸与房间开关的事件 → LightRig 的平滑开关。</summary>
        /// <remarks>
        /// 为什么用事件而不是每帧轮询：LightRig 内部已有 On→Target 的平滑过渡（ToggleSpeed），
        /// 每帧硬设 intensity 会把过渡打掉（灯会"跳"而不是"亮起来"）。
        /// </remarks>
        void WirePowerToLights()
        {
            if (_power == null) return;
            _power.OnBreakerChanged += on =>
            {
                if (_lightRig != null) _lightRig.SetAllLights(on);
            };
            _power.OnRoomLightChanged += (roomId, on) =>
            {
                if (_lightRig != null) _lightRig.SetRoomLights(roomId, on);
            };
        }

        /// <summary>把总闸放到关卡数据给的位置（没有就退回入口区，保证玩家找得到）。</summary>
        void PlaceBreaker()
        {
            if (_power == null || Level == null) return;
            // 关卡里没有专门的"总闸位置"字段 → 用**最深的房间**（离入口最远）作为总闸位置：
            // 这正是官方"必须深入才有电"的意图，而且是从数据推出来的，不是硬编码坐标。
            string far = null; float best = -1f;
            var entrance = Level.Rooms.Find(r => r.Id == (Level.Extraction?.Standard ?? ""));
            float ex = entrance?.MinX ?? 0f, ez = entrance?.MinZ ?? 0f;
            foreach (var r in Level.Rooms)
            {
                float dx = (r.MinX + r.MaxX) * 0.5f - ex, dz = (r.MinZ + r.MaxZ) * 0.5f - ez;
                float d = dx * dx + dz * dz;
                if (d > best) { best = d; far = r.Id; }
            }
            var target = Level.Rooms.Find(r => r.Id == far);
            if (target != null)
            {
                _power.PlaceBreaker(target.Id, (target.MinX + target.MaxX) * 0.5f, (target.MinZ + target.MaxZ) * 0.5f);
            }
            // 每个房间都登记"有灯"（LightRig 是按房间建灯的；没有灯的房间自然收不到 SetRoomLights 的效果）
            foreach (var r in Level.Rooms) _power.RegisterRoomLight(r.Id, true);
            Debug.Log("[Whisper] 总闸放在 " + far + "（离入口最远的房间）· 房间灯 " + Level.Rooms.Count + " 个");
        }
`);
  log.push('  ✓ 电力接灯光 + 放置总闸');
}

// ③ 每帧 Tick 互动 + 任务进度（放在 HUD 刷新附近）
if (!s.includes('TickInteractionAndTasks')) {
  s = s.replace('        void Update()\n        {',
`        void Update()
        {
            TickInteractionAndTasks();
`);
  s = s.replace('        /// <summary>初始化等级/商店/任务/电力/互动（一次性；Boot 阶段调用）。</summary>',
`        /// <summary>每帧推进：鬼互动节拍 + 任务进度（鬼房停留秒数）。</summary>
        void TickInteractionAndTasks()
        {
            if (_interaction == null || _power == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 确定性随机源（xorshift32）—— 禁 UnityEngine.Random：它依赖全局种子，跨端不一致
            float Roll()
            {
                _interactRng ^= _interactRng << 13; _interactRng ^= _interactRng >> 17; _interactRng ^= _interactRng << 5;
                return (_interactRng & 0xFFFFFF) / 16777216f;
            }

            string ghostRoom = _ghostRoom;
            float gx = 0f, gz = 0f;
            if (_monsters != null && _monsters.LastViews != null && _monsters.LastViews.Length > 0)
            { gx = _monsters.LastViews[0].X; gz = _monsters.LastViews[0].Z; }

            var rooms = new System.Collections.Generic.List<string>();
            if (Level != null) foreach (var r in Level.Rooms) rooms.Add(r.Id);

            _interaction.Tick(dt, Roll, ghostRoom, gx, gz, rooms);

            // 任务：在鬼房停留秒数（用玩家位置与鬼房比对）
            if (_tasks != null && _player != null && !string.IsNullOrEmpty(ghostRoom)
                && RoomIdAt(_player.X, _player.Z) == ghostRoom)
            {
                _ghostRoomDwellAcc += dt;
                if (_ghostRoomDwellAcc >= 1f) { _tasks.ReportGhostRoomDwell(1f); _ghostRoomDwellAcc -= 1f; }
            }
        }

        /// <summary>初始化等级/商店/任务/电力/互动（一次性；Boot 阶段调用）。</summary>`);
  log.push('  ✓ 每帧互动 + 任务进度');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
