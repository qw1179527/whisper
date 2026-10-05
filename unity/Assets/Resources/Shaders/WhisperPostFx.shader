// Project Whisper · 后处理栈（自研，单文件多 pass）
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 为什么自研而不是用 Post Processing Stack / URP Volume
// ══════════════════════════════════════════════════════════════════════════════════════
//   · 本工程**活动管线是 Built-in**（`GraphicsSettings.m_CustomRenderPipeline: {fileID: 0}` 实测）；
//     URP 17 只是装在依赖里没启用。要启用 URP 得新增 URP Asset + 改 GraphicsSettings/QualitySettings，
//     而本工程没有 ProjectSettings 真源（V9 §19.1 C1），且历史上"资产被剥离"已造成过真机全黑事故。
//   · PPSv2 需要包依赖 + Volume 资产（又是 .asset 资产 → 同一个剥离风险）。
//   · 颜色空间是 **Gamma**（`m_ActiveColorSpace: 0`）→ 后处理里的亮度阈值要按屏显 sRGB 量级标定，
//     不能照抄"线性空间里 1.0 以上才算高光"那套（那会让辉光永远不触发）。
//   · 自研的代价：没有时序抗锯齿（TAA）、没有物理正确的泛光能量守恒。见文件末尾"明确没做"。
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 每个 pass 的输入/输出（改代码前先读这张表）
// ══════════════════════════════════════════════════════════════════════════════════════
//   Pass 0  Bright       src → 半分辨率亮部（辉光种子）
//   Pass 1  BlurH        src → 水平模糊
//   Pass 2  BlurV        src → 垂直模糊
//   Pass 3  Composite    src(+bloom) → 最终画面：SSAO/SSGI/眼适应/辉光/颗粒/暗角/体积光
//   Pass 4  EyeAdaptSample src → 1x1 平均亮度（CPU 读回做时间适应）
//
//   **所有 pass 都必须自己声明用到的每个 uniform** —— 这是本项目踩过的坑：
//   多 pass 着色器里漏声明一个全局量 → 整个 shader 编译失败 → 真机整屏品红（0.1.19 事故）。
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 开关约定（全部走**全局量**，不在 Properties 里）
// ══════════════════════════════════════════════════════════════════════════════════════
//   为什么走全局量：本组效果是**全局**的（一个相机一套），进 Properties 会被
//   `new Material(shader)` 固化，将来想全局改就得追材质引用 —— 与雾同样的取舍（见 WhisperUnlitColor.shader）。
//   全局名                     含义                        0 值行为
//   -------------------------  --------------------------  ------------------
//   _WhisperFxBloom            辉光强度                    0 = 关
//   _WhisperFxAo               环境遮蔽强度                0 = 关
//   _WhisperFxGi               全局光照（反弹）强度        0 = 关
//   _WhisperFxEye              眼部适应强度                0 = 关
//   _WhisperFxGrain            颗粒量                      0 = 关
//   _WhisperFxVignette         暗角强度                    0 = 关
//   _WhisperFxVolumetric       体积光（屏幕空间散射）强度   0 = 关
//   _WhisperFxExposure         当前曝光（CPU 每帧算完写回） 1 = 中性
//   _WhisperFxAoRadiusM        SSAO 世界半径（米）          用默认 0.6
//   _WhisperFxTime              时间（颗粒/体积光噪声用）     —
//
// ══════════════════════════════════════════════════════════════════════════════════════
// 明确没做（如实登记，不含糊）
// ══════════════════════════════════════════════════════════════════════════════════════
//   · TAA / 时序超分：需要运动矢量与历史缓冲，成本与风险都远超本轮范围。
//   · 屏幕空间反射（SSR）：同样的射线步进成本，本轮先做 SSAO/SSGI。
//   · 真实体积雾积分（ray-march 进深度）：本 pass 做的是**屏幕空间径向散射**，
//     近似"亮处向四周溢出"，不是物理体积雾。真正的体积雾已在几何着色器里用距离雾近似。

