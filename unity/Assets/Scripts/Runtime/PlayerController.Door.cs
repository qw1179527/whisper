using UnityEngine;
using UnityEngine.UI;
using Whisper.Core;
using Whisper.Gameplay.Level;

namespace Whisper.Runtime
{
    /// <summary>
    /// 门交互 = **点门本身**（`PlayerController` 的 partial 拆分）。
    ///
    /// ## 为什么从"按钮"改成"点门"
    /// 用户明确要求：「将门互动改为点击门及开关」。按钮版虽然不与移动/视角抢触摸，
    /// 但它把"门"这一空间物体抽象成了屏幕角落的一个方块 —— 玩家得先找按钮、再看是开还是关。
    /// 点门本身更符合直觉，也让"门在哪"这件事由场景回答而不是 UI。
    ///
    /// ## 判定：先射线打门扇，打不中再退化到"抬手指最近的门"
    /// - **主判据**：从相机沿手指做射线，命中 `LevelBuilder.DoorObjects` 里的门扇 → 就是它。
    ///   这是"点哪个开哪个"，最直观。
    /// - **兜底**：手机上手抖/门扇很薄，射线可能擦过去。此时若**抬手指附近**有一扇
    ///   2.2m 内、且在视线方向的门（屏幕距离 &lt; `TapSlopPx`），也算点中。
    ///   没有兜底会出现"明明点了门却没反应"——那是最容易劝退的手感问题。
    ///
    /// ## 触摸归属
    /// 点门**不抢**移动/视角的触摸：只在"没有手指被认领为移动或视角"且
    /// "本帧这次抬起与按下之间位移小于 `TapSlopPx`"（即真的是一次点击而不是拖拽）时才判定。
    /// 这样"右半屏拖视角"与"点门"不会互相干扰。
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>到**门洞矩形最近距离** ≤ 此值才算可达（与几何层 `TryFindInteractableDoor` 同口径）。</summary>
        const float DoorInteractRadiusM = 2.2f;
        /// <summary>点击容差：按下与抬起之间位移小于此值才算"点击"（否则是拖拽）。</summary>
        const float TapSlopPx = 24f;
        /// <summary>兜底判据：门中心投影到屏幕后，离抬手指多近才算点中。</summary>
        const float DoorScreenSlopPx = 220f;

        LevelBuilder _levelBuilder;
        LevelGeometry.DoorInfo? _nearDoor;
        Vector2 _doorAimScreen;
        bool _doorAimValid;
        int _tapTouchId = -1;
        Vector2 _tapStart;
        float _doorNextScanTime;

        /// <summary>是否在门交互半径内（HUD 提示用）。</summary>
        public bool NearDoor => _nearDoor.HasValue;

        /// <summary>注入 LevelBuilder（门扇对象列表用于射线判据）。</summary>
        public void BindLevelBuilder(LevelBuilder b) => _levelBuilder = b;

        /// <summary>节流扫描附近的门（0.1s 一次）。只用于 HUD 提示与兜底判定，不影响主射线判据。</summary>
        void ScanNearDoor()
        {
            if (_geo == null || _motion == null || _camera == null) return;
            if (Time.unscaledTime < _doorNextScanTime) return;
            _doorNextScanTime = Time.unscaledTime + 0.1f;

            LevelGeometry.DoorInfo info;
            if (_geo.TryFindInteractableDoor(_motion.X, _motion.Z, DoorInteractRadiusM, out info))
            {
                _nearDoor = info;
                var sp = _camera.WorldToScreenPoint(new Vector3(info.X, info.Y + 1.0f, info.Z));
                // z < 0 表示门在相机背后 → 投影无意义，标记为不可用
                _doorAimValid = sp.z > 0f;
                if (_doorAimValid) _doorAimScreen = new Vector2(sp.x, sp.y);
            }
            else
            {
                _nearDoor = null;
                _doorAimValid = false;
            }
        }

