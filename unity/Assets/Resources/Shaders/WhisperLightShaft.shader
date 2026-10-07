// Project Whisper · **体积光光柱**着色器（URP 17 · 附加混合 · 不依赖深度）
//
// ═══════════════════════════════════════════════════════════════════════════════
// 为什么走"发光锥体网格"而不是屏幕空间体积光
// ═══════════════════════════════════════════════════════════════════════════════
// `data/config.json` 里 `volumetricLight` 一直被关，原文理由（永久约束台账）是：
//   "它们在 OnRenderImage 的 Blit 链里拿不到深度/读回数据（真机 raw=0.00），开启会黑屏。"
// ⇒ 屏幕空间方案在本工程**已被实测证明会黑屏**，不能再走那条路。
//
// 但"体积光"在恐怖游戏里的实际观感来自**光柱**（god ray / light shaft），
// 而光柱有一个**不需要深度**的经典做法：**一个贴合光锥的锥体网格 + 附加混合**。
//   · 观感随视角变化（真 3D 体积感，因为它就是几何体）
//   · 不需要深度纹理、不需要读回、不需要 Blit
//   · 天然与 URP 的 Forward 路径兼容
// ⇒ 本文件就是这条路。**它是"体积光"的一种实现，不是近似替代品**；
//   但要在文档里如实写明：这是**几何光柱**，不是参与介质散射的积分。
//
// ═══════════════════════════════════════════════════════════════════════════════
// 三个必须遵守的纪律（每条对应本工程的一次事故）
// ═══════════════════════════════════════════════════════════════════════════════
// ① **属性名用 `_Color`**：全项目（LevelPalette / ModelLibrary / EvidencePlacer）
//    都按这个名字上色，改名 = 全项目失色。
// ② **每个 pass 各自声明它用到的每个 uniform**：多 pass 漏一个 →
//    `undeclared identifier` → 整个着色器编译失败 → 真机整屏品红（0.1.19 事故）。
// ③ **不用 `multi_compile_fog`**：内置雾依赖 Lighting 全局量，历史事故是把整个 3D 视图
//    洗成均匀 #D8CFBB。本文件**不参与雾**（光柱本身是发光体，被雾吃反而错）。
Shader "Whisper/LightShaft"
{
    Properties
    {
        _Color ("光柱颜色（乘 _Intensity）", Color) = (1, 0.95, 0.82, 1)
        _Intensity ("强度", Range(0, 4)) = 0.55
        _TipFade ("顶端衰减（0=不衰减 1=到顶端全透明）", Range(0, 1)) = 0.85
        _EdgeSoft ("边缘柔化", Range(0.01, 1)) = 0.35
    }

    SubShader
    {
        // 光柱是**纯附加**的发光体：不写深度、不投影、队列在几何之后、透明之前。
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShaftAdditive"
            Tags { "LightMode" = "UniversalForward" }

            // **附加混合 + 关深度写入 + 关剔除**（从内部看也要可见，否则进光柱会突然消失）
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP 的核心库（与 WhisperLitPbr 同一入口，已验证可用）
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ⚠ 纪律②：本 pass 用到的每个 uniform 都在这里声明
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _TipFade;
                float  _EdgeSoft;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;   // 锥体 UV：v=0 在光源端，v=1 在远端
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.uv = IN.uv;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ── 衰减模型（三项相乘，每项都有明确理由）─────────────────────────
                // ① 轴向衰减：从光源端到远端逐渐消失。**这是"光柱"而非"实心锥"的关键** ——
                //    没有它，锥体看起来就是一个塑料喇叭。
                float axial = 1.0 - saturate(IN.uv.y) * _TipFade;

                // ② 边缘柔化（**菲涅尔式**）：法线越垂直于视线 → 越靠锥体轮廓 → 越亮。
                //    物理直觉：视线穿过锥体的**路径长度**在轮廓处最长。
                //    ⚠ 用 `abs()`：`Cull Off` 时背面法线朝内，不加绝对值会让内表面全黑。
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float ndv = abs(dot(normalize(IN.normalWS), viewDirWS));
                float edge = lerp(1.0, 1.0 - ndv, _EdgeSoft);

                // ③ 相机距离淡出：贴脸时不要糊住整屏（玩家走进光柱里应几乎看不见它）
                float dist = distance(IN.positionWS, GetCameraPositionWS());
                float nearFade = saturate(dist / 1.2);

                half3 col = _Color.rgb * _Intensity * axial * edge * nearFade;
                // 预乘式附加：Blend One One ⇒ 输出即增量，alpha 不参与
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
