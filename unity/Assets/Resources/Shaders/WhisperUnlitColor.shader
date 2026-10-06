// Project Whisper · 关卡几何着色器（URP 版 · 半兰伯特 + 自研雾）
//
// ═══════════════════════════════════════════════════════════════════════════
// 2026-10-06 迁移：Built-in(CGPROGRAM) → **URP 17(HLSLPROGRAM)**
//
// ## 为什么必须迁（不是"想升级"，是不迁就全屏品红）
// 官方《Upgrade custom shaders for URP compatibility》明说：Built-in 写法的自定义着色器
// 在 URP 下**不会静默降级，而是变成品红**（"they turn magenta (bright pink) to indicate an error"），
// 且官方明说这类着色器**不能**用 Render Pipeline Converter 自动升级，必须重写。
// 出处：docs/reference-urp17-setup.md §9（含 95 个已探活的官方 URL）。
//
// ## 一个比"变品红"更危险的坑：`ForwardAdd` 在 URP 下**不执行且不报错**
// 官方 pass tag 表的"不支持清单"里明确含 `ForwardAdd`。原文件的手电筒/点光全在 Pass 2(ForwardAdd)，
// 若只开 URP 而不迁本文件 —— 画面正常、日志干净、**手电筒静默失效**。
// ⇒ URP 的附加光必须在 `UniversalForward` 里由**光照循环**处理（见下面的 LIGHTLOOP）。
//   这也是本文件"先迁着色器，再开管线"这条顺序的由来。
//
// ## 迁移时**刻意保留**的三件事（改动它们是回归）
// ① **自研距离雾**（`_WhisperFog*` 全局量）：历史事故是把 `multi_compile_fog` 打开后，
//    真机整个 3D 视图被内置雾洗成均匀 #D8CFBB（亮度 207/255 · 标准差 2.1）。
//    根因是**内置雾依赖 Lighting 设置这个不可控全局量**，而本工程连 ProjectSettings 真源都没有。
//    ⇒ 继续不用 `multi_compile_fog`，雾的起止/颜色/开关全部走全局量，一处可调、可在取证里开关。
// ② **属性名 `_Color`**：`Material.color` 与 `MaterialPropertyBlock` 都写这个名字，
//    全项目（LevelPalette / ModelLibrary / EvidencePlacer）都按它上色。改名字 = 全项目失色。
// ③ **半兰伯特**（`NdotL*0.5+0.5`）：纯兰伯特会让盒体背光面全黑，关灯后取证无法区分
//    "暗"与"没渲染"。半兰伯特保留体积感。
//
// ## 保留的踩坑纪律（每条都对应一次真机事故）
// · **每个 pass 各自声明它用到的每个 uniform**：多 pass 着色器漏一个 → `undeclared identifier`
//   → 整个着色器编译失败 → 真机整屏品红（0.1.19 事故）。
// · **不用嵌套三元**（`a ? b : c ? d : e`）：gles3/vulkan 的 HLSL→GLSL 转译器会报
//   `syntax error: unexpected token ')'`（0.1.46 实测，三个后端全挂）。要分支就写 if 或查表。
// · **不用 `[unroll]` 包 `continue`/`break`**：同上，转译后语法不合法。
// · **先判零向量再归一化**：主光被关掉时方向会是 `(0,0,0)`，`normalize` 得到 **NaN** →
//   片元输出 NaN → **整张图全黑**，且加色叠加上去仍是 NaN（手电跟着消失）。取证实测过。
//
// ## 为什么必须自带一个着色器（真机事故，2026-10-03）
// `LevelBuilder.FlatMaterial` 曾用 `Shader.Find("Standard")` 建材质，而本工程**没有任何 .mat
// 资产引用 Standard**（几何全部运行时生成）→ Standard 被剥离出包 → `new Material(null)` 抛异常
// → 关卡一个对象都没建出来 → **全黑但没报错**。放在 `Resources/` 下的内容**无条件进包**，
// 这是无编辑器条件下能给出的最强保证（`LevelBuilder` 用 `Resources.Load<Shader>` 取它）。
// ═══════════════════════════════════════════════════════════════════════════

