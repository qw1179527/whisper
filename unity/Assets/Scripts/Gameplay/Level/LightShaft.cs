using System.Collections.Generic;
using UnityEngine;

namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// **体积光光柱**：给聚光灯挂一个贴合光锥的锥体网格，用附加混合画出光柱。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 为什么是"锥体网格"而不是屏幕空间体积光
    /// ══════════════════════════════════════════════════════════════════════════════
    /// `data/config.json` 一直把 `volumetricLight` 关着，原文理由是
    /// "在 OnRenderImage 的 Blit 链里拿不到深度/读回数据（真机 raw=0.00），开启会黑屏"
    /// —— 屏幕空间方案在本工程**已被实测证明会黑屏**。
    /// 而"体积光"在恐怖游戏里的观感主要来自**光柱**，光柱有一个**不需要深度**的经典做法：
    /// 锥体网格 + 附加混合。观感随视角变化（因为它就是几何体），且不碰 Blit 链。
    ///
    /// ⚠ **如实界定**：这是**几何光柱**（light shaft），**不是**参与介质散射的积分。
    ///   在文档与判据里都按"。光柱"描述，不冒充"物理体积散射"。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 本机可验证（这是选它的另一个理由）
    /// ══════════════════════════════════════════════════════════════════════════════
    /// 上一轮实测：CI 的软件 GL **不支持多采样 RT**（MSAA 验不了）。
    /// 而本方案只用"不透明几何 + 附加混合"，不依赖深度/多采样/读回
    /// ⇒ 在软件 GL 下**同样能出像素证据**，不必等真机。
    /// </summary>
    public sealed class LightShaft : MonoBehaviour
    {
        /// <summary>光柱网格的段数（横向）。8 段在近处看得出棱角 ⇒ 取 16。</summary>
        const int RadialSegments = 16;
        /// <summary>沿轴向的分段数。分段让"轴向衰减"是逐顶点插值，1 段就够（衰减是线性的）。</summary>
        const int LengthSegments = 1;

        /// <summary>由 <see cref="Attach"/> 建出来的光柱数（取证与诊断读数）。</summary>
        public static int Built { get; private set; }

        /// <summary>光柱是否启用（全局开关；由 RenderTierApplier 按配置设）。</summary>
        public static bool Enabled = true;

        /// <summary>本工程唯一的光柱材质（所有光柱共用；改一处全体生效）。</summary>
        static Material _mat;
        static readonly List<LightShaft> _all = new List<LightShaft>();

        Light _light;
        MeshRenderer _mr;
        float _coneAngle = 60f, _range = 12f;

        /// <summary>
        /// 给一盏**聚光灯**挂光柱。非聚光灯直接跳过（点光/平行光没有"锥"）。
        ///
        /// 返回值：true = 建了光柱（调用方可计数）。
        /// </summary>
        public static bool Attach(Light spot)
        {
            if (spot == null || spot.type != LightType.Spot) return false;
            if (!Enabled) return false;
            var go = new GameObject("LightShaft");
            go.transform.SetParent(spot.transform, false);
            var shaft = go.AddComponent<LightShaft>();
            shaft.Build(spot);
            _all.Add(shaft);
            Built++;
            return true;
        }

        /// <summary>把全部光柱开关（取证 ON/OFF 用；产品由配置驱动）。</summary>
        public static void SetAllEnabled(bool on)
        {
            Enabled = on;
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null && _all[i]._mr != null) _all[i]._mr.enabled = on;
        }

        /// <summary>清空（换关卡时调用，避免上一个关卡的光柱残留 —— 本项目踩过残留的坑）。</summary>
        public static void Clear()
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null) DestroyImmediate(_all[i].gameObject);
            _all.Clear();
            Built = 0;
        }

        void Build(Light spot)
        {
            _light = spot;
            _coneAngle = spot.spotAngle;
            _range = spot.range;
            if (_mat == null) _mat = new Material(Shader.Find("Whisper/LightShaft"));
            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildConeMesh(_coneAngle, _range);
            _mr = gameObject.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = _mat;
            // 光柱**不投影也不接收阴影**：它是发光体，且参与阴影会自遮蔽（本项目 ShadowCaster 踩过）
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            // 颜色跟着灯走：暖光柱配暖灯，冷光柱配冷灯（否则观感"灯和柱子不是一回事"）
            var c = spot.color;
            _mat.SetColor("_Color", new Color(c.r, c.g, c.b, 1f));
            _mat.SetFloat("_Intensity", Mathf.Clamp(spot.intensity * 0.22f, 0.05f, 1.2f));
        }

        void LateUpdate()
        {
            if (_light == null) return;
            // 灯的锥角/距离可在运行时改（手电筒若变焦）⇒ 跟随重建。
            // 用"差异阈值"而不是每帧重建：重建网格会持续产生 GC，移动端代价明显。
            if (Mathf.Abs(_light.spotAngle - _coneAngle) > 0.5f || Mathf.Abs(_light.range - _range) > 0.1f)
            {
                _coneAngle = _light.spotAngle; _range = _light.range;
                GetComponent<MeshFilter>().sharedMesh = BuildConeMesh(_coneAngle, _range);
            }
        }

        /// <summary>
        /// 建一个**开口锥体**（不是封闭圆锥）：顶点在光源处，底面在 `range` 处。
        ///
        /// 关键几何约定（写清楚，避免以后接反）：
        /// · 轴向沿 **+Z**（Unity 光照方向也沿 +Z，灯在原点朝前照）
        /// · `uv.y` = 0 在光源端、1 在远端（着色器的轴向衰减按这个来）
        /// · 顶点处半径**不为 0**：取一个极小值，否则顶点的法线退化 → 菲涅尔项出现 NaN 黑斑
        /// </summary>
        static Mesh BuildConeMesh(float spotAngleDeg, float range)
        {
            float halfRad = spotAngleDeg * 0.5f * Mathf.Deg2Rad;
            float rEnd = Mathf.Tan(halfRad) * range;
            const float rTip = 0.004f;              // 见上：不为 0
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int i = 0; i <= LengthSegments; i++)
            {
                float t = (float)i / LengthSegments;
                float z = t * range;
                float r = Mathf.Lerp(rTip, rEnd, t);
                for (int s = 0; s <= RadialSegments; s++)
                {
                    float a = (float)s / RadialSegments * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z));
                    uvs.Add(new Vector2((float)s / RadialSegments, t));
                }
            }
            int stride = RadialSegments + 1;
            for (int i = 0; i < LengthSegments; i++)
                for (int s = 0; s < RadialSegments; s++)
                {
                    int a = i * stride + s, b = a + 1, c = a + stride, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }

            var m = new Mesh { name = "LightShaftCone" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();     // 菲涅尔项要法线；`Cull Off` ⇒ 内外都要
            m.RecalculateBounds();
            return m;
        }
    }
}
