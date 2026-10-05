// Project Whisper · 关卡几何着色器（**带光照 + 自研雾**）
//
// ═══════════════════════════════════════════════════════════════════════════
// 2026-10-06 重写：从 Unlit 改为 ForwardBase + ForwardAdd 的真光照
//
// ## 为什么要改（云端渲染取证抓到的真实缺陷，不是推测）
// 原实现是 Unlit，片元只有一行 `return i.color;`。云端取证跑出来：
// ```
//   corridor_main/eye  lightOn_fogDefault  → 亮度 89.8 · 相对基准变化 0.000%
//   corridor_main/eye  lightOff_fogDefault → 亮度 89.8 · 相对基准变化 0.000%
//   entrance_safe/eye  lightOn_fogDefault  → 亮度 135.5 · 相对基准变化 0.000%
//   RENDER_EVIDENCE FAIL · 29 项判据不成立
// ```
// **开灯与关灯逐像素完全相同、手电筒对渲染零作用** ——
// 对一个靠光照营造氛围的恐怖游戏，这等于**氛围层整体缺失**。
// 用户此前反复说的"好亮/上色不行"，根子就在这里。
//
// ## 与旧注释的关系（那条教训仍然成立，别走回头路）
// 旧版曾开 `#pragma multi_compile_fog` + `UNITY_TRANSFER/APPLY_FOG`，真机把整个 3D 视图
// 洗成均匀的 #D8CFBB（亮度 207/255 · 标准差 2.1 · 边缘密度 0%）。
// 根因是**内置雾依赖 Lighting 设置这个不可控全局量**（本工程连 ProjectSettings 都没有）。
// ⇒ 本版**继续不用内置雾**，改用**自研雾**：起止/颜色全部由 `_WhisperFog*` 全局量驱动，
//   一处可调、可在取证里被开关与调参。**这正是"可控"与"不可控"的区别。**
//
// ## 为什么必须自带一个着色器（真机事故，2026-10-03）
//   `LevelBuilder.FlatMaterial` 曾用 `Shader.Find("Standard")` 建材质，而本工程
//   **没有任何 .mat 资产引用 Standard**（几何全部运行时生成）→ Standard 被剥离出包
//   → `new Material(null)` 抛异常 → 关卡几何一个都没建出来 → **全黑但没报错**。
//   放在 Resources/ 下的内容**无条件进包**，这是无编辑器条件下能给出的最强保证。
// ═══════════════════════════════════════════════════════════════════════════

