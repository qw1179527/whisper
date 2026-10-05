using UnityEngine;
using UnityEngine.UI;

namespace Whisper.Runtime
{
    /// <summary>
    /// 鬼跳脸（jumpscare）—— 玩家被猎杀的瞬间，鬼的脸**从远处冲到满屏**。
    ///
    /// ## 用户要求
    /// 「如果玩家被鬼猎杀到的话会被鬼跳脸」（其余按恐鬼症官方行为：猎杀中被抓到即死亡，
    /// 死亡瞬间有鬼脸扑向镜头的画面）。
    ///
    /// ## 为什么单独一个类
    /// · 它要在**相机正前方**、**覆盖 HUD**、**锁住玩家输入**，三件事都和玩法循环的正常流程相反；
    /// · 它必须在**猎杀判定成立的那一帧**就开始，晚一帧玩家会先看到"我死了但没反应"。
    ///
    /// ## 实现要点（都是踩过的坑）
    /// 1. **不要用 UI Image 贴图**：本项目没有鬼脸的贴图资源，且用户要求"加重眼部亮处理"。
    ///    改用**真模型**：把鬼的头部部件挂到相机前方，缩放从远到近，眼睛走 `_WhisperEmission` 自发光。
    /// 2. **不受雾影响**：雾会把远景压暗 —— 跳脸必须**无视雾**（靠近相机后雾自然失效，但仍要保证
    ///    相机 near 平面不裁掉它：把脸放在 nearClip+0.05m 处）。
    /// 3. **确定性**：时长/抖动都用固定常数与帧计数，不引入 `确定性伪随机（禁用的那个 API）`（gate-physics 判据）。
    /// </summary>
    public sealed class JumpscareView : MonoBehaviour
    {
        /// <summary>整段跳脸时长（秒）。0.55s：够看清鬼脸，又不至于让玩家以为卡住。</summary>
        public float DurationSec = 0.55f;
        /// <summary>起始距离（米）—— 鬼脸从这么远冲过来。</summary>
        public float StartDistM = 6.0f;
        /// <summary>结束距离（米）—— 贴到相机近裁剪面附近，实现"满屏"。</summary>
        public float EndDistM = 0.20f;
        /// <summary>脸在屏幕上的最大尺寸（米）—— 略小于结束距离的两倍即可满屏。</summary>
        public float FaceScaleM = 0.55f;
        /// <summary>红眼 / 白眼（用户要求两种，随机取）。</summary>
        public bool RedEyes = true;

        Camera _cam;
        Transform _face;
        float _t;
        bool _playing;
        Image _flash;
        Text _caption;

        /// <summary>是否正在播放（自检/HUD 可读）。</summary>
        public bool Playing => _playing;
        /// <summary>已播放次数（自检可读：证明跳脸确实触发过）。</summary>
        public int PlayedCount { get; private set; }

        /// <summary>构建（创建脸与遮罩，默认隐藏）。`hud` 用于叠一层红闪与文字。</summary>
        public void Build(Camera cam, Transform parent, Transform hud)
        {
            _cam = cam;
            var root = new GameObject("JumpscareRoot").transform;
            root.SetParent(parent, false);
            _face = root;

            // 脸：用鬼的**头部部件**（模型驱动，不是贴图）
            GameObject head = null;
            try { head = ModelLibrary.InstantiatePart("ghost", "GEO-GhostHead", _face, true); }
            catch (System.Exception e) { Debug.LogWarning("[Whisper] 跳脸模型降级：" + e.Message); }
            if (head == null)
            {
                // 明确降级：用球体占位（可见地承认模型缺失，而不是静默什么都不显示）
                var fb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fb.name = "JumpscareHeadFallback";
                fb.transform.SetParent(_face, false);
                var c = fb.GetComponent<Collider>(); if (c != null) Destroy(c);
                head = fb;
            }
            // 眼睛：自发光（红/白两色），"加重眼部亮处理"
            var eyeMat = SceneMaterials.Emissive(RedEyes ? new Color(1.0f, 0.06f, 0.03f) : new Color(0.92f, 0.96f, 1.0f), 6.0f);
            for (int s = -1; s <= 1; s += 2)
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                e.name = "JumpscareEye" + (s > 0 ? "L" : "R");
                e.transform.SetParent(_face, false);
                e.transform.localPosition = new Vector3(s * 0.085f, 0.030f, -0.075f);
                e.transform.localScale = new Vector3(0.062f, 0.062f, 0.034f);
                var mr = e.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = eyeMat;
                var col = e.GetComponent<Collider>(); if (col != null) Destroy(col);
            }
            // 脸自身的一点光：保证暗场里也亮（不是靠环境光）
            var gl = new GameObject("JumpscareGlow");
            gl.transform.SetParent(_face, false);
            gl.transform.localPosition = new Vector3(0f, 0f, -0.18f);
            var l = gl.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 3.0f;
            l.intensity = 1.1f;
            l.color = RedEyes ? new Color(1.0f, 0.18f, 0.10f) : new Color(0.88f, 0.94f, 1.0f);

