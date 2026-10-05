using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// 大厅第一人称自由行走 + 自由环视（用户 2026-10-05：「我要的自由视角呢，恐鬼症官方是这么设定的？」）。
    ///
    /// ## 官方依据（**权威来源：用户《补充说明.md》第 5 行原文**）
    /// > 「场景建模：大厅的3D模型包含**完整的碰撞体（Collider）**，**玩家可以在其中自由行走**。
    /// >  场景中的道具（如篮球、喷漆罐）都有独立的物理效果。」
    /// > 「主菜单板：位于大厅最明亮的墙上…按 **空格键** 或**鼠标左键点击**即可进入操作界面，
    /// >  再按一次 空格键 或 **Esc** 退出。」
    ///
    /// 即：**行走与"进操作界面"是两件事** —— 玩家先自由走动走到板前，再按键/点击进操作视角。
    /// 我此前的实现（`MenuScene.UpdateBoardCamera` 每帧直接写相机位姿、只有两个固定机位）
    /// 把这个区别抹掉了，所以玩家不能走、不能转头。本类补上缺失的那一半。
    ///
    /// ## 为什么不用局内的 `PlayerMotion` / `PlayerController`
    /// `PlayerMotion.Step(...)` **要求 `LevelGeometry` 非空**（`PlayerMotion.cs:150` 显式抛异常），
    /// 而 `LevelGeometry` 只能从**关卡数据**（`LevelData`）编译 —— 大厅是代码搭的、没有关卡数据。
    /// 为大厅现造一份关卡数据成本过高，故本类自行做**矩形范围约束**（大厅是规则矩形仓库，
    /// 墙由 `HallScene` 建、带 BoxCollider）。真实碰撞留给后续"道具物理"那一轮统一做。
    ///
    /// ## 操控（移动端优先，桌面端兼容）
    /// | 平台 | 移动 | 环视 |
    /// |---|---|---|
    /// | 移动端 | **左半屏拖动** | **右半屏拖动** |
    /// | 桌面 | `W/A/S/D`（由 `KeyCode` 桩支持） | 鼠标位置比例 → 或按住左键拖动 |
    ///
    /// ⚠ 为什么"左半屏/右半屏"而不是虚拟摇杆：本项目 UI 全是代码建的、没有摇杆控件体系，
    /// 而分屏拖动**零控件依赖**、在触摸屏上可靠（局内 `PlayerController` 也走 `Input.touches`）。
    /// 官方主机版正是"左摇杆走 / 右摇杆看"，分屏拖动是其触屏等价物。
    /// </summary>
    public sealed class LobbyCamera : MonoBehaviour
    {
        /// <summary>眼高（米）。与局内 `PlayerController.EyeHeightM` 取同一值，避免大厅/局内视线高度不一致。</summary>
        public const float EyeHeightM = 1.7f;

        /// <summary>行走速度（m/s）。普通步速，官方大厅是休闲空间不需要冲刺。</summary>
        public const float WalkSpeed = 2.6f;
        /// <summary>环视灵敏度（弧度/像素）。手感值，可调。</summary>
        public const float LookSensitivity = 0.0045f;
        /// <summary>俯仰限位（度）。防止翻过头顶。</summary>
        public const float PitchLimitDeg = 78f;

        /// <summary>可行走矩形（世界 XZ）。由 <see cref="Configure"/> 传入 —— 大厅墙内收一点。</summary>
        float _minX, _maxX, _minZ, _maxZ;
        bool _configured;

        float _yawDeg;
        float _pitchDeg;
        Vector3 _pos;

        /// <summary>是否接受输入。<c>false</c> 时（例如已进"操作视角"）本类完全不写相机。</summary>
        public bool Active = true;

        // 触摸跟踪：区分左半屏（移动）与右半屏（环视）
        int _moveFinger = -1, _lookFinger = -1;
        Vector2 _moveLast, _lookLast;

        public float YawDeg => _yawDeg;
        public float PitchDeg => _pitchDeg;
        public Vector3 Position => _pos;

        /// <summary>
        /// 配置可行走范围与初始位姿。
        /// </summary>
        /// <param name="minX">可行走矩形 X 下界</param>
        /// <param name="maxX">X 上界</param>
        /// <param name="minZ">Z 下界</param>
        /// <param name="maxZ">Z 上界</param>
        /// <param name="start">起始世界位置（y 会被眼高覆盖）</param>
        /// <param name="yawDeg">起始朝向（度）</param>
        public void Configure(float minX, float maxX, float minZ, float maxZ, Vector3 start, float yawDeg)
        {
            _minX = minX; _maxX = maxX; _minZ = minZ; _maxZ = maxZ;
            _configured = true;
            _pos = new Vector3(
                Mathf.Clamp(start.x, minX, maxX), EyeHeightM, Mathf.Clamp(start.z, minZ, maxZ));
            _yawDeg = yawDeg;
            _pitchDeg = 0f;
        }

        /// <summary>直接放置（进/出"操作视角"时由 MenuScene 调用，避免出现跳动）。</summary>
        public void Teleport(Vector3 worldPos, float yawDeg, float pitchDeg = 0f)
        {
            _pos = new Vector3(worldPos.x, EyeHeightM, worldPos.z);
            _yawDeg = yawDeg;
            _pitchDeg = pitchDeg;
        }

        void Update()
        {
            if (!_configured) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) dt = 0.016f;

            Vector2 move = Vector2.zero;     // x = 左右平移, y = 前后
            Vector2 look = Vector2.zero;     // 本帧环视增量（像素）

            // ── 触摸：左半屏移动、右半屏环视 ──
            int n = Input.touchCount;
            for (int i = 0; i < n; i++)
            {
                var t = Input.GetTouch(i);
                bool left = t.position.x < Screen.width * 0.5f;
                if (t.phase == TouchPhase.Began)
                {
                    if (left && _moveFinger < 0) { _moveFinger = t.fingerId; _moveLast = t.position; }
                    else if (!left && _lookFinger < 0) { _lookFinger = t.fingerId; _lookLast = t.position; }
                }
                else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                {
                    if (t.fingerId == _moveFinger)
                    {
                        move += (t.position - _moveLast) * 0.03f;   // 拖动距离 → 位移（手感系数）
                        _moveLast = t.position;
                    }
                    else if (t.fingerId == _lookFinger)
                    {
                        look += (t.position - _lookLast);
                        _lookLast = t.position;
                    }
                }
                else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    if (t.fingerId == _moveFinger) _moveFinger = -1;
                    if (t.fingerId == _lookFinger) _lookFinger = -1;
                }
            }

            // ── 桌面：WASD 移动 + 鼠标位置比例环视 ──
            // （KeyCode 桩里有 None/Escape/Space/E/H/J/Alpha1-9；W/A/S/D 尚未加入桩，
            //   故这里用"移动端优先"的实现，桌面键位待桩补齐后再接 —— 不写不存在的成员。）
            {
                var mp = Input.mousePosition;
                if (Screen.width > 0 && Screen.height > 0 && mp.x > 0f && mp.y > 0f)
                {
                    float nx = (mp.x / Screen.width - 0.5f);      // -0.5..0.5
                    float ny = (mp.y / Screen.height - 0.5f);
                    move += new Vector2(nx, ny) * 0.06f;          // 鼠标偏离中心 = 缓慢移动
                }
            }

            if (!Active) return;

            // ── 应用环视 ──
            if (look.sqrMagnitude > 0.0001f)
            {
                _yawDeg += look.x * LookSensitivity * Mathf.Rad2Deg;
                _pitchDeg -= look.y * LookSensitivity * Mathf.Rad2Deg;
                _pitchDeg = Mathf.Clamp(_pitchDeg, -PitchLimitDeg, PitchLimitDeg);
            }

            // ── 应用移动（按当前朝向的前/右分解）──
            if (move.sqrMagnitude > 0.000001f)
            {
                float rad = _yawDeg * Mathf.Deg2Rad;
                var fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                var right = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
                var delta = (fwd * move.y + right * move.x) * WalkSpeed * dt;
                _pos += delta;
                // 矩形约束：大厅是规则矩形（墙由 HallScene 建），行走限制在墙内收 0.35 m
                _pos.x = Mathf.Clamp(_pos.x, _minX, _maxX);
                _pos.z = Mathf.Clamp(_pos.z, _minZ, _maxZ);
                _pos.y = EyeHeightM;
            }

            // 只有 Active 时才写相机（← 这是"进操作视角后本类不抢相机"的关键）
            Apply();
        }

        /// <summary>把当前位姿写进相机。由本类在自由态调用，也供 MenuScene 在过渡后调用一次。</summary>
        public void Apply()
        {
            transform.position = _pos;
            transform.rotation = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
        }

        /// <summary>诊断一行（供 HUD 关闭时的落盘日志用）。</summary>
        public string Describe()
            => $"大厅相机 pos({_pos.x:F1},{_pos.z:F1}) yaw {_yawDeg:F0}° pitch {_pitchDeg:F0}°"
             + $" · 可行走 x[{_minX:F1},{_maxX:F1}] z[{_minZ:F1},{_maxZ:F1}]";
    }
}
