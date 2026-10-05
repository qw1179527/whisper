using UnityEngine;
using UnityEngine.UI;
using Whisper.Core;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Hearing;
using Whisper.Gameplay.Items;
using Whisper.Gameplay.Level;
using Whisper.Gameplay.Monsters;
using Whisper.Gameplay.Session;

namespace Whisper.Runtime
{
    /// <summary>
    /// 怪物的场景呈现与驱动（V9 §7）—— **Unity 侧胶水层**。
    ///
    /// 分工：`MonsterDirector` 里的状态机/听觉/视线/移动全是纯逻辑（本机 154 条断言覆盖），
    /// 本类只做引擎相关的事：给三怪各建一个可见体、每帧把逻辑结果写进 Transform、
    /// 把玩家脚步刺激喂进听觉判定。
    ///
    /// 为什么这一环必须补：在它之前，`MonsterBrain` 有完整状态机、`Hearing` 有完整判定、
    /// 移植等价性向量也全绿 —— 但**没有任何东西实例化它们**，游戏里一只怪都没有。
    /// "逻辑完备却不在产品里"是本项目反复出现的失效模式（几何层、内容管线都栽过）。
    ///
    /// 体块与配色是**占位**：真美术资产接入后只换 Mesh，逻辑接线不动。
    /// 三怪各自配色便于肉眼区分（缝匠锈褐 / 低语者霉绿 / 收殓人血色）。
    /// </summary>
    public sealed partial class MonsterViews : MonoBehaviour
    {
        /// <summary>三怪占位体块尺寸（米）与配色 token。</summary>
        static readonly (string id, float w, float h, string color)[] Placeholders =
        {
            ("stitcher",  0.55f, 1.85f, DesignTokens.ColorRust),   // 教学怪：压力型
            ("whisperer", 0.50f, 1.70f, DesignTokens.ColorMold),   // 核心怪：语音猎手
            ("coroner",   0.65f, 2.05f, DesignTokens.ColorBlood),  // 终局怪：高压
        };

        MonsterDirector _director;
        PlayerController _player;
        LevelGeometry _geo;
        Text _hud;
        readonly System.Collections.Generic.Dictionary<string, Transform> _bodies =
            new System.Collections.Generic.Dictionary<string, Transform>(System.StringComparer.Ordinal);
        readonly System.Collections.Generic.Dictionary<string, Material> _mats =
            new System.Collections.Generic.Dictionary<string, Material>(System.StringComparer.Ordinal);

        long _tick;
        MonsterView[] _lastViews = System.Array.Empty<MonsterView>();
        long _lastStimulusTick = -1;

        /// <summary>最近一次步进的三怪状态（HUD/诊断）。</summary>
        public MonsterView[] LastViews => _lastViews;

        /// <summary>累计喂进去的刺激条数（证明听觉链路真的在跑）。</summary>
        public int StimuliEmitted { get; private set; }
        public int StimuliHeard { get; private set; }

        /// <summary>初始化：建三怪可见体，并把移动解析注入状态机。</summary>
        public void Initialize(LevelGeometry geo, LevelData level, PlayerController player, Text hud)
        {
            _geo = geo;
            _player = player;
            _hud = hud;

            var cfg = new GameConfigReader();
            var patrol = MonsterDirector.PatrolPointsFromLevel(geo, level);

            try
            {
                // startOffset=1：三怪分别从第 1/2/3 个巡逻点起步，不挤在一起
                _director = new MonsterDirector(cfg, geo, MonsterDirector.DefaultMonsterIds, patrol, 1);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Whisper] 怪物总控初始化失败：{ex.Message}");
                return;
            }

            for (int i = 0; i < _director.Brains.Count; i++) BuildBody(i, _director.Brains[i].Id);
            Debug.Log($"[Whisper] 怪物已实例化 {_director.Brains.Count} 只 · 巡逻点 {patrol.Count} 个 · 起始 "
                + string.Join(" / ", System.Linq.Enumerable.Select(_director.Brains, b => $"{b.Id}@({b.Position.X:0.0},{b.Position.Z:0.0})")));
        }

        void BuildBody(int index, string id)
        {
            var (_, w, h, colorHex) = Placeholders[index];

            // ── 优先用**真模型**：从鬼怪模型池里按匹配种子**确定性随机**取一个 ──
            // 用户规则（2026-10-05）：模型与鬼类型**完全解耦**，所有鬼都是完整人形，
            // 分男女建模、开局随机取一个；类型差异**只体现在机制上**。
            // 所以这里**不看 id（类型）**，只看 `_modelSeed + index` —— 这一点是刻意写成这样的。
            string model = GhostModelPool.Pick(_modelSeed, index);
            if (!string.IsNullOrEmpty(model))
            {
                GameObject body = null;
                try { body = ModelLibrary.InstantiateWhole(model, transform); }
                catch (System.Exception e) { Debug.LogWarning("[Whisper] 鬼模型降级：" + e.Message); }
                if (body != null)
                {
                    body.name = "Monster_" + id;
                    _bodies[id] = body.transform;
                    var firstMr = body.GetComponentInChildren<MeshRenderer>();
                    _mats[id] = firstMr != null ? firstMr.sharedMaterial : null;
                    _modelIds[id] = model;
                    return;
                }
            }

            // 降级：立方体占位（**明确可见**，不假装成功；HUD 会显示实际用的模型名）
            var go = new GameObject("Monster_" + id, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            var mf = go.GetComponent<MeshFilter>();
            mf.sharedMesh = CubeMesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = FlatMaterial(HexToColor(colorHex));
            go.transform.localScale = new Vector3(w, h, w);
            _bodies[id] = go.transform;
            _mats[id] = mr.sharedMaterial;
            _modelIds[id] = "(占位立方体)";
        }

        /// <summary>每只怪实际用的模型 id（自检/HUD 可核："模型与类型解耦"是否真的成立）。</summary>
        readonly System.Collections.Generic.Dictionary<string, string> _modelIds =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);
        /// <summary>本局的模型选择种子（来自匹配种子；确定性）。</summary>
        uint _modelSeed = 0x5EED_1234u;
        /// <summary>设置模型选择种子（组合根在开局时调用）。</summary>
        public void SetModelSeed(uint seed) { _modelSeed = seed; }
        /// <summary>HUD/自检用：每只怪用的模型。</summary>
        public string DescribeModels()
        {
            var sb = new System.Text.StringBuilder("模型池：");
            foreach (var kv in _modelIds) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            return sb.ToString().TrimEnd();
        }