        /// <summary>
        /// 每次触摸抬起时调用（由 `ClaimTouches` 转发）。
        /// 只有"本次按下→抬起位移 &lt; TapSlopPx"（真点击）才判门，避免拖视角时误开门。
        /// </summary>
        void OnTouchReleasedForDoor(int fingerId, Vector2 endPos)
        {
            if (fingerId != _tapTouchId) return;
            _tapTouchId = -1;
            if ((endPos - _tapStart).magnitude > TapSlopPx) return;   // 是拖拽，不是点击
            if (!_nearDoor.HasValue) return;                          // 附近没门

            // ① 主判据：射线打门扇（"点哪个开哪个"）
            if (TryRaycastDoor(endPos, out string rayKey)) { ToggleAndReport(rayKey); return; }

            // ② 兜底：门在视线内且离抬手指够近 → 也算点中
            if (_doorAimValid && (endPos - _doorAimScreen).magnitude <= DoorScreenSlopPx)
                ToggleAndReport(_nearDoor.Value.Key);
        }

        /// <summary>从相机沿屏幕点做射线，命中门扇则返回它的门键。</summary>
        bool TryRaycastDoor(Vector2 screenPos, out string doorKey)
        {
            doorKey = null;
            if (_camera == null || _geo == null || _levelBuilder == null) return false;
            // 相机 API 收 Vector3（z 为到近裁剪面的距离；屏幕射线用 0 即可）
            var ray = _camera.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, DoorInteractRadiusM + 2f)) return false;

            // 命中对象必须是某扇门扇的（或它的子物体）——用"往上找父级"的写法，
            // 因为门扇有合页枢轴/门楣等子物，命中点可能在子物上。
            var t = hit.transform;
            while (t != null)
            {
                for (int i = 0; i < _levelBuilder.DoorObjects.Count; i++)
                {
                    var go = _levelBuilder.DoorObjects[i];
                    if (go == null) continue;
                    if (go.transform == t)
                    {
                        // 门扇对象名形如 "Door_<key>"；键也可从几何层按位置反查，这里用名字后缀最直接
                        doorKey = KeyFromDoorObjectName(go.name);
                        return !string.IsNullOrEmpty(doorKey);
                    }
                }
                t = t.parent;   // Transform.parent 是属性（Unity 桩里可能缺声明，真 Unity 有）
            }
            return false;
        }

        /// <summary>从门扇对象名取门键。build-render 的命名若变化，这里退化为"用最近门洞的键"。</summary>
        string KeyFromDoorObjectName(string objName)
        {
            const string prefix = "Door_";
            if (!string.IsNullOrEmpty(objName) && objName.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                string k = objName.Substring(prefix.Length);
                if (_geo != null && _geo.DoorCount > 0) return k;
            }
            // 名字不符合约定 → 用附近门洞的规范键（几何层已归一化，能直接喂 ToggleDoor）
            return _nearDoor.HasValue ? _nearDoor.Value.Key : null;
        }

        void ToggleAndReport(string key)
        {
            if (_geo == null || string.IsNullOrEmpty(key)) return;
            bool ok = _geo.ToggleDoor(key);
            _doorNextScanTime = 0f;     // 立刻复扫，让 HUD 提示与新状态同步
            if (ok)
                Debug.Log($"[Whisper] 门 {key} → {( _geo.IsDoorOpen(key) ? "开" : "关")}");
            else if (!string.IsNullOrEmpty(_geo.LastDoorMessage))
                Debug.Log($"[Whisper] 门交互被拒：{_geo.LastDoorMessage}");
        }

        /// <summary>HUD 用的门提示（玩家行尾）。</summary>
        string DoorHint()
        {
            if (!_nearDoor.HasValue) return "";
            return $" · 门 {_nearDoor.Value.DisplayName} [{( _nearDoor.Value.Open ? "开" : "关")}] 点门开合";
        }
    }
}
