using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// 玩家身体（第一人称"能看到自己"）。
    ///
    /// ## 为什么需要
    /// 之前玩家**只有相机、没有身体** —— 主界面看不到人、联机时别人看不到你、
    /// 低头看不到自己的手。用户明确要求"要给角色建模了"。
    ///
    /// ## 第一人称下要藏掉头与躯干（否则相机在头里面，满屏是头的内壁）
    /// 只在**第一人称**藏：留 `GEO-PlayerArmL/R`（低头能看见手臂）、`GEO-PlayerLegL/R`（低头看腿）。
    /// 主界面/联机第三人称时 `SetFirstPerson(false)` 把全部部件打开。
    ///
    /// ## 坐标标定（不要手改）
    /// **眼高 1.70m** 是单一真源（`PlayerController.EyeHeightM`），而模型头顶 1.831m、眼在 1.74m。
    /// 所以身体不能简单挂在原点 —— 要以"相机高度"为基准摆放，见 `ApplyEyeAlignment()`。
    /// </summary>
    public sealed class PlayerBody : MonoBehaviour
    {
        /// <summary>第一人称是否隐藏头部（默认隐藏；主界面/联机要打开）。</summary>
        public bool HideHeadInFirstPerson = true;
        /// <summary>第一人称是否隐藏躯干（默认隐藏；否则相机在胸腔里）。</summary>
        public bool HideTorsoInFirstPerson = true;
        /// <summary>第一人称是否隐藏颈（与头一起藏，否则会看到一截悬空的脖子）。</summary>
        public bool HideNeckInFirstPerson = true;

        GameObject _root;
        bool _firstPerson = true;
        /// <summary>身体网格是否加载成功（HUD/日志可核）。</summary>
        public bool BodyLoaded => _root != null;
        /// <summary>实际创建的部件数（0 表示模型没加载上）。</summary>
        public int PartCount { get; private set; }

        /// <summary>
        /// 构建身体。`parent` 通常是 Player 根对象（`PlayerController` 所在的那个），
        /// **不是相机** —— 相机每帧被 `ApplyToTransform` 覆写旋转，挂上去会跟着转，身体就"贴脸转"了。
        /// </summary>
        public void Build(Transform parent)
        {
            if (_root != null) return;
            _root = ModelLibrary.InstantiateWhole("player", parent);
            if (_root == null)
            {
                Debug.LogWarning($"[Whisper] 玩家身体加载失败：{ModelLibrary.LastProblem}");
                return;
            }
            PartCount = _root.transform.childCount;
            ApplyEyeAlignment();
            SetFirstPerson(_firstPerson);
        }

        /// <summary>
        /// 把身体摆到"眼高 = PlayerController.EyeHeightM"的位置。
        /// 模型的**眼在 1.74m**（Blender 里定的），而本作眼高是 **1.70m** → 差 0.04m，
        /// 所以整体下移 0.04m。**不要用缩放去凑**：缩放会连带改变肩宽/臂长，破坏"按视角判断大小"。
        /// </summary>
        void ApplyEyeAlignment()
        {
            if (_root == null) return;
            const float ModelEyeZ = 1.74f;                    // Blender 里的眼高（见 ModelLibrary 的标定注释）
            float delta = PlayerController.EyeHeightM - ModelEyeZ;
            var lp = _root.transform.localPosition;
            _root.transform.localPosition = new Vector3(lp.x, lp.y + delta, lp.z);
        }

        /// <summary>切换第一/第三人称可见性（主界面与联机用第三人称）。</summary>
        public void SetFirstPerson(bool on)
        {
            _firstPerson = on;
            if (_root == null) return;
            foreach (Transform child in _root.transform)
            {
                string n = child.name;
                bool hide = on && (
                    (HideHeadInFirstPerson && n.IndexOf("Head", System.StringComparison.Ordinal) >= 0) ||
                    (HideNeckInFirstPerson && n.IndexOf("Neck", System.StringComparison.Ordinal) >= 0) ||
                    (HideTorsoInFirstPerson && n.IndexOf("Torso", System.StringComparison.Ordinal) >= 0));
                var mr = child.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = !hide;
            }
        }

        /// <summary>HUD 摘要。</summary>
        public string Describe()
            => _root == null
                ? $"身体：未加载（{ModelLibrary.LastProblem ?? "未知原因"}）"
                : $"身体：{PartCount} 部件 · {( _firstPerson ? "第一人称" : "第三人称")} · 眼高对齐 {PlayerController.EyeHeightM:0.00}m";
    }
}