Shader "Whisper/UnlitColor"
{
    Properties
    {
        // 必须叫 _Color：Material.color 以及 MaterialPropertyBlock 都写这个属性名
        _Color ("主色", Color) = (1, 1, 1, 1)

        // 环境项：默认值必须等于 DesignTokens.RenderLightAmbient（取证脚本会核对这个数）。
        // 改这里就必须同步 RenderEvidenceCapture.AmbientReference 与 LevelPalette 校准表。
        _WhisperAmbient ("环境项", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual

        // ═══════════════════════════════════════════════════════════════════
        // Pass 1 · ForwardBase —— 主光（平行光）+ 自研雾 + 环境项
        // ═══════════════════════════════════════════════════════════════════
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            // ── 自研雾的参数（全局量，**刻意不放进 Properties**）──────────────
            // 为什么不做成材质属性：`new Material(shader)` 会把属性值**固化进材质**，
            // 于是"一处可调"变成"每个材质各调一次"，取证脚本的 SetGlobalFloat 也会被材质值盖掉。
            // 起止与颜色同时以 #define 写死一份，供取证脚本核对（两边必须是同一份数）。
            #define WHISPER_FOG_START 8
            #define WHISPER_FOG_END 40
            #define WHISPER_FOG_COLOR float3(0.055, 0.051, 0.047)

            float _WhisperAmbient;
            float _WhisperFogOff;
            float _WhisperFogStartM;
            float _WhisperFogEndM;
            float4 _WhisperFogColor;

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float4 color  : COLOR;
                float3 worldN : TEXCOORD0;
                float3 worldP : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // 顶点色 × 主色：Cube 基元的顶点色是白，所以最终颜色 = 主色；
                // 接带烘焙顶点色的美术网格时无需改着色器。
                o.color = v.color * _Color;
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldP = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldN);

                // ⚠ 【实测踩到的坑，别再犯】主光被关掉时 `_WorldSpaceLightPos0` 会是 `(0,0,0)`，
                //   而 `normalize((0,0,0))` = **NaN** → 片元输出 NaN → **整张图全黑**，
                //   并且 ForwardAdd 加到 NaN 上仍是 NaN（手电也跟着消失）。
                //   取证实测：lightOff 亮度 0.0 · 颜色数 1，18 项判红就是这个。
                //   所以必须**先判零向量再归一化**。
                float3 lp = _WorldSpaceLightPos0.xyz;
                float3 l = (dot(lp, lp) > 1e-6) ? normalize(lp) : float3(0.0, 1.0, 0.0);

                // 半兰伯特：纯兰伯特在盒体背光面会全黑，半兰伯特保留体积感，
                // 也避免"关灯后什么都看不见"造成的取证歧义。
                // 主光关掉时 `_LightColor0` 为 0 → 只剩环境项，正是"关灯"应有的观感。
                float ndl = dot(n, l) * 0.5 + 0.5;
                float3 lit = _LightColor0.rgb * ndl;

                float3 albedo = i.color.rgb;
                float3 c = albedo * (_WhisperAmbient + lit);

                // ── 自研雾 ──
                // 线性距离雾，起止由全局量给（缺省回落到 #define）。
                // `_WhisperFogOff` 非 0 时整体跳过 —— 这就是"关雾"相位的实现。
                if (_WhisperFogOff < 0.5)
                {
                    float fstart = _WhisperFogStartM > 0.0 ? _WhisperFogStartM : WHISPER_FOG_START;
                    float fend   = _WhisperFogEndM   > 0.0 ? _WhisperFogEndM   : WHISPER_FOG_END;
                    float3 fcol  = _WhisperFogColor.rgb;

                    float dist = length(i.worldP - _WorldSpaceCameraPos);
                    float f = saturate((dist - fstart) / max(fend - fstart, 0.0001));
                    c = lerp(c, fcol, f);
                }

                return fixed4(c, 1.0);
            }
            ENDCG
        }

        // ═══════════════════════════════════════════════════════════════════
        // Pass 2 · ForwardAdd —— 手电筒等附加光源（**逐像素加色**）
        //
        // 为什么必须有：只有 ForwardBase 时**手电筒对渲染零作用** ——
        // 取证里 `lightOff_flashlight` 与 `lightOff_fogDefault` 会逐像素相同（实测 0.000%）。
        // 手电是恐怖游戏的核心交互（照路、照鬼），缺了它玩法层直接少一块。
        //
        // ⚠ 每个 pass 是**独立编译单元**：本 pass 用到的所有 `_Whisper*` 必须**在这里再声明一次**。
        //   真机事故 0.1.19 的整屏洋红就是这个坑（ForwardAdd 漏声明 `_WhisperFogOff`，
        //   GLES3/Vulkan 报 undeclared identifier → 着色器整体编译失败 → error shader 填满屏幕）。
        // ═══════════════════════════════════════════════════════════════════
        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdadd
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            // 同 Pass 1，**本 pass 内必须重新声明**（见上方 0.1.19 事故说明）
            #define WHISPER_FOG_START 8
            #define WHISPER_FOG_END 40
            #define WHISPER_FOG_COLOR float3(0.055, 0.051, 0.047)

            float _WhisperAmbient;
            float _WhisperFogOff;
            float _WhisperFogStartM;
            float _WhisperFogEndM;
            float4 _WhisperFogColor;

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float4 color  : COLOR;
                float3 worldN : TEXCOORD0;
                float3 worldP : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldP = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldN);

                // 附加光为点光/聚光时 `_WorldSpaceLightPos0` 是**世界坐标**（w=1），
                // 平行光时是方向（w=0）。必须分开处理，否则手电的位置会被当成方向。
                float3 ldir;
                float atten = 1.0;
                if (_WorldSpaceLightPos0.w > 0.5)
                {
                    float3 d = _WorldSpaceLightPos0.xyz - i.worldP;
                    float dist = length(d);
                    ldir = d / max(dist, 0.0001);
                    // 温和的平方反比：保证"开手电必然变亮"（取证要的是可分辨，不是物理精确）
                    atten = 1.0 / (1.0 + 0.15 * dist * dist);
                }
                else
                {
                    // 同样先判零向量（见 Pass 1 的说明）—— 否则附加光也会把该像素变 NaN
                    float3 lp = _WorldSpaceLightPos0.xyz;
                    ldir = (dot(lp, lp) > 1e-6) ? normalize(lp) : float3(0.0, 0.0, 0.0);
                }

                float ndl = saturate(dot(n, ldir));
                float3 add = _LightColor0.rgb * ndl * atten * i.color.rgb;

                // 雾对附加光同样生效：否则远处手电会"穿过雾"照出突兀亮斑。
                if (_WhisperFogOff < 0.5)
                {
                    float fstart = _WhisperFogStartM > 0.0 ? _WhisperFogStartM : WHISPER_FOG_START;
                    float fend   = _WhisperFogEndM   > 0.0 ? _WhisperFogEndM   : WHISPER_FOG_END;
                    float dist = length(i.worldP - _WorldSpaceCameraPos);
                    float f = saturate((dist - fstart) / max(fend - fstart, 0.0001));
                    add *= (1.0 - f);
                }

                return fixed4(add, 1.0);
            }
            ENDCG
        }
    }

    // 兜底：万一进了不支持 CG 的管线，至少不是隐形物体
    Fallback Off
}