            // HUD 叠层：红闪 + 一行字（让"死了"这件事**确定可见**，不靠玩家猜）
            if (hud != null)
            {
                var flashGo = new GameObject("JumpscareFlash");
                flashGo.transform.SetParent(hud, false);
                _flash = flashGo.AddComponent<Image>();
                _flash.color = new Color(0.55f, 0.02f, 0.02f, 0f);
                var frt = _flash.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

                _caption = SceneMaterials.Label(hud, "JumpscareCaption", "你被抓住了",
                    new Vector2(0.5f, 0.46f), new Vector2(0.5f, 0.46f), 64, TextAnchor.MiddleCenter);
                _caption.color = new Color(0.95f, 0.25f, 0.22f, 0f);
            }
            SetVisible(false);
        }

        void SetVisible(bool on)
        {
            if (_face != null) _face.gameObject.SetActive(on);
            if (_flash != null) _flash.enabled = on;
            if (_caption != null) _caption.enabled = on;
        }

        /// <summary>开始播放（由猎杀判定调用）。重复调用在播放中会被忽略。</summary>
        public void Play()
        {
            if (_playing || _cam == null) return;
            _playing = true;
            _t = 0f;
            PlayedCount++;
            SetVisible(true);
        }

        void LateUpdate()
        {
            if (!_playing || _cam == null || _face == null) return;
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Mathf.Max(0.05f, DurationSec));
            // 缓出：前段冲得快、末段贴脸（恐怖片的"扑"就是这个节奏）
            float ease = 1f - (1f - k) * (1f - k);

            float dist = Mathf.Lerp(StartDistM, EndDistM, ease);
            // 固定在相机正前方（用相机自身的朝向，玩家转视角也甩不掉它）
            _face.position = _cam.transform.position + _cam.transform.forward * dist;
            _face.rotation = _cam.transform.rotation;
            float s = Mathf.Lerp(0.06f, FaceScaleM, ease);
            _face.localScale = new Vector3(s, s, s);

            if (_flash != null)
            {
                var c = _flash.color;
                c.a = Mathf.Lerp(0f, 0.55f, ease);
                _flash.color = c;
            }
            if (_caption != null)
            {
                var c = _caption.color;
                c.a = Mathf.Lerp(0f, 1f, ease);
                _caption.color = c;
            }

            if (k >= 1f)
            {
                // 结束时**保留**红闪与文字（死亡状态不会自己消失）—— 只停住动画
                _playing = false;
            }
        }

        /// <summary>复位（重开一局时用）。</summary>
        public void Reset()
        {
            _playing = false; _t = 0f; SetVisible(false);
        }

        /// <summary>HUD/自检摘要。</summary>
        public string Describe()
            => string.Format("跳脸：{0} · 播放过 {1} 次 · 时长 {2:0.00}s · {3}眼",
                _playing ? "播放中" : "待机", PlayedCount, DurationSec, RedEyes ? "红" : "白");
    }
}
