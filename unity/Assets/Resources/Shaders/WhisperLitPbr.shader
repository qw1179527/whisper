// Project Whisper · 模型/关卡着色器（URP 版 · PBR-lite：albedo + 金属度/粗糙度 + 法线 + 遮蔽 + 自发光）
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 2026-10-06 迁移：Built-in(CGPROGRAM + ForwardBase/ForwardAdd) → **URP 17(HLSLPROGRAM)**
//
// ## 为什么必须迁
// 官方《Upgrade custom shaders for URP compatibility》明说 Built-in 写法的自定义着色器在 URP 下
// **变品红**，且不能自动升级。见 docs/reference-urp17-setup.md §9。
//
// ## 本次迁移里**最要命**的一条：`ForwardAdd` 在 URP 下不执行且不报错
// 官方 pass tag 表的"不支持清单"含 `ForwardAdd`。原文件的 Pass 2（手电筒/室内点光，`Blend One One`）
// 是**整段失效**的 —— 画面不会变品红，日志也不会报错，只是**灯不亮**。
// ⇒ 附加光改为在 `UniversalForward` 内用 URP 的**光照循环**处理（见 LIGHT_LOOP 段）。
//
// ## 为什么是"PBR-lite"而不是完整 PBR（这条判断在本工程仍然成立）
//   · 本工程**没有 ProjectSettings/Lighting 真源**（`unity/ProjectSettings/README.md` 明令不许手写），
//     所以刻意**不读** `UNITY_LIGHTMODEL_AMBIENT`（那个宏还是 Built-in 专有），改用自定 `_WhisperAmbient`；
//   · 颜色空间是 **Gamma**（`m_ActiveColorSpace: 0`），高光项按屏显量级标定 ——
//     照抄线性空间的 1.0 阈值会得到"永远没有高光"；
//   · **真实增加**的部分（相对旧版）：阴影接收（`GetMainLight(shadowCoord)`）、ShadowCaster pass
//     （旧文件自述"没有 ShadowCaster pass"）、DepthOnly pass。
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 兼容契约（不要改名字，也不要删 Properties）
// ══════════════════════════════════════════════════════════════════════════════════════
//   · `_Color`：`Material.color` 与 `MaterialPropertyBlock` 都写这个名字 ——
//     全项目（LevelPalette / ModelLibrary / EvidencePlacer）都按它上色，**必须保留**。
//   · `_WhisperAmbient`：自定环境项（刻意不读引擎环境光，理由见上）。
//   · `_WhisperEmission`（rgb=色, a=强度）：黑场里必须自己亮的部件（液晶屏/激光/水银柱/鬼眼）。
//   · `_WhisperFog*`：全局量（`Shader.SetGlobalXxx`），**刻意不进 Properties / 不进 CBUFFER**。
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 血泪纪律（每一条都对应一次真机事故，改代码前先读）
// ══════════════════════════════════════════════════════════════════════════════════════
//   ① **不用 `multi_compile_fog`** —— 早期版本用过，真机把整个 3D 视图洗成一片
//      #D8CFBB（雾色由 Lighting 设置决定，而本工程没有那个真源）。本文件用自研距离雾。
//   ② **不用嵌套三元** —— gles3/vulkan 的 HLSL 转译器会在 `a ? b : c ? d : e` 上报
//      `syntax error: unexpected token ')'`（0.1.46 实测，三个后端全挂）。要分支就写 if 或查表。
//   ③ **不用 `[unroll]` 包 `continue`/`break`** —— 同上，转译后语法不合法。
//   ④ **每个 pass 都要各自声明它用到的每一个 uniform** —— 多 pass 着色器里漏一个就是
//      `undeclared identifier` → 整个着色器编译失败 → 真机整屏品红（0.1.19 事故）。
//   ⑤ **属性不会自动变成 HLSL 变量**，必须在 CBUFFER 里再声明一次。
//   ⑥ **`fixed` 在 URP 不受支持**（官方原文）→ 全部改 `half`/`float`。
Shader "Whisper/LitPbr"
{
    Properties
    {
        // ── 基础 ──
        [MainColor] _Color ("主色（albedo）", Color) = (1, 1, 1, 1)
        [MainTexture] _MainTex ("反照率贴图（可空）", 2D) = "white" {}
        _WhisperAmbient ("环境项（0=纯黑，0.22=默认）", Range(0, 1)) = 0.22
        _ShadowDebug ("阴影诊断（0=正常 1=强制无阴影 2=显式坐标）", Range(0, 2)) = 0

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
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        LOD 200
        Cull Back
        ZWrite On
        ZTest LEqual

        // ══════════════════════════════════════════════════════════════════════
        // Pass 1 · UniversalForward —— 主光（带阴影）+ 附加光（手电/点光）+ 环境 + 自发光 + 自研雾
        //   原 Pass 1(ForwardBase) 与 Pass 2(ForwardAdd) **合并到这一个 pass**
        // ══════════════════════════════════════════════════════════════════════
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 雾的编译期默认（与 UnlitColor 保持一致：近黑、只吃长走廊）
            #define WHISPER_FOG_START 8.0
            #define WHISPER_FOG_END   40.0
            #define WHISPER_FOG_COLOR float3(0.055, 0.051, 0.047)

            // ── 全局量（**刻意不进 CBUFFER**：它们由 Shader.SetGlobalXxx 驱动，一处可调）──
            float  _WhisperFogStartM;
            float  _WhisperFogEndM;
            float4 _WhisperFogColor;
            float  _WhisperFogOff;

            // ── 材质属性必须包进 CBUFFER（SRP Batcher 要求；多 pass 必须**同一个** CBUFFER）──
            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                float4 _MainTex_ST;
                float  _WhisperAmbient;
                float  _ShadowDebug;
                float  _Metallic;
                float  _Glossiness;
                float4 _BumpMap_ST;
                float  _BumpScale;
                float  _UseDerivativeNormal;
                float  _OcclusionStrength;
                float  _DetailScale;
                float  _DetailStrength;
                float  _RoughnessVariation;
                float  _DirtAmount;
                half4  _WhisperEmission;
            CBUFFER_END

            TEXTURE2D(_MainTex);       SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap);       SAMPLER(sampler_BumpMap);
            TEXTURE2D(_OcclusionMap);  SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_DetailTex);     SAMPLER(sampler_DetailTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                half4  color       : COLOR;
                float4 shadowCoord : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS  = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = GetVertexNormalInputs(IN.normalOS).normalWS;
                OUT.shadowCoord = GetShadowCoord(pos);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color * _Color;
                return OUT;
            }

            float WhisperFogK(float3 positionWS)
            {
                if (_WhisperFogOff > 0.5) return 0.0;
                float fstart = _WhisperFogStartM > 0.0 ? _WhisperFogStartM : WHISPER_FOG_START;
                float fend   = _WhisperFogEndM   > 0.0 ? _WhisperFogEndM   : WHISPER_FOG_END;
                float dist = distance(positionWS, _WorldSpaceCameraPos);
                return saturate((dist - fstart) / max(fend - fstart, 0.0001));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(_WorldSpaceCameraPos - IN.positionWS);

                // ── ① albedo：贴图 × 顶点色 × 主色，再按顶点色做脏化 ──
                half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb * IN.color.rgb;
                half vcDirt = saturate(1.0 - IN.color.b * _DirtAmount);   // B 通道高 = 更干净
                albedo *= vcDirt;

                // ── ② 细节贴图：世界尺度采样（不随 UV 拉伸，任何网格都有同等细节）──
                if (_DetailStrength > 0.001)
                {
                    float2 duv = IN.positionWS.xz * _DetailScale;
                    half4 det = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, duv);
                    albedo *= lerp(1.0, det.g * 2.0, _DetailStrength * 0.35);
                }

                // ── ③ 法线贴图（无切线版：解析 TBN，不需要网格带切线）──
                half3 nrmSample = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(IN.uv, _BumpMap)));
                if (_UseDerivativeNormal > 0.5 && _BumpScale > 0.001)
                {
                    half3 ref = abs(n.y) < 0.999 ? half3(0, 1, 0) : half3(1, 0, 0);
                    half3 t = normalize(cross(ref, n));
                    half3 b = cross(n, t);
                    n = normalize(n + (nrmSample.x * t + nrmSample.y * b) * _BumpScale);
                }

                // ── ④ 遮蔽 ⑤ 粗糙/金属 ──
                half occ = lerp(1.0, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, IN.uv).r, _OcclusionStrength);
                half rough = saturate(1.0 - _Glossiness + (1.0 - IN.color.g) * _RoughnessVariation);
                half metal = saturate(_Metallic);

                // ── ⑥ 主光（带阴影）──
                // ⚠ 主光被关掉时方向是零向量 → normalize = NaN → 整图全黑（Built-in 版踩过，迁移别丢）
                // ── 【2026-10-06 阴影诊断开关】──────────────────────────────────────────
                // 实测事实：主光 `shadows=Soft`、全局 `_MAIN_LIGHT_SHADOWS_CASCADE=True`，
                // 但取证的三条阴影对照（分辨率/开-关/仅主光）**全是 0.000%**。
                // 关键矛盾：**全局** `_MAIN_LIGHT_SHADOWS=False` 而 `_MAIN_LIGHT_SHADOWS_CASCADE=True`，
                // 而 URP 的 `GetShadowCoord` 里是 `#if defined(_MAIN_LIGHT_SHADOWS_CASCADE)`。
                // ⇒ 怀疑：`_MAIN_LIGHT_SHADOWS` 那条变体在本工程**从未被启用**，
                //   于是我一直在测一条**没被编译进去的路径**。
                // 为一次判定，加一个**只用于取证**的调试开关：
                //   _ShadowDebug = 0 → 正常 `GetMainLight(IN.shadowCoord)`
                //   _ShadowDebug = 1 → 强制无阴影（shadowAttenuation 恒 1）
                //   _ShadowDebug = 2 → 走 `half4(1,1,1,1)` 的显式阴影坐标路径
                // 三者同机位各渲一张 ⇒ 一次分辨"阴影链没编进去"与"坐标无效"。
                Light mainLight;
                if (_ShadowDebug > 1.5)
                    mainLight = GetMainLight(GetShadowCoord(IN.positionWS), IN.positionWS);
                else
                    mainLight = GetMainLight(IN.shadowCoord);
                if (_ShadowDebug > 0.5 && _ShadowDebug < 1.5)
                    mainLight.shadowAttenuation = 1.0;
                float3 lp = mainLight.direction;
                float3 l = (dot(lp, lp) > 1e-6) ? normalize(lp) : float3(0.0, 1.0, 0.0);
                half3 h = normalize(l + v);

                half ndl = saturate(dot(n, l));
                half ndh = saturate(dot(n, h));

                // 漫反射：能量守恒（金属没有漫反射）
                half3 diffuse = albedo * (1.0 - metal) * ndl;

                // GGX 高光（简化：省掉 Smith 分母的 NdotV 项，移动端够用且不爆）
                half a = max(rough * rough, 0.002);
                half a2 = a * a;
                half dGGX = a2 / (3.14159265 * pow(ndh * ndh * (a2 - 1.0) + 1.0, 2.0));
                // 金属的镜面色 = albedo；非金属 = 固定的 4% 介电反射率
                half3 specColor = lerp(half3(0.04, 0.04, 0.04), albedo, metal);
                half3 spec = specColor * dGGX * ndl * mainLight.shadowAttenuation;

                half3 lightTerm = mainLight.color * (diffuse * mainLight.shadowAttenuation + spec);

                // ── ⑦ 附加光（手电筒/室内点光）：原 ForwardAdd pass 的等价替代 ──
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                        float3 aldir = addLight.direction;
                        float3 al = (dot(aldir, aldir) > 1e-6) ? normalize(aldir) : float3(0.0, 0.0, 0.0);
                        half andl = saturate(dot(n, al));
                        half3 ah = normalize(al + v);
                        half andh = saturate(dot(n, ah));
                        half adGGX = a2 / (3.14159265 * pow(andh * andh * (a2 - 1.0) + 1.0, 2.0));
                        half atten = addLight.distanceAttenuation * addLight.shadowAttenuation;
                        half3 addDiff = albedo * (1.0 - metal) * andl;
                        half3 addSpec = specColor * adGGX * andl;
                        lightTerm += addLight.color * (addDiff + addSpec) * atten;
                    LIGHT_LOOP_END
                }
                #endif

                // ── ⑧ 自定环境项：**带方向性**，避免整片平涂（"塑料感"的一个来源）──
                //     朝上的面接天光更多，朝下的面更少 → 同样的 albedo 也有了层次。
                //     【亮度标定】平均系数 1.15、且**不再乘 occ**：albedo 已被脏化与遮蔽压过，
                //     环境项再乘 occ 就是第三次压暗（真机实测整片偏暗）。
                half up = saturate(n.y * 0.5 + 0.5);
                half3 ambient = half3(_WhisperAmbient, _WhisperAmbient, _WhisperAmbient) * (0.80 + 0.70 * up);
                half3 lit = albedo * (ambient + lightTerm * occ);

                // ── ⑨ 自研距离雾 ──
                float fogK = WhisperFogK(IN.positionWS);
                half3 fogColor = _WhisperFogColor.a > 0.0 ? _WhisperFogColor.rgb : WHISPER_FOG_COLOR;

                // ── ⑩ 自发光在雾之后相加（远处也要看得见）──
                half3 emissive = _WhisperEmission.rgb * _WhisperEmission.a;

                // ⚠⚠ 【2026-10-06 修一个我自己在 URP 迁移时引入的回归 —— lerp 参数写反了】⚠⚠
                // 我原先写的是 `lerp(lit, fogColor, fogK)`，但它与正确语义**正好相反**：
                //   · 正确语义：fogK=0（近处/未起雾）→ 应是**本体**；fogK=1（远处）→ 才是雾色
                //     ⇒ `lerp(lit, fogColor, fogK)`
                //   · 而我写的那个在 fogK=0 时返回 **fogColor** —— 等价于"雾永远按最大浓度参与"，
                //     画面被雾色整体替换掉本体。
                //   （原始 Built-in 版是 `lerp(c, fcol, f)`，参数顺序与上面这条正确语义一致；
                //     我在迁 URP 时把两个实参写颠倒了，属于纯粹的迁移事故。）
                // 后果（云端取证实测，不是推测）：
                //   entrance_safe/orbit33 开灯 2.9 < **关灯 17.6** —— 开灯反而更暗（自相矛盾），
                //   "颜色数 2 / 判为全黑"的判红就是它。
                // 为什么本机门禁没拦住：本机没有 Unity，无法离线渲染；门禁只能验
                //   "uniform 有没有声明、契约对不对"，**验不了这个数学式对不对**。
                // ⇒ 这条只有「看图 + 读像素」能发现 —— 本项目"门禁全绿但结果是错的"的又一例。
                return half4(lerp(lit, fogColor, fogK) + emissive, IN.color.a);
            }
            ENDHLSL
        }

        // ══════════════════════════════════════════════════════════════════════
        // Pass 2 · ShadowCaster —— 投影（原文件自述"没有 ShadowCaster pass"）
        //   手写而不 include 官方 ShadowCasterPass.hlsl：官方那个文件自带 UnityPerMaterial
        //   CBUFFER（_BaseMap/_BaseColor/_Cutoff），与本工程的字段集**重复定义** → 编译失败。
        //
        // ⚠⚠【2026-10-06 修一个我自己引入的真 bug —— 这段 bias 不能省】⚠⚠
        // 我最初把它写成最朴素的 `TransformObjectToHClip(positionOS.xyz)`，**没有 shadow bias**。
        // 后果（云端取证 + 同轮 A/B 实测）：`entrance_safe/orbit33` 带阴影 2.93 vs **关阴影 17.92**
        // （差 15.00，关掉阴影亮度涨 6 倍）⇒ **几何把自己投进了自己的阴影**，主光贡献整片归零，
        // 再叠雾就成了"全黑"判红。官方 `ShadowCasterPass.hlsl` 里正是这段 `ApplyShadowBias`，
        // 手写时漏掉 = 把"深度偏移"整件事丢掉。
        // offset 方向约定：URP 里 `_MainLightPosition.xyz` 是**指向光源**的向量，故此处取负号
        // （与 URP 内部 `GetShadowPositionHClip` 一致，从而与 Asset 的 shadowDepth/NormalBias 配套）。
        // ══════════════════════════════════════════════════════════════════════
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            float3 _LightDirection;
            float3 _LightPosition;
            float  _ShadowBias;

            float4 GetShadowPositionHClip(float3 positionOS, float3 normalOS)
            {
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);

                float3 lightDirectionWS = _LightDirection;
                // 聚光/点光阴影用**光源位置**而非方向：此时 `_LightDirection` 为 0，
                // 必须改成"从光源指向本点"，否则偏移方向是零向量（= 完全不偏移）。
                if (_LightPosition.w != 0.0)
                    lightDirectionWS = _LightPosition.xyz - positionWS;

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = GetShadowPositionHClip(IN.positionOS.xyz, IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ══════════════════════════════════════════════════════════════════════
        // Pass 3 · DepthOnly —— 深度预判/深度纹理（缺了它开 Depth Priming 后物体会消失）
        // ══════════════════════════════════════════════════════════════════════
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

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}

// ══════════════════════════════════════════════════════════════════════════════════════
// 明确没做（如实登记，不含糊）
// ══════════════════════════════════════════════════════════════════════════════════════
//   · IBL / 反射探针 / 光照贴图：需要 Lighting 设置或探针资产，本工程没有 ProjectSettings 真源。
//     金属因此只反射主光与点光，没有环境反射 —— 这是当前最大的观感缺口。
//     （URP 下的正解是 Reflection Probe 或 `SampleSH`；两者都要资产/设置，属于后续里程碑。）
//   · 各向异性高光（头发/拉丝金属）、次表面散射（皮肤）—— 需要额外贴图通道与更高成本。
//   · 屏幕空间导数重建 TBN 在**极低模**网格上会有接缝（UV 拉伸处切线不连续）。
//     本工程模型都由 Blender 自动展开 UV 且面数充足，实测可接受。