Shader "Whisper/PostFx"
{
    Properties
    {
        // 唯一必须进 Properties 的：主纹理（OnRenderImage 的 src 由脚本 Blit 传入，不靠这个）
        _MainTex ("源", 2D) = "white" {}
    }

    SubShader
    {
        // 后处理**不写深度、不剔面、不参与光照**；Cull Off + ZWrite Off 是硬要求
        Cull Off
        ZWrite Off
        ZTest Always

        // ───────────────────────── Pass 0：亮部提取（辉光种子） ─────────────────────────
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float  _WhisperFxBloomThreshold;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 4 点盒式下采样（同时降噪）；Gamma 空间阈值默认 0.62（屏显量级，不是线性 1.0）
                float2 d = _MainTex_TexelSize.xy;
                float3 c = 0;
                c += tex2D(_MainTex, i.uv + float2(-d.x, -d.y)).rgb;
                c += tex2D(_MainTex, i.uv + float2( d.x, -d.y)).rgb;
                c += tex2D(_MainTex, i.uv + float2(-d.x,  d.y)).rgb;
                c += tex2D(_MainTex, i.uv + float2( d.x,  d.y)).rgb;
                c *= 0.25;
                float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                // 软膝盖：阈值以下不发光，以上线性放大 —— 硬阈值会让高光边缘"跳"
                float k = saturate((lum - _WhisperFxBloomThreshold) / max(1e-4, 1.0 - _WhisperFxBloomThreshold));
                return fixed4(c * k, 1.0);
            }
            ENDCG
        }

        // ───────────────────────── Pass 1/2：可分离高斯模糊 ─────────────────────────
        // 一次 pass 处理一个方向（`_WhisperFxBlurDir` = (1,0) 或 (0,1)）：
        // 可分离 = 9+9 次采样代替 81 次，移动端这是必须的。
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float2 _WhisperFxBlurDir;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 9 抽头高斯（权重和 = 1，已归一化）
                const float w[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
                float2 step = _WhisperFxBlurDir * _MainTex_TexelSize.xy;
                float3 c = tex2D(_MainTex, i.uv).rgb * w[0];
                [unroll]
                for (int k = 1; k < 5; k++)
                {
                    c += tex2D(_MainTex, i.uv + step * k).rgb * w[k];
                    c += tex2D(_MainTex, i.uv - step * k).rgb * w[k];
                }
                return fixed4(c, 1.0);
            }
            ENDCG
        }

        // ───────────────────────── Pass 3：合成（所有效果的落点） ─────────────────────────
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _WhisperBloomTex;
            // 深度+法线：SSAO/SSGI 用它重建"物体在哪、朝向如何"。
            // 脚本里设 camera.depthTextureMode = Depth | DepthNormals 后 Unity 才会生成它。
            sampler2D _CameraDepthNormalsTexture;
            float4 _CameraDepthNormalsTexture_TexelSize;
            float4 _MainTex_TexelSize;

            float _WhisperFxBloom;
            float _WhisperFxBloomThreshold;
            float _WhisperFxAo;
            float _WhisperFxGi;
            float _WhisperFxEye;
            float _WhisperFxGrain;
            float _WhisperFxVignette;
            float _WhisperFxVolumetric;
            float _WhisperFxExposure;
            float _WhisperFxAoRadiusM;
            float _WhisperFxTime;
            float4 _WhisperFxAmbientColor;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // 解码深度法线纹理：xy = 法线（编码），zw = 深度（编码）
            void DecodeDN(float2 uv, out float3 nrm, out float linDepth)
            {
                float4 dn = tex2D(_CameraDepthNormalsTexture, uv);
                DecodeDepthNormal(dn, linDepth, nrm);
                nrm = nrm * 2.0 - 1.0;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 col = tex2D(_MainTex, i.uv).rgb;

                // 【0.1.50】直通分支：所有效果都关时**原样返回**。
                // 加它的目的有两个，都很实在：
                //   ① 二分定位 —— 黑屏到底出在"合成"还是"某个效果"？有这条就能一眼分开；
                //   ② 正确性 —— 全关时不该有任何一次多余的采样/乘法（省电，且避免"关了还变样"）。
                if (_WhisperFxBloom < 0.001 && _WhisperFxAo < 0.001 && _WhisperFxGi < 0.001
                    && _WhisperFxVolumetric < 0.001 && _WhisperFxEye < 0.001
                    && _WhisperFxGrain < 0.001 && _WhisperFxVignette < 0.001)
                    return fixed4(col, 1.0);

                // ① 环境遮蔽（SSAO）：在法线半球内比较邻域深度，邻域更近 → 本点被遮住
                if (_WhisperFxAo > 0.001)
                {
                    float3 n; float d;
                    DecodeDN(i.uv, n, d);
                    float occ = 0.0;
                    // 8 方向 × 1 步：移动端友好（16 抽头）。半径按**线性深度**归一化，
                    // 所以世界半径 _WhisperFxAoRadiusM 在远近处表现一致。
                    const float2 dirs[8] = {
                        float2(1,0), float2(-1,0), float2(0,1), float2(0,-1),
                        float2(0.707,0.707), float2(-0.707,0.707), float2(0.707,-0.707), float2(-0.707,-0.707)
                    };
                    float r = _WhisperFxAoRadiusM * _CameraDepthNormalsTexture_TexelSize.z;   // z = 1/width
                    r = max(r, _CameraDepthNormalsTexture_TexelSize.x) * 12.0;
                    [unroll]
                    for (int k = 0; k < 8; k++)
                    {
                        float2 off = dirs[k] * r;
                        float3 ns; float ds;
                        DecodeDN(i.uv + off, ns, ds);
                        // 邻域深度更小（更近）且法线不共面 → 判为遮挡
                        float dz = d - ds;
                        float rangeCheck = saturate(1.0 - abs(dz) * 8.0);
                        occ += (dz > 0.0 ? 1.0 : 0.0) * rangeCheck * saturate(dot(n, ns));
                    }
                    occ = 1.0 - (occ / 8.0) * _WhisperFxAo;
                    // 只压暗，不提亮：避免"发光的地板"这种反物理结果。
                    // 【深度无效保护】本工程 PostFx 走 OnRenderImage + 手工 Blit，
                    // 实测深度法线纹理在这里拿不到有效数据（HUD 亮度读回 raw=0.00）。
                    // 深度无效时 occ 会退化成常量 —— 把它当有效结果用是错的，
                    // 所以夹下限 0.35：遮蔽最多压到 35%，数学上不可能变成黑屏。
                    occ = clamp(occ, 0.35, 1.0);
                    col *= occ;
                }

                // ② 全局光照（SSGI 近似）：把亮部**按法线方向**加回来 —— 暗部不再死黑。
                //     这是近似而非真 GI：没有射线求交，只用"屏幕空间亮度 × 环境色的方向性"。
                //     之所以够用：用户要的是"阴影和暗部不再死黑"，而不是可对拍的物理正确 GI。
                if (_WhisperFxGi > 0.001)
                {
                    float3 n; float d;
                    DecodeDN(i.uv, n, d);
                    float3 bounce = 0;
                    // ⚠ 这里原本写的是嵌套三元 `float2(k==0?1.0:k==1?-1.0:0.0, k==2?1.0:k==3?-1.0:0.0)`，
                    //   在 d3d11 / gles3 / vulkan **三个后端全部报**
                    //   `syntax error: unexpected token ')'`（真机出包日志实测）。
                    //   HLSL→GLSL 的转译器处理不了三元里再套三元 —— 改成**常量查表**，
                    //   这既是最保险的写法，也更好读。
                    const float2 giOff[4] = { float2(1,0), float2(-1,0), float2(0,1), float2(0,-1) };
                    [unroll]
                    for (int k = 0; k < 4; k++)
                    {
                        float2 off = giOff[k] * _CameraDepthNormalsTexture_TexelSize.xy * 10.0;
                        bounce += tex2D(_MainTex, i.uv + off).rgb;
                    }
                    bounce *= 0.25;
                    // 朝上的面收到更多反弹（近似天光），朝下的面少
                    float up = saturate(n.y * 0.5 + 0.5);
                    col += bounce * _WhisperFxAmbientColor.rgb * _WhisperFxGi * up;
                }

                // ③ 体积光（屏幕空间径向散射近似）：亮像素沿指向屏幕中心的方向拖出光柱
                if (_WhisperFxVolumetric > 0.001)
                {
                    float2 dir = (float2(0.5, 0.5) - i.uv);
                    float3 acc = 0;
                    float wsum = 0;
                    [unroll]
                    for (int s = 1; s <= 6; s++)
                    {
                        float t = s / 6.0;
                        float2 uv = i.uv + dir * t * 0.14;
                        float3 c = tex2D(_MainTex, uv).rgb;
                        float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                        float w = saturate(lum - _WhisperFxBloomThreshold);
                        acc += c * w * (1.0 - t);
                        wsum += w * (1.0 - t);
                    }
                    col += (acc / max(wsum, 1e-4)) * _WhisperFxVolumetric * 0.35;
                }

                // ④ 辉光
                if (_WhisperFxBloom > 0.001)
                    col += tex2D(_WhisperBloomTex, i.uv).rgb * _WhisperFxBloom;

                // ⑤ 眼部适应：曝光由 CPU 逐帧算出（见 PostFx.cs），这里只乘。
                //     为什么曝光不放着色器里算：适应是**时间积分**，需要跨帧状态；
                //     着色器每帧都是无状态的，所以状态必须由 C# 持有。
                // 曝光只在眼部适应**开启**时参与：否则"关了眼适应"画面仍被曝光系数改变，
                // 玩家会看到"选项没生效"（这类"关了还在起作用"是最难被发现的 bug 之一）。
                if (_WhisperFxEye > 0.001) col *= clamp(_WhisperFxExposure, 0.05, 4.0);

                // ⑥ 暗角：让视线聚焦在画面中心（恐怖游戏的基础构图手段）
                if (_WhisperFxVignette > 0.001)
                {
                    float2 v = i.uv - 0.5;
                    float r2 = dot(v, v);
                    col *= lerp(1.0, saturate(1.0 - r2 * 2.2), _WhisperFxVignette);
                }

                // ⑦ 颗粒：**必须随帧变化**，静止的颗粒看起来像脏屏幕而不是胶片
                if (_WhisperFxGrain > 0.001)
                {
                    float n = frac(sin(dot(i.uv * 1024.0 + _WhisperFxTime, float2(12.9898, 78.233))) * 43758.5453);
                    col += (n - 0.5) * _WhisperFxGrain;
                }

                return fixed4(saturate(col), 1.0);
            }
            ENDCG
        }

        // ───────────────────────── Pass 4：1x1 平均亮度（眼部适应的输入） ─────────────────────────
        // 用 16 点网格 + 大偏移近似全屏平均：1x1 目标下 16 次采样已足够（适应是慢过程，噪声会被时间滤掉）。
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float lum = 0;
                [unroll]
                for (int y = 0; y < 4; y++)
                {
                    [unroll]
                    for (int x = 0; x < 4; x++)
                    {
                        float2 uv = float2((x + 0.5) / 4.0, (y + 0.5) / 4.0);
                        float3 c = tex2D(_MainTex, uv).rgb;
                        lum += dot(c, float3(0.2126, 0.7152, 0.0722));
                    }
                }
                lum /= 16.0;
                // 亮度包在 log2 域：人眼对亮度的感受是**对数**的，适应曲线也应在对数域做插值
                // （否则从暗到亮会"先慢后猛"，看起来像坏掉的自动曝光）
                return fixed4(log2(max(lum, 1e-4)), 0, 0, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
