// Project Whisper · 关卡几何专用 Unlit 着色器
//
// 为什么必须自带一个着色器（真机事故，2026-10-03）：
//   首次装机验证时 App 稳定 60fps 出帧，但屏幕全黑，logcat 里：
//       E Unity : [Whisper] 关卡装载失败（ArgumentNullException）：Value cannot be null.
//       E Unity : Parameter name: shader
//   根因：`LevelBuilder.FlatMaterial` 用 `Shader.Find("Standard")` 建材质，而本工程
//   **没有任何 .mat 资产引用 Standard**（几何全部运行时生成，V9 §19.1 C2），
//   于是 Standard 被剥离出包 → Shader.Find 返回 null → new Material(null) 抛异常
//   → 关卡几何一个都没建出来 → 全黑。
//
// 为什么放在 Resources/ 下：Resources 目录的内容**无条件进包**，不依赖
//   Graphics Settings 的 "Always Included Shaders"，也就不需要手写一个
//   容易出错的 ProjectSettings.asset。这是本机（无 Unity 编辑器）能给出的最强保证。
//
// 同时这也是**粉红测试**的载体：若本着色器仍加载失败，界面会变成洋红
//   （Unity 的 "shader error" 色），肉眼一看就知道，不会再出现"黑屏但没报错"。

Shader "Whisper/UnlitColor"
{
    Properties
    {
        // 必须叫 _Color：Material.color 以及 MaterialPropertyBlock 都写这个属性名
        _Color ("主色", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        // 不透明队列；先做最简可用版本（几何为盒体，无需深度排序技巧）
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual
        Blend One Zero

        Pass
        {
            // 目标 3.0：覆盖 Android 全系（含 GLES3 / Vulkan）
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            // 【真机实测修正】这里曾开 `#pragma multi_compile_fog` + UNITY_TRANSFER/APPLY_FOG。
            // 装机实测后果：整个 3D 视图被洗成一片均匀的 #D8CFBB（= ColorBone，安全区墙色），
            // 截屏判据测得「亮度 207/255 · 标准差 2.1 · 边缘密度 0.0%」——看着不黑，但等于什么都没看见。
            // 雾是**全局**效果，取值依赖 Lighting 设置（我们连 ProjectSettings 都没有，不可控），
            // 而本关卡只有 22m × 10m，根本不需要距离雾。去掉它 = 少一个不可控的全局变量。
            //
            // 刻意【不】写 UNITY_VERTEX_INPUT_INSTANCE_ID / UNITY_VERTEX_OUTPUT_STEREO /
            // UNITY_SETUP_INSTANCE_ID：它们必须与 #pragma multi_compile_instancing、
            // 单通道立体渲染等 pragma 配对使用，配不上时会在编译期报未定义符号。
            // 本工程不需要 GPU 实例化与 VR，所以把这些宏整体去掉——**少一个可能编不过的地方**，
            // 而这个着色器一旦编不过，真机就是洋红屏（也就是又一次"打不开"）。

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // 顶点色 × 主色：Cube 基元的顶点色是白，所以最终颜色 = 主色；
                // 将来接真美术网格（带烘焙顶点色）时无需改着色器。
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }

    // 兜底：万一进了不支持 CG 的管线，至少不是隐形物体
    Fallback Off
}
