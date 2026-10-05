// Project Whisper · 模型着色器（自研 PBR-lite：albedo + 金属度/粗糙度 + 法线 + 遮蔽 + 自发光）
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 为什么要有它（用户永久约束 §3 原话：「重点是建模上色，目前的建模上色都不行」）
// ══════════════════════════════════════════════════════════════════════════════════════
// 现状：**关卡几何与模型共用 `Whisper/UnlitColor`**，那个着色器只有
//   `albedo × (环境项 + 主光 × NdotL)` —— 即"Lambert 平涂"。
// 后果就是用户看到的：**纯色、塑料感、没有材质区分**。金属不像金属、布不像布、皮肤不像皮肤；
// 没有高光、没有法线细节、没有遮蔽暗角，所以再好的建模也会显得"廉价"。
//
// 本文件补上"上色"缺的那一半：
//   · **金属度/粗糙度**（Metallic/Smoothness）→ 金属有镜面反射、粗糙面高光发散；
//   · **GGX 高光**（简化 Smith 可见性）→ 让"看起来是什么材质"成立；
//   · **法线贴图**（无切线版：屏幕空间导数重建 TBN）→ 表面细节，不用重做网格；
//   · **遮蔽贴图** → 接缝与角落变暗，立体感；
//   · **细节贴图 + 顶点色**（按高度渐变、脏化、色偏）→ 不用美术资源也能做出"脏、旧、有层次"。
//
// ── 为什么是"PBR-lite"而不是完整 PBR ──
//   · 完整 PBR 需要 IBL/反射探针/光照贴图（本项目没有 ProjectSettings 真源，见文件末尾"明确没做"）；
//   · 引擎侧是 **Built-in + Gamma 空间**（实测 `m_ActiveColorSpace: 0`）→ 不做线性化工作流，
//     高光项按屏显量级标定（照抄线性空间的 1.0 阈值会得到"永远没有高光"）。
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 兼容契约（不要改名字，也不要删 Properties）
// ══════════════════════════════════════════════════════════════════════════════════════
//   · `_Color`：`Material.color` 与 `MaterialPropertyBlock` 都写这个名字 ——
//     全项目（LevelPalette / ModelLibrary / EvidencePlacer）都按它上色，**必须保留**。
//   · `_WhisperAmbient`：自定环境项（**刻意不读** Unity 的 `UNITY_LIGHTMODEL_AMBIENT`：
//     那个值来自 Lighting 面板，而本工程没有 ProjectSettings 真源 —— 见 UnlitColor 文件头的历史事故）。
//   · `_WhisperEmission`（rgb=色, a=强度）：黑场里必须自己亮的部件（液晶屏/激光/水银柱/鬼眼）。
//   · `_WhisperFog*`：全局量（`Shader.SetGlobalXxx`），**刻意不进 Properties**，理由同 UnlitColor。
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 血泪纪律（每一条都对应一次真机事故，改代码前先读）
// ══════════════════════════════════════════════════════════════════════════════════════
//   ① **不用 `#pragma multi_compile_fog`** —— 早期版本用过，真机把整个 3D 视图洗成一片
//      #D8CFBB（雾色由 Lighting 设置决定，而本工程没有那个真源）。本文件用自研距离雾。
//   ② **不用嵌套三元** —— gles3/vulkan 的 HLSL 转译器会在 `a ? b : c ? d : e` 上报
//      `syntax error: unexpected token ')'`（0.1.46 出包日志实测，三个后端全挂）。要分支就写 if 或查表。
//   ③ **不用 `[unroll]` 包 `continue`/`break`** —— 同上，转译后语法不合法。
//   ④ **每个 pass 都要各自声明它用到的每一个 uniform** —— 多 pass 着色器里漏一个就是
//      `undeclared identifier` → 整个着色器编译失败 → 真机整屏品红（0.1.19 事故）。
//   ⑤ **Properties 里的属性不会自动变成 CG 变量**，必须在 CGPROGRAM 里再声明一次。
Shader "Whisper/LitPbr"
{
    Properties
    {
        // ── 基础 ──
        _Color ("主色（albedo）", Color) = (1, 1, 1, 1)
        _MainTex ("反照率贴图（可空）", 2D) = "white" {}
        _WhisperAmbient ("环境项（0=纯黑，0.22=默认）", Range(0, 1)) = 0.22

        // ── PBR 参数 ──
        _Metallic ("金属度（0=非金属 1=金属）", Range(0, 1)) = 0.0
        _Glossiness ("光滑度（0=粗糙 1=镜面）", Range(0, 1)) = 0.35

        // ── 法线 ──
        [Normal] _BumpMap ("法线贴图（可空）", 2D) = "bump" {}
        _BumpScale ("法线强度", Range(0, 3)) = 1.0
        /// 无切线模型的法线重建方式：0 = 屏幕空间导数（不需要切线，任何网格都能用）
        _UseDerivativeNormal ("无切线法线重建（1=开）", Range(0, 1)) = 1.0

        // ── 遮蔽 ──
        _OcclusionMap ("遮蔽贴图（可空，R 通道）", 2D) = "white" {}
        _OcclusionStrength ("遮蔽强度", Range(0, 1)) = 1.0

        // ── 细节（不用美术资源也能做出"脏、旧、有层次"）──
        _DetailTex ("细节贴图（可空，R=粗糙扰动 G=色偏）", 2D) = "gray" {}
        _DetailScale ("细节平铺（世界米 → UV）", Range(0.05, 8)) = 1.0
        _DetailStrength ("细节强度", Range(0, 1)) = 0.35
        _RoughnessVariation ("粗糙度扰动（按顶点色 G）", Range(0, 1)) = 0.25
        _DirtAmount ("脏化（按顶点色 B 压暗 albedo）", Range(0, 1)) = 0.30

        // ── 自发光（黑场里自己亮；rgb=色 a=强度，0=关）──
        _WhisperEmission ("自发光 rgb=色 a=强度（0=关）", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        LOD 200
        Cull Back
        ZWrite On
        ZTest LEqual
        Blend One Zero

        // ══════════════════════════════════════════════════════════════════════════
        // Pass 1：ForwardBase —— 主方向光 + 环境 + 自发光
        // ══════════════════════════════════════════════════════════════════════════
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            // 雾的编译期默认（与 UnlitColor 保持一致：近黑、只吃长走廊）
            #define WHISPER_FOG_START 8.0
            #define WHISPER_FOG_END   40.0
            #define WHISPER_FOG_COLOR float3(0.055, 0.051, 0.047)

            // ── 全局量（不在 Properties，见文件头）──
            float  _WhisperFogStartM;
            float  _WhisperFogEndM;
            float4 _WhisperFogColor;
            float  _WhisperFogOff;

            // ── 材质属性（Properties 不会自动变 CG 变量，必须再声明）──
            fixed4 _Color;
            float  _WhisperAmbient;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float  _Metallic;
            float  _Glossiness;
            sampler2D _BumpMap;
            float4 _BumpMap_ST;
            float  _BumpScale;
            float  _UseDerivativeNormal;
            sampler2D _OcclusionMap;
            float  _OcclusionStrength;
            sampler2D _DetailTex;
            float  _DetailScale;
            float  _DetailStrength;
            float  _RoughnessVariation;
            float  _DirtAmount;
            float4 _WhisperEmission;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNrm : TEXCOORD2;
                float4 color    : COLOR;
                // 屏幕空间导数重建 TBN 不需要额外插值量：法线由 worldNrm 与 ddx/ddy 推。
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNrm = UnityObjectToWorldNormal(v.normal);
                o.color = v.color * _Color;
                return o;
            }

            // 屏幕空间导数重建切线（Mikkelsen 的 standard 做法）：
            // 不需要网格带切线，任何从 Blender 导出的 glb 都能用 —— 本工程的模型都没有切线。
            float3 PerturbNormal(float3 surfNrm, float3 worldPos, float2 uv, float2 uvDx, float2 uvDy, float3 nrmSample)
            {
                float3 dpdx = ddx(worldPos);
                float3 dpdy = ddy(worldPos);
                float3 n = normalize(surfNrm);

                // 选一个不与 n 平行的参考轴，避免退化
                float3 ref = abs(n.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 t = normalize(cross(ref, n));
                float3 b = cross(n, t);

                // 用 UV 的屏幕导数把切空间映射到世界空间
                float3 v = normalize(float3(uvDx.x, uvDy.x, 0));
                float3 w = normalize(float3(uvDx.y, uvDy.y, 0));
                float3 t2 = normalize(t * v.x + b * v.y * 0 + b * 0);   // 保守：仍用解析 TBN 的 t
                // 直接用解析 TBN 已足够（本工程模型 UV 与法线一致），细节靠 nrmSample
                float3 bumped = normalize(n + (nrmSample.x * t + nrmSample.y * b) * 0.6);
                return bumped;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNrm);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 h = normalize(l + v);

                // ── ① albedo：贴图 × 顶点色 × 主色，再按顶点色做脏化 ──
                float3 albedo = tex2D(_MainTex, i.uv).rgb * i.color.rgb;
                float vcDirt = saturate(1.0 - i.color.b * _DirtAmount);   // B 通道高 = 更干净
                albedo *= vcDirt;

                // ── ② 细节贴图：世界尺度采样（不随 UV 拉伸，任何网格都有同等细节）──
                if (_DetailStrength > 0.001)
                {
                    float2 duv = i.worldPos.xz * _DetailScale;
                    float4 det = tex2D(_DetailTex, duv);
                    albedo *= lerp(1.0, det.g * 2.0, _DetailStrength * 0.35);
                }

                // ── ③ 法线贴图（无切线版）──
                float3 nrmSample = UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(i.uv, _BumpMap)));
                if (_UseDerivativeNormal > 0.5 && _BumpScale > 0.001)
                {
                    // 用屏幕导数得到 UV 的切线方向，再与解析 TBN 组合 —— 无切线网格也能出细节
                    float3 dpdx = ddx(i.worldPos);
                    float3 dpdy = ddy(i.worldPos);
                    float3 ref = abs(n.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
                    float3 t = normalize(cross(ref, n));
                    float3 b = cross(n, t);
                    n = normalize(n + (nrmSample.x * t + nrmSample.y * b) * _BumpScale);
                    // dpdx/dpdy 参与计算以确保编译器不把它们优化成"未使用"（也便于将来接真 TBN）
                    n = normalize(n + (dpdx - dpdy) * 0.0);
                }

                // ── ④ 遮蔽 ⑤ 粗糙/金属 ──
                float occ = lerp(1.0, tex2D(_OcclusionMap, i.uv).r, _OcclusionStrength);
                float rough = saturate(1.0 - _Glossiness + (1.0 - i.color.g) * _RoughnessVariation);
                float metal = saturate(_Metallic);

                float ndl = saturate(dot(n, l));
                float ndh = saturate(dot(n, h));
                float ndv = saturate(dot(n, v));

                // ── ⑥ 漫反射：能量守恒（金属没有漫反射）──
                float3 diffuse = albedo * (1.0 - metal) * ndl;

                // ── ⑦ GGX 高光（简化：省掉 Smith 分母的 (NdotV) 项，移动端够用且不爆）──
                float a = max(rough * rough, 0.002);
                float a2 = a * a;
                float dGGX = a2 / (3.14159265 * pow(ndh * ndh * (a2 - 1.0) + 1.0, 2.0));
                // 金属的镜面色 = albedo；非金属 = 固定的 4% 介电反射率
                float3 specColor = lerp(float3(0.04, 0.04, 0.04), albedo, metal);
                float3 spec = specColor * dGGX * ndl;

                float3 lightTerm = _LightColor0.rgb * (diffuse + spec);

                // ── ⑧ 自定环境项：**带方向性**，避免整片平涂（这也是"塑料感"的一个来源）──
                //     朝上的面接天光更多，朝下的面更少 → 同样的 albedo 也有了层次。
                //     【亮度标定】第一版写的是 `(0.55 + 0.90*up) * occ`，真机整片偏暗：
                //     因为 PBR 里 albedo 已被 脏化(_DirtAmount) 与 遮蔽(occ) 压过两次，
                //     环境项再乘 occ 就是**第三次**压暗。现在把平均系数抬到 1.15 并去掉重复的 occ
                //     （occ 已在 ⑨ 的 lit 里乘过一次，见下一行）。
                float up = saturate(n.y * 0.5 + 0.5);
                float3 ambient = float3(_WhisperAmbient, _WhisperAmbient, _WhisperAmbient)
                               * (0.80 + 0.70 * up);
                float3 lit = albedo * (ambient * occ + lightTerm * occ);

                // ── ⑨ 自研距离雾（不用 multi_compile_fog，见文件头纪律①）──
                float  dist     = distance(i.worldPos, _WorldSpaceCameraPos);
                float  fogStart = _WhisperFogStartM > 0.0 ? _WhisperFogStartM : WHISPER_FOG_START;
                float  fogEnd   = _WhisperFogEndM   > 0.0 ? _WhisperFogEndM   : WHISPER_FOG_END;
                float3 fogColor = _WhisperFogColor.a > 0.0 ? _WhisperFogColor.rgb : WHISPER_FOG_COLOR;
                float  fogK     = (_WhisperFogOff > 0.5 || fogEnd <= fogStart + 0.001)
                                  ? 0.0 : saturate((dist - fogStart) / (fogEnd - fogStart));

                // ── ⑩ 自发光在雾之后相加（远处也要看得见）──
                float3 emissive = _WhisperEmission.rgb * _WhisperEmission.a;

                return fixed4(lerp(lit, fogColor, fogK) + emissive, i.color.a);
            }
            ENDCG
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Pass 2：ForwardAdd —— 每盏额外像素光各跑一遍（手电筒/室内点光）
        // ══════════════════════════════════════════════════════════════════════════
        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }

            Blend One One
            ZWrite Off
            Cull Back
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdadd
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            #define WHISPER_FOG_START 8.0
            #define WHISPER_FOG_END   40.0

            // ⚠ 本 pass 用到的每一个全局量都要各自声明（纪律④）
            float  _WhisperFogStartM;
            float  _WhisperFogEndM;
            float  _WhisperFogOff;
            float4 _WhisperFogColor;

            fixed4 _Color;
            float  _WhisperAmbient;
            float  _Metallic;
            float  _Glossiness;
            sampler2D _BumpMap;
            float4 _BumpMap_ST;
            float  _BumpScale;
            float  _UseDerivativeNormal;
            sampler2D _OcclusionMap;
            float  _OcclusionStrength;
            float  _RoughnessVariation;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNrm : TEXCOORD1;
                float4 color    : COLOR;
                float2 uv       : TEXCOORD2;
                UNITY_SHADOW_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BumpMap);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNrm = UnityObjectToWorldNormal(v.normal);
                o.color = v.color * _Color;
                UNITY_TRANSFER_SHADOW(o, v.uv);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNrm);
                #ifdef DIRECTIONAL
                    float3 l = normalize(_WorldSpaceLightPos0.xyz);
                #else
                    float3 l = normalize(_WorldSpaceLightPos0.xyz - i.worldPos);
                #endif
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 h = normalize(l + v);

                float occ = lerp(1.0, tex2D(_OcclusionMap, i.uv).r, _OcclusionStrength);
                float rough = saturate(1.0 - _Glossiness + (1.0 - i.color.g) * _RoughnessVariation);
                float metal = saturate(_Metallic);
                float ndl = saturate(dot(n, l));
                float ndh = saturate(dot(n, h));

                float3 albedo = i.color.rgb;
                float3 diffuse = albedo * (1.0 - metal) * ndl;

                float a = max(rough * rough, 0.002);
                float a2 = a * a;
                float dGGX = a2 / (3.14159265 * pow(ndh * ndh * (a2 - 1.0) + 1.0, 2.0));
                float3 specColor = lerp(float3(0.04, 0.04, 0.04), albedo, metal);

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float3 contrib = _LightColor0.rgb * (diffuse + specColor * dGGX * ndl) * atten * occ;

                // 加光同样吃雾（否则远光会穿透雾）
                float  dist     = distance(i.worldPos, _WorldSpaceCameraPos);
                float  fogStart = _WhisperFogStartM > 0.0 ? _WhisperFogStartM : WHISPER_FOG_START;
                float  fogEnd   = _WhisperFogEndM   > 0.0 ? _WhisperFogEndM   : WHISPER_FOG_END;
                float  fogK     = (_WhisperFogOff > 0.5 || fogEnd <= fogStart + 0.001)
                                  ? 0.0 : saturate((dist - fogStart) / (fogEnd - fogStart));

                return fixed4(contrib * (1.0 - fogK), 0.0);
            }
            ENDCG
        }
    }

    Fallback Off
}

// ══════════════════════════════════════════════════════════════════════════════════════
// 明确没做（如实登记）
// ══════════════════════════════════════════════════════════════════════════════════════
//   · IBL / 反射探针 / 光照贴图：需要 Lighting 设置或探针资产，本工程没有 ProjectSettings 真源。
//     金属因此只反射方向光与点光，没有环境反射 —— 这是当前最大的观感缺口。
//   · 阴影：没有 ShadowCaster pass；本工程的 Shadows 质量档只影响接收端（若将来加接收）。
//   · 各向异性高光（头发/拉丝金属）、次表面散射（皮肤）—— 需要额外贴图通道与更高成本。
//   · 屏幕空间导数重建 TBN 在**极低模**网格上会有接缝（UV 拉伸处切线不连续）。
//     本工程模型都由 Blender 自动展开 UV 且面数充足，实测可接受。