        static Mesh _cube;
        static Mesh CubeMesh
        {
            get
            {
                if (_cube == null)
                {
                    var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _cube = temp.GetComponent<MeshFilter>().sharedMesh;
                    DestroyImmediate(temp);
                }
                return _cube;
            }
        }

        static readonly System.Collections.Generic.Dictionary<Color, Material> _matCache =
            new System.Collections.Generic.Dictionary<Color, Material>();

        static Material FlatMaterial(Color c)
        {
            if (_matCache.TryGetValue(c, out var m) && m != null) return m;
            // 复用与关卡几何同一个随包着色器（Resources 无条件进包）——
            // 黑屏事故的根因就是"按名字找内置着色器拿到了 null"，这里绝不能再走那条路。
            var shader = LevelBuilder.GeometryShader;
            if (shader == null) throw new System.InvalidOperationException("几何着色器缺失：怪物无法上色");
            m = new Material(shader) { color = c };
            _matCache[c] = m;
            return m;
        }

        static Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length < 6) return Color.white;
            return new Color32(
                System.Convert.ToByte(hex.Substring(0, 2), 16),
                System.Convert.ToByte(hex.Substring(2, 2), 16),
                System.Convert.ToByte(hex.Substring(4, 2), 16), 255);
        }

        void Update()
        {
            if (_director == null || _geo == null) return;

            _tick++;
            var playerPos = new Vec2(_player != null ? _player.Motion.X : 0f, _player != null ? _player.Motion.Z : 0f);

            EmitPlayerNoise(playerPos);
            _lastViews = _director.Tick(_tick, playerPos);
            ApplyViews(_lastViews);
            TickDoors();   // 鬼开关门（按 canOpenDoors 区分；见 MonsterViews.Doors.cs）
        }

        /// <summary>
        /// 把玩家这一帧的噪音变成刺激喂给怪物（**核心机制的接线**）。
        /// 玩家移动的纯逻辑 `PlayerMotion` 会按步幅产出脚步刺激 key，
        /// 这里补上"强度/半径"（来自配置的真源）与坐标，再交给听觉判定。
        /// </summary>
        void EmitPlayerNoise(Vec2 playerPos)
        {
            var step = _player.LastStep;
            if (step.FootstepStimulusKey == null) return;
            if (_player.Motion.DistanceTravelledM <= 0f) return;

            var cfg = new GameConfigReader();
            string key = step.FootstepStimulusKey;
            float intensity = cfg.Float($"stimulusSources.{key}.intensity", float.NaN);
            float radius = cfg.Float($"stimulusSources.{key}.radiusM", float.NaN);
            if (float.IsNaN(intensity) || float.IsNaN(radius)) return;

            var stim = new Stimulus(key, "footstep", intensity, radius, false, playerPos.X, playerPos.Z, _tick);
            _director.EmitStimulus(stim);
            StimuliEmitted++;
            StimuliHeard += _director.LastHeardCount;
            _lastStimulusTick = _tick;
        }

        void ApplyViews(MonsterView[] views)
        {
            for (int i = 0; i < views.Length; i++)
            {
                var v = views[i];
                if (!_bodies.TryGetValue(v.Id, out var t) || t == null) continue;
                // 方块原点在中心，抬高半个身高让它站在地面上
                var (_, _, h, _) = Placeholders[i];
                t.position = new Vector3(v.X, h * 0.5f, v.Z);
                // 追击时放大一点点作为"发现你了"的视觉反馈（真美术接入后换成动画）
                float scale = v.State == "chase" ? 1.15f : 1f;
                t.localScale = new Vector3(Placeholders[i].w * scale, h * scale, Placeholders[i].w * scale);
                // 追击时染红：一眼可辨（HUD 之外的第二重证据）
                if (_mats.TryGetValue(v.Id, out var mat) && mat != null)
                {
                    string hex = v.State == "chase" ? DesignTokens.ColorBlood : Placeholders[i].color;
                    mat.color = HexToColor(hex);
                }
            }
        }

        /// <summary>诊断用：把三怪状态拼进 HUD（证明它们真的在跑，而不是摆着不动）。</summary>
        public string Describe()
        {
            if (_director == null) return "怪物：未初始化";
            var sb = new System.Text.StringBuilder();
            sb.Append($"怪物 {_lastViews.Length} 只 · 听见/发出 {StimuliHeard}/{StimuliEmitted}");
            foreach (var v in _lastViews)
                sb.Append($"\n  {v.Label}({v.Id}) {v.State} ({v.X:0.0},{v.Z:0.0}) {v.SpeedMps:0.0}m/s"
                    + (v.SeesPlayer ? " 👁看见玩家" : ""));
            return sb.ToString();
        }
    }
}