Shader "Whisper/UnlitColor"
{
    Properties
    {
        // 必须叫 _Color：Material.color 与 MaterialPropertyBlock 都写这个属性名
        [MainColor] _Color ("主色", Color) = (1, 1, 1, 1)

        // 环境项：默认值必须等于 DesignTokens.RenderLightAmbient（取证脚本会核对这个数）。
        // 改这里就必须同步 RenderEvidenceCapture.AmbientReference 与 LevelPalette 校准表。
        _WhisperAmbient ("环境项", Range(0, 1)) = 0.22
    }

    SubShader
    {
        // URP 要求的 SubShader tag（官方迁移清单第 3 步）
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual

        // ═══════════════════════════════════════════════════════════════════
        // Pass 1 · UniversalForward —— 主光 + 附加光（手电/点光）+ 自研雾 + 环境项
        //   原 Pass 2(ForwardAdd) 的职责**合并进本 pass**：URP 不支持 ForwardAdd，
        //   附加光由下面的 LIGHTLOOP 在同一 pass 内逐个叠加。
        // ═══════════════════════════════════════════════════════════════════
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            // ── 光照相关的 keyword（少一个就会"某些灯不亮"且不报错）──
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS

            // ── URP 的库（Core 提供矩阵与变换；Lighting 提供 GetMainLight / 光照循环）──
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── 自研雾的参数（全局量，**刻意不放进 CBUFFER/Properties**）──────────
            // 为什么不做成材质属性：`new Material(shader)` 会把属性值**固化进材质**，
            // 于是"一处可调"变成"每个材质各调一次"，取证脚本的 SetGlobalFloat 也会被材质值盖掉。
            // 起止与颜色同时以 #define 写死一份，供取证脚本核对（两边必须是同一份数）。
            #define WHISPER_FOG_START 8
            #define WHISPER_FOG_END 40
            #define WHISPER_FOG_COLOR float3(0.055, 0.051, 0.047)

            float _WhisperFogOff;
            float _WhisperFogStartM;
            float _WhisperFogEndM;
            float4 _WhisperFogColor;

            // ── 材质属性必须包进 CBUFFER（SRP Batcher 的要求；官方迁移清单第 6 步）──
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _WhisperAmbient;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = GetVertexNormalInputs(IN.normalOS).normalWS;
                OUT.shadowCoord = GetShadowCoord(pos);
                // 顶点色 × 主色：Cube 基元的顶点色是白，所以最终颜色 = 主色；
                // 接带烘焙顶点色的美术网格时无需改着色器。
                OUT.color = IN.color * _Color;
                return OUT;
            }

            /** 自研距离雾：返回 [0,1] 的雾混合系数（0=无雾 1=全雾）。所有光照都要吃它。 */
            float WhisperFogK(float3 positionWS)
            {
                if (_WhisperFogOff > 0.5) return 0.0;
                float fstart = _WhisperFogStartM > 0.0 ? _WhisperFogStartM : WHISPER_FOG_START;
                float fend   = _WhisperFogEndM   > 0.0 ? _WhisperFogEndM   : WHISPER_FOG_END;
                float dist = distance(positionWS, _WorldSpaceCameraPos);
                return saturate((dist - fstart) / max(fend - fstart, 0.0001));
            }

            /** 环境项：与 Built-in 版逐字同义（半兰伯特只在主光项里用，环境项是常量项）。 */
            half3 WhisperAmbientTerm(half3 albedo)
            {
                return albedo * _WhisperAmbient;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                half3 albedo = IN.color.rgb;

                // ── 主光：半兰伯特（保留体积感）+ 阴影 ──
                // ⚠ 主光被关掉时 `GetMainLight()` 的方向是零向量 → normalize = NaN → 整图全黑。
                //   所以**先判零向量**再归一化（这是 Built-in 版踩过并写进注释的坑，迁移别丢）。
                Light mainLight = GetMainLight(IN.shadowCoord);
                float3 lp = mainLight.direction;
                float3 l = (dot(lp, lp) > 1e-6) ? normalize(lp) : float3(0.0, 1.0, 0.0);
                half ndl = dot(n, l) * 0.5 + 0.5;
                half3 lit = mainLight.color * ndl * mainLight.shadowAttenuation;

                // ── 附加光（手电筒 / 室内点光）：URP 的光照循环逐个叠加 ──
                // 这一段是原 ForwardAdd pass 的**等价替代**；没有它 = 手电筒零作用。
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                        float3 aldir = addLight.direction;
                        float3 al = (dot(aldir, aldir) > 1e-6) ? normalize(aldir) : float3(0.0, 0.0, 0.0);
                        half andl = saturate(dot(n, al));
                        // distanceAttenuation 里已含 URP 的距离衰减（替代原手写的 1/(1+0.15d²)）
                        lit += addLight.color * andl * addLight.distanceAttenuation * addLight.shadowAttenuation;
                    LIGHT_LOOP_END
                }
                #endif

                half3 c = WhisperAmbientTerm(albedo) + albedo * lit;

                // ── 自研雾 ──
                float f = WhisperFogK(IN.positionWS);
                float3 fcol = _WhisperFogColor.rgb;
                c = lerp(c, fcol, f);

                return half4(c, 1.0);
            }
            ENDHLSL
        }

        // ═══════════════════════════════════════════════════════════════════
        // Pass 2 · ShadowCaster —— 投影（原文件**没有**这个 pass ⇒"物体不投影"）
        //
        // 【为什么手写而不 include 官方的 ShadowCasterPass.hlsl】
        // 官方那个 .hlsl **自己声明了 `UnityPerMaterial` CBUFFER**（`_BaseMap`/`_BaseColor`/`_Cutoff`）。
        // 本工程有自己的 `UnityPerMaterial` 字段，两边在同一个 pass 里会出现**重复的 CBUFFER 定义**
        // ⇒ 编译失败 ⇒ 整个着色器变品红。官方那个文件是给"用 URP 标准属性名"的着色器准备的，
        // 与本工程的属性集不兼容。本 pass 只需要"把不透明几何的位置写进深度"，手写 26 行足够，
        // 且**不需要任何材质属性** —— 从根上没有 CBUFFER 冲突。
        // ═══════════════════════════════════════════════════════════════════
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ColorMask 0 + ZWrite On：只写深度，颜色无意义
                return 0;
            }
            ENDHLSL
        }

        // ═══════════════════════════════════════════════════════════════════
        // Pass 3 · DepthOnly —— 深度预判/深度纹理要用；缺了它开 Depth Priming 后物体会消失
        // ═══════════════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    // 兜底：万一进了不支持的管线，至少不是隐形物体
    Fallback Off
}
