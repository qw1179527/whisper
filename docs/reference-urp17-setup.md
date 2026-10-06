# URP 17 / Unity 6 落地参考（Project Whisper · Android 无编辑器 CI 路径）

> **文档定位**：本文是「把 Whisper 从 Built-in 渲染管线切到 URP 17，并且全部由 C# Editor 脚本在云 CI 完成」的**实现级**参考。
> 目标版本：Unity **6000.3.25f1** + `com.unity.render-pipelines.universal@17.0.3`。
> 语言约定：正文中文，标识符（类型名/属性名/代码）保持英文原样。

---

## 0. 证据等级与阅读约定（先读这一节）

| 标记 | 含义 |
|---|---|
| 【官】 | Unity 官方文档（`docs.unity3d.com` 手册或 Scripting API / 包 API）。链接直接给到该页。 |
| 【源码】 | Unity 公开源码仓库 `Unity-Technologies/Graphics`（Unity 官方 GitHub）。用于**官方文档没写**的序列化字段名等。注：`master` 分支可能略新于 17.0.3。 |
| 【工程】 | 本仓库文件的实际内容（可现场复核）。 |
| **UNKNOWN — needs editor validation** | 官方文档没有回答、且无法从公开源码确定的点。**不要猜**，必须在 CI 里跑一次编辑器验证。 |

**重要纪律**：凡是本文标注 **UNKNOWN** 的，不得直接写进构建脚本当作既成事实；应先用 `-executeMethod` 跑一个只读探测方法，把结果回执到 CI 日志。

**本项目硬约束（来自 `unity/README.md` 与 `unity/ProjectSettings/ProjectSettings-NOTE.md`）**：
- 本机（Android/Termux）**没有 Unity 编辑器**，`ProjectSettings/*.asset` 是编辑器生成的 YAML；
- 因此**禁止手写 `.asset`**，所有设置必须走 `-executeMethod` + C# API（现有先例：`unity/Assets/Editor/BuildConfigurator.cs` 对 `PlayerSettings` 就是这么做的）；
- 本文只描述**新增一个 Editor 脚本**的做法，不修改任何既有工程文件。

---

## 1. 现状诊断：为什么现在实际跑的是 Built-in Render Pipeline

这一节把「问题是什么」用工程文件原文钉死，避免后面讨论跑偏。

### 1.1 没有分配 Render Pipeline Asset（决定性证据）

`unity/ProjectSettings/GraphicsSettings.asset`：`m_CustomRenderPipeline: {fileID: 0}`【工程】

官方语义：**只要没有指定 render pipeline asset，Unity 就用 Built-in Render Pipeline**。
> "To specify which Scriptable Render Pipeline Unity uses, you use render pipeline assets. … **If you don't specify a render pipeline asset, Unity uses the Built-in Render Pipeline.**" — [Change or detect the active render pipeline](https://docs.unity3d.com/6000.3/Documentation/Manual/srp-setting-render-pipeline-asset.html)

`GraphicsSettings.defaultRenderPipeline` 的 API 说明同样原文：
> "If this value is null, the default render pipeline is the Built-in Render Pipeline." — [GraphicsSettings.defaultRenderPipeline](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.GraphicsSettings-defaultRenderPipeline.html)

**结论**：`Packages/manifest.json` 里有 `com.unity.render-pipelines.universal@17.0.3`【工程】只代表**包被下载/编译了**，不代表管线被启用。URP 包的存在与「使用 URP」是两件事。

### 1.2 质量档把灯和阴影全关了（Built-in 语义下）

`unity/ProjectSettings/QualitySettings.asset`【工程】：
- `m_CurrentQuality: 5`，第 6 档名为 `Very Low`；
- 该档 `pixelLightCount: 0`、`shadows: 0`、`shadowResolution: 0`、`antiAliasing: 0`、`lodBias: 0.3`；
- 该档 `customRenderPipeline: {fileID: 0}`（即该质量档**没有** URP override）。

**关键语义**：`pixelLightCount` / `shadows` / `shadowCascades` / `shadowDistance` 这一组是 **Built-in 管线的质量字段**。切到 URP 后，主光/附加光的阴影与级联由 **URP Asset** 决定，官方对照表就是这么写的：
> "Directional Light Cascade Shadows | Built-in: 1, 2, or 4 cascades, control by percentage only | **URP: 1 to 4 cascades. Control by percentage, or switch to meters. Settings are in URP asset.**" — [Render pipeline feature comparison](https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html)

所以「把 `shadows` 从 0 改成 2」在 URP 下**不解决问题**——必须改 URP Asset 的 `supportsMainLightShadows`（只读，见 §2.4）。

**UNKNOWN — needs editor validation**：官方文档没有一句明确说「URP 下 `QualitySettings.shadows` / `pixelLightCount` 被完全忽略」。已知的确定事实是 URP 的阴影/灯设置位于 URP Asset（上引对照表）；二者是否会在某些路径上叠加，需在编辑器里实测。

### 1.3 默认 Volume Profile 是空的

`unity/Assets/DefaultVolumeProfile.asset`【工程】：`components: []`，`m_Script` 指向 `UnityEngine.Rendering.VolumeProfile`。
即：**即使现在切到 URP，也不会有任何后处理**（没有 Bloom / Vignette / Color Adjustments / Tonemapping）。必须由脚本填充（见 §4.5）。

### 1.4 三个自研 shader 都是 Built-in 写法

`unity/Assets/Resources/Shaders/*.shader`【工程】：
- `WhisperLitPbr.shader`：`Tags { "LightMode" = "ForwardBase" }` + `Tags { "LightMode" = "ForwardAdd" }`，两段都是 `CGPROGRAM` + `#include "UnityCG.cginc"` / `"Lighting.cginc"` / `"AutoLight.cginc"`；
- 文件头自述：引擎侧是 **Built-in + Gamma 空间**（`m_ActiveColorSpace: 0`）；
- `WhisperPostFx.shader`（20,456 字节）与 `WhisperUnlitColor.shader`（12,818 字节）同属 legacy 体系。

**这一条是最危险的**：`ForwardAdd` 在 URP **不受支持**（官方 pass tag 表原文列出不支持清单），切管线后这些 pass 不会被执行：

> "**Note: URP does not support the following LightMode tags: `Always`, `ForwardAdd`, `PrepassBase`, `PrepassFinal`, `Vertex`, `VertexLMRGBM`, `VertexLM`.**" — [ShaderLab Pass tags in URP reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html)

### 1.5 URP 的 Global Settings 已经在了（说明工具链兼容）

`GraphicsSettings.asset` 里有 `m_RenderPipelineGlobalSettingsMap: UnityEngine.Rendering.Universal.UniversalRenderPipeline: {fileID: 11400000, guid: 544303567fc65c440a6fc1a3eead5209…}`【工程】，对应 `Assets/UniversalRenderPipelineGlobalSettings.asset`【工程】。这说明 URP 包已被正确导入、Global Settings 资产已生成——**缺的只是「创建并挂载 URP Asset + Renderer Data」这一步**。

---

## 2. 用 Editor 脚本创建并挂载 URP 资产（核心配方）

### 2.1 必须用到的类型与命名空间

| 类型 | 命名空间 | 程序集 | 用途 |
|---|---|---|---|
| `UniversalRenderPipelineAsset` | `UnityEngine.Rendering.Universal` | `Unity.RenderPipelines.Universal.Runtime.dll` | URP 总资产（画质档） |
| `UniversalRendererData` | `UnityEngine.Rendering.Universal` | `Unity.RenderPipelines.Universal.Runtime.dll` | 渲染器数据（渲染路径/特性列表） |
| `ScriptableRendererData` | `UnityEngine.Rendering.Universal` | `Unity.RenderPipelines.Universal.Runtime.dll` | `UniversalRendererData` 的基类 |
| `ScriptableRendererFeature` | `UnityEngine.Rendering.Universal` | `Unity.RenderPipelines.Universal.Runtime.dll` | 渲染特性基类 |
| `ScreenSpaceAmbientOcclusion` | `UnityEngine.Rendering.Universal` | 同上 | SSAO 特性 |
| `DecalRendererFeature` | `UnityEngine.Rendering.Universal` | 同上 | Decal 特性 |
| `FullScreenPassRendererFeature` | `UnityEngine.Rendering.Universal` | 同上 | 全屏自定义 pass 特性（低代码后处理） |
| `GraphicsSettings` | `UnityEngine.Rendering` | `UnityEngine.CoreModule` | `defaultRenderPipeline` |
| `QualitySettings` | `UnityEngine` | `UnityEngine.CoreModule` | `renderPipeline`（按质量档 override） |
| `AssetDatabase` / `SerializedObject` / `EditorUtility` | `UnityEditor` | `UnityEditor.CoreModule` | 资产创建与只读字段改写 |

命名空间/程序集来源：各类型 API 页顶部的 `Namespace:` / `Assembly:` 行，例如
[UniversalRenderPipelineAsset](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html)、
[UniversalRendererData](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRendererData.html)、
[ScriptableRendererData](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScriptableRendererData.html)。

### 2.2 官方工厂方法：`UniversalRenderPipelineAsset.Create(ScriptableRendererData)`

**这是本题最重要的一条 API**：URP 官方提供了静态工厂方法，不要自己 `CreateInstance` 拼装。

```
public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null)
```
— [UniversalRenderPipelineAsset.Create 文档](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html)

它在官方源码里做三件事（Unity 官方仓库原文）【源码】[UniversalRenderPipelineAsset.cs（master）](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAsset.cs)：

```csharp
public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null)
{
    var instance = CreateInstance<UniversalRenderPipelineAsset>();
    // Initialize default renderer data
    instance.m_RendererDataList[0] = (rendererData != null) ? rendererData : CreateInstance<UniversalRendererData>();
    // Only enable for new URP assets by default
    instance.m_ConservativeEnclosingSphere = true;
    ResourceReloader.ReloadAllNullIn(instance, packagePath);
    return instance;
}
```

注意两点：
1. 它会把 `rendererData` 写进 `m_RendererDataList[0]`；`rendererData` 传 `null` 时会**新建一个未保存的** `UniversalRendererData`（那时你还得自己 `AssetDatabase.CreateAsset` 存它）。
2. 它对新资产默认把 `m_ConservativeEnclosingSphere` 设为 `true`。官方手册对此的性能说明是正向的：
   > "Performance impact: Enabling this option is likely to improve performance, because the option minimizes the overlap of shadow cascades…" — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)

**Renderer Data 的默认资源不需要你手动塞**：`UniversalRendererData.OnEnable()` 内部会调用资源重载【源码】[UniversalRendererData.cs（master，第 400–414 行）](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Runtime/UniversalRendererData.cs)：

```csharp
protected override void OnEnable()
{
    base.OnEnable();
    ...
    ResourceReloader.TryReloadAllNullIn(this, UniversalRenderPipelineAsset.packagePath);
}
```
所以 `ScriptableObject.CreateInstance<UniversalRendererData>()` 在编辑器里会把默认 shader 资源填好。

> **UNKNOWN — needs editor validation**：`ResourceReloader` 属于 `UnityEditor.Rendering`（core 包），其公开 API 页在 core@17.0 文档中未检索到；不要在自己的脚本里直接调用它，依赖 `Create()` / `OnEnable()` 的行为即可。

### 2.3 完整可运行代码（可直接放进 `Assets/Editor/`）

> 说明：这是**新增文件**，不改动 `BuildConfigurator.cs`。放在 `Assets/Editor/` 下即属于 Editor 程序集，可被 `-executeMethod` 调用。

```csharp
// Assets/Editor/UrpSetup.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Whisper.Editor
{
    /// <summary>
    /// URP 17 的「代码真源」：创建并挂载 UniversalRenderPipelineAsset + UniversalRendererData。
    /// 幂等：已存在则复用（按 GUID 定位），不重复创建。
    /// 调用：Unity -quit -batchmode -projectPath unity \
    ///        -executeMethod Whisper.Editor.UrpSetup.ConfigureUrp
    /// </summary>
    public static class UrpSetup
    {
        const string SettingsDir    = "Assets/Settings";
        const string RendererPath   = SettingsDir + "/WhisperUniversalRenderer.asset";
        const string PipelinePath   = SettingsDir + "/WhisperURPAsset.asset";

        [MenuItem("Whisper/配置 URP 渲染管线")]
        public static void ConfigureUrp()
        {
            if (!AssetDatabase.IsValidFolder(SettingsDir))
                AssetDatabase.CreateFolder("Assets", "Settings");

            // ① Renderer Data（不存在才建）——用 GUID 定位，避免路径改名即重复创建
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                // OnEnable 会通过 ResourceReloader 填好默认 shader 资源（见 §2.2）。
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                AssetDatabase.SaveAssets();
            }

            // ② URP Asset（不存在才建）——必须用官方工厂方法
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, PipelinePath);
                AssetDatabase.SaveAssets();
            }

            ConfigurePipelineSettings(urp);   // 见 §2.4（含只读字段）
            ConfigureRendererData(rendererData); // 见 §3.7

            EditorUtility.SetDirty(urp);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ③ 挂载为默认管线 + 当前质量档 override
            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;

            // ④ 自检：currentRenderPipeline 是唯一可信的「生效」证据
            var active = GraphicsSettings.currentRenderPipeline;
            if (active == null)
                throw new System.InvalidOperationException(
                    "URP 未生效：GraphicsSettings.currentRenderPipeline == null（仍是 Built-in）");
            Debug.Log($"[UrpSetup] active render pipeline = {active.name} ({active.GetType().FullName})");
        }

        static void ConfigurePipelineSettings(UniversalRenderPipelineAsset urp) { /* §2.4 */ }
        static void ConfigureRendererData(UniversalRendererData data) { /* §3.7 */ }
    }
}
```

**若干 API 的官方出处**：
- `AssetDatabase.CreateAsset(Object asset, string path)` — [文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.CreateAsset.html)
- `AssetDatabase.AddObjectToAsset(Object objectToAdd, Object assetObject)`（子资产；见 §4.5 的 VolumeComponent） — [文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.AddObjectToAsset.html)
- `AssetDatabase.SaveAssets()` — [文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.SaveAssets.html)
- `EditorUtility.SetDirty(Object)` — [文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorUtility.SetDirty.html)
- `SerializedObject` / `SerializedObject.FindProperty(string)` — [SerializedObject](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SerializedObject.html) / [FindProperty](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SerializedObject.FindProperty.html)

### 2.4 只读属性的坑：哪些能直接赋值，哪些必须走 `SerializedObject`

URP 17 的 `UniversalRenderPipelineAsset` **大量属性只有 getter**。API 页的 `Declaration` 行会明确写 `{ get; }` 还是 `{ get; set; }`。

**【可直接赋值】**（API 页为 `{ get; set; }`）：

| 属性 | 类型 | 备注 |
|---|---|---|
| `renderScale` | `float` | 渲染缩放 |
| `msaaSampleCount` | `int` | 1/2/4/8 |
| `upscalingFilter` | `UpscalingFilterSelection` | Auto/Linear/Point/FSR/STP |
| `fsrOverrideSharpness` / `fsrSharpness` | `bool` / `float` | FSR 锐化 |
| `supportsHDR` | `bool` | HDR |
| `hdrColorBufferPrecision` | `HDRColorBufferPrecision` | 32/64 bit |
| `supportsCameraDepthTexture` | `bool` | `_CameraDepthTexture` |
| `supportsCameraOpaqueTexture` | `bool` | `_CameraOpaqueTexture`（折射用） |
| `shadowDistance` | `float` | 最大阴影距离 |
| `shadowCascadeCount` | `int` | 1–4 |
| `cascade2Split` / `cascade3Split` / `cascade4Split` / `cascadeBorder` | `float` / `Vector2` / `Vector3` / `float` | 级联切分 |
| `shadowDepthBias` / `shadowNormalBias` | `float` | |
| `mainLightShadowmapResolution` / `additionalLightsShadowmapResolution` | `int` | |
| `maxAdditionalLightsCount` | `int` | |
| `colorGradingMode` / `colorGradingLutSize` | `ColorGradingMode` / `int` | |
| `useSRPBatcher` / `supportsDynamicBatching` | `bool` | |
| `volumeProfile` | `VolumeProfile` | 默认 Volume Profile（见 §4.5） |
| `conservativeEnclosingSphere` / `numIterationsEnclosingSphere` | `bool` / `int` | |
| `enableLODCrossFade` / `lodCrossFadeDitheringType` | `bool` / `LODCrossFadeDitheringType` | |
| `useAdaptivePerformance` | `bool` | |
| `gpuResidentDrawerMode` | `GPUResidentDrawerMode` | |
| `smallMeshScreenPercentage` | `float` | |
| `storeActionsOptimization` | `StoreActionsOptimization` | 见下方源码注 |

**【只读！必须 SerializedObject】**（API 页为 `{ get; }`）：

`supportsMainLightShadows`、`supportsAdditionalLightShadows`、`supportsSoftShadows`、`supportsLightCookies`、`supportsMixedLighting`、`useRenderingLayers`、`mainLightRenderingMode`、`additionalLightsRenderingMode`、`opaqueDownsampling`、`additionalLightsShadowResolutionTierLow/Medium/High`、`reflectionProbeBlending`、`reflectionProbeBoxProjection`、`supportScreenSpaceLensFlare`、`supportDataDrivenLensFlare`、`allowPostProcessAlphaOutput`、`volumeFrameworkUpdateMode`、**`rendererDataList`**（`ReadOnlySpan<ScriptableRendererData>`）。

— 全部逐条来自 [UniversalRenderPipelineAsset API 页](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html) 的 `Declaration` 行。

**对应的序列化字段名**（官方文档没有列出属性↔字段映射，以下取自 Unity 官方 Graphics 仓库）【源码】[UniversalRenderPipelineAsset.cs](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAsset.cs)：

| 属性（只读） | 序列化字段名 |
|---|---|
| `supportsMainLightShadows` | `m_MainLightShadowsSupported` |
| `supportsAdditionalLightShadows` | `m_AdditionalLightShadowsSupported` |
| `supportsSoftShadows` | `m_SoftShadowsSupported` |
| （软阴影质量档） | `m_SoftShadowQuality`（`SoftShadowQuality.Low/Medium/High`） |
| `supportsLightCookies` | `m_SupportsLightCookies` |
| `useRenderingLayers` | `m_SupportsLightLayers` |
| `mainLightRenderingMode` | `m_MainLightRenderingMode` |
| `additionalLightsRenderingMode` | `m_AdditionalLightsRenderingMode` |
| （附加光每物体上限） | `m_AdditionalLightsPerObjectLimit` |
| `supportsMixedLighting` | `m_MixedLightingSupported` |
| `opaqueDownsampling` | `m_OpaqueDownsampling` |
| `supportsCameraDepthTexture` | `m_RequireDepthTexture` |
| `supportsCameraOpaqueTexture` | `m_RequireOpaqueTexture` |
| `volumeFrameworkUpdateMode` | `m_VolumeFrameworkUpdateMode` |
| `volumeProfile` | `m_VolumeProfile` |
| `allowPostProcessAlphaOutput` | `m_AllowPostProcessAlphaOutput` |
| `supportScreenSpaceLensFlare` | `m_SupportScreenSpaceLensFlare` |
| `supportDataDrivenLensFlare` | `m_SupportDataDrivenLensFlare` |
| `colorGradingMode` / `colorGradingLutSize` | `m_ColorGradingMode` / `m_ColorGradingLutSize` |
| `msaaSampleCount` / `renderScale` / `upscalingFilter` | `m_MSAA` / `m_RenderScale` / `m_UpscalingFilter` |
| `shadowDistance` / `shadowCascadeCount` | `m_ShadowDistance` / `m_ShadowCascadeCount` |
| **渲染器列表** | `m_RendererDataList`（`ScriptableRendererData[]`）、`m_DefaultRendererIndex`、`m_RendererData` |

⚠️ **源码注（版本漂移风险）**：`master` 分支里 `m_StoreActionsOptimization` 已被标注 `[Obsolete("#from(6000.0) #breakingFrom(6000.4)", true)]`【源码】。所以**字段名会随版本变化**，本文的字段名表只能当作「起点」，最终必须做一次下面的**字段名发现**。

**字段名发现 + 只读改写（推荐做法，抗版本漂移）**：

```csharp
// 先把真实字段名打到 CI 日志里（一次性诊断，随后据此填表）
static void DumpSerializedFields(Object asset, string label)
{
    var so = new SerializedObject(asset);
    var it = so.GetIterator();
    var sb = new System.Text.StringBuilder();
    while (it.NextVisible(true))
        sb.AppendLine($"{it.propertyPath}  ({it.propertyType})");
    Debug.Log($"[UrpSetup] ---- {label} serialized fields ----\n{sb}");
}

// 只读字段的通用写法：找不到就 FAIL，绝不静默跳过（避免假绿）
static void SetSerialized(Object asset, string propertyPath, System.Action<SerializedProperty> set)
{
    var so = new SerializedObject(asset);
    var p  = so.FindProperty(propertyPath);
    if (p == null)
        throw new System.InvalidOperationException(
            $"序列化字段不存在: {propertyPath}（版本漂移？先跑 DumpSerializedFields）");
    set(p);
    so.ApplyModifiedPropertiesWithoutUndo();
    EditorUtility.SetDirty(asset);
}
```

`SerializedObject.ApplyModifiedPropertiesWithoutUndo()` 属于 `UnityEditor`（[SerializedObject 文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SerializedObject.html)）。

**挂渲染器列表（`rendererDataList` 只读 → 走字段）**：

```csharp
static void AssignRendererData(UniversalRenderPipelineAsset urp, UniversalRendererData data)
{
    var so = new SerializedObject(urp);
    var list = so.FindProperty("m_RendererDataList");
    if (list == null) throw new System.InvalidOperationException("m_RendererDataList 不存在（版本漂移）");
    list.arraySize = Mathf.Max(1, list.arraySize);
    list.GetArrayElementAtIndex(0).objectReferenceValue = data;
    var idx = so.FindProperty("m_DefaultRendererIndex");
    if (idx != null) idx.intValue = 0;
    so.ApplyModifiedPropertiesWithoutUndo();
    EditorUtility.SetDirty(urp);
}
```

### 2.5 挂载语义：default vs per-quality-level override

官方语义（必须搞对，否则「脚本跑了但没生效」）：

> "For each quality level in the Quality settings window, Unity uses the render pipeline asset assigned to **Render Pipeline Asset**. If the property is unassigned, Unity uses the render pipeline asset assigned to **Default Render Pipeline Asset** in the Graphics settings window instead. **If both … aren't set, Unity uses the Built-In Render Pipeline.**" — [Change or detect the active render pipeline](https://docs.unity3d.com/6000.3/Documentation/Manual/srp-setting-render-pipeline-asset.html)

- `GraphicsSettings.defaultRenderPipeline` = Graphics 设置里的「Default Render Pipeline」；
- `QualitySettings.renderPipeline` = **当前质量档**的 override（`QualitySettings.renderPipeline` 的 API 原文："The RenderPipelineAsset that defines the override render pipeline for **the current quality level**" — [文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/QualitySettings-renderPipeline.html)）；
- 只读校验用 `GraphicsSettings.currentRenderPipeline`（"To get a reference to the render pipeline asset that defines the active render pipeline, use `GraphicsSettings.currentRenderPipeline`" — [同上手册](https://docs.unity3d.com/6000.3/Documentation/Manual/srp-setting-render-pipeline-asset.html)；API：[currentRenderPipeline](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.GraphicsSettings-currentRenderPipeline.html)）。

**本项目必须两个都设**：因为 `m_CurrentQuality: 5`（Very Low）【工程】，如果只设 default，而某个质量档残留了 override，就以 override 为准；如果只设 override，则其它档位仍走 default（null → Built-in）。最稳的写法是**所有质量档都显式赋同一个 URP Asset**：

```csharp
static void AssignToAllQualityLevels(UniversalRenderPipelineAsset urp)
{
    int original = QualitySettings.GetQualityLevel();
    int count    = QualitySettings.names.Length;
    for (int i = 0; i < count; i++)
    {
        QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
        QualitySettings.renderPipeline = urp;   // 写的是「当前档」的 override
    }
    QualitySettings.SetQualityLevel(original, false);
}
```

> **UNKNOWN — needs editor validation**：官方文档没有明确写「`QualitySettings.renderPipeline` 的 setter 会把值写进 `QualitySettings.asset` 的对应档位并跨会话持久化」，也没有提供 `SetRenderPipeline(level, asset)` 这类按档位索引的 API。上面的循环是**依据 API 语义的推断写法**，必须在 CI 里跑一次后 `git diff ProjectSettings/QualitySettings.asset` 确认每档 `customRenderPipeline` 都写对了。

### 2.6 Unity 6 相对旧版本的变化（易踩的点）

| 变化 | 官方依据 |
|---|---|
| URP 17 引入 **Render graph**，自定义 `ScriptableRenderPass` 必须用 render graph API 重写；为兼容可开 `Project Settings > Graphics > Render Graph > Compatibility Mode (Render Graph Disabled)` | [Upgrade to URP 17 (Unity 6.0)](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-unity-6.html) |
| URP 17 起 `VolumeComponent.Override(...)` 的实现**必须**在改变参数时把 `VolumeParameter.overrideState` 置 `true`（性能/正确性） | [同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-unity-6.html) |
| `ScriptableRenderer.cameraColorTarget` / `cameraDepthTarget` → `cameraColorTargetHandle` / `cameraDepthTargetHandle`（RTHandle 化） | [Upgrade to URP 16（2022.2 指南同文）](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-2022-2.html) |
| 渲染特性不再「只要有特性就走中间纹理」：必须用 `ScriptableRenderPass.ConfigureInput` 声明输入，或把 Universal Renderer 的 **Intermediate Texture** 设为 `Always` | [同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-2022-2.html) / [Universal Renderer 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html) |
| URP 17 新增 STP 上采样、GPU Resident Drawer、GPU occlusion culling、Camera history API | [What's new in URP 17](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/whats-new/urp-whats-new.html) |

### 2.7 CI（`-executeMethod`）注意事项

1. **方法必须是 `public static`，且不能抛异常**——抛异常会让批处理返回非 0 退出码（本项目 `BuildScript.cs` 已用这个模式）。因此 §2.3 里用 `throw` 做自检是**故意**的：宁可不绿，不可假绿。
2. **要把「生效」写进日志**：打印 `GraphicsSettings.currentRenderPipeline` 的 `name` 与类型，这是唯一能证明「不是 Built-in」的证据（官方定义见 §2.5）。
3. **ProjectSettings 的落盘**：在编辑器里改 `GraphicsSettings` / `QualitySettings` 等价于改 Project Settings 窗口（`GraphicsSettings.defaultRenderPipeline` 的 API 原文："In the Unity Editor, this property corresponds to the Render Pipeline Setting field in the Graphics Settings window" — [文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.GraphicsSettings-defaultRenderPipeline.html)）。
   > **UNKNOWN — needs editor validation**：官方文档没有说明「`-quit -batchmode` 下 ProjectSettings 何时写盘、进程被 kill 是否会丢」。稳妥做法：脚本末尾 `AssetDatabase.SaveAssets()`，并在 CI 里构建完成后 `git diff --stat unity/ProjectSettings/` 断言 `GraphicsSettings.asset` 与 `QualitySettings.asset` 真的变了。
4. **资产路径必须进版本库**：`Assets/Settings/*.asset` 是新生成的资产，需要 `.meta` 一起提交；否则下次 CI 每个 runner 都会重新生成不同 GUID 的资产。
5. 编辑器脚本目录：`Assets/Editor/` 下的脚本进 `Assembly-CSharp-Editor`，可被 `-executeMethod` 调用（现有 `BuildConfigurator.cs` 即此模式【工程】）。

---

## 3. Universal Renderer Data 特性（恐怖游戏相关）

### 3.1 渲染路径：`RenderingMode`

```
namespace UnityEngine.Rendering.Universal
public enum RenderingMode { Deferred, Forward, ForwardPlus }
```
— [RenderingMode API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.RenderingMode.html)

| 值 | API 页原文描述 |
|---|---|
| `Forward` | "Render all objects and lighting in one pass, with a hard limit on the number of lights that can be applied on an object." |
| `ForwardPlus` | "Render all objects and lighting in one pass using a clustered data structure to access lighting data." |
| `Deferred` | "Render all objects first in a g-buffer pass, then apply all lighting in a separate pass using deferred shading." |

**版本分歧（要点）**：URP 手册的 Universal Renderer 参考里列了 **四个** 选项 `Forward / Forward+ / Deferred / Deferred+`（[Universal Renderer asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html)），但 **URP 17.0 的 API 枚举页只列出三个成员**（Deferred / Forward / ForwardPlus）[同上]。此外官方源码里存在 `RenderingMode.DeferredPlus` 的判断分支【源码】[UniversalRendererData.cs](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Runtime/UniversalRendererData.cs)（`usesDeferredLighting => m_RenderingMode == Deferred || m_RenderingMode == DeferredPlus`）。
> **UNKNOWN — needs editor validation**：`DeferredPlus` 在 6000.3.25f1 + URP 17.0.3 的 C# 枚举里到底存不存在（API 页 vs 手册 vs 源码三处不一致）。要用它之前先在 CI 里 `Debug.Log(System.Enum.GetNames(typeof(RenderingMode)))`。

**移动端结论（工程建议）**：用 `Forward`（默认值，源码 `m_RenderingMode = RenderingMode.Forward`【源码】）或 `ForwardPlus`；**不要用 Deferred**——官方性能页明确把「内存带宽」列为移动端大敌，而 Deferred 需要 G-buffer（多张 RT），且开启 Rendering Layers 还会再多一张 RT：
> "If you use the Deferred rendering path, disable Use Rendering Layers so that URP doesn't create an extra render target." — [Configure for better performance in URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html)

`RenderingMode` 是**可写属性**：`public RenderingMode renderingMode { get; set; }` — [UniversalRendererData API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRendererData.html)（序列化字段名 `m_RenderingMode`【源码】）。

### 3.2 `UniversalRendererData` 的全部公开属性（可写性逐条标注）

全部来自 [UniversalRendererData API 页](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRendererData.html)：

| 属性 | 类型 | 可写 | 序列化字段【源码】 |
|---|---|---|---|
| `renderingMode` | `RenderingMode` | ✅ | `m_RenderingMode` |
| `depthPrimingMode` | `DepthPrimingMode` | ✅ | `m_DepthPrimingMode` |
| `copyDepthMode` | `CopyDepthMode` | ✅ | `m_CopyDepthMode` |
| `intermediateTextureMode` | `IntermediateTextureMode` | ✅ | `m_IntermediateTextureMode` |
| `opaqueLayerMask` / `transparentLayerMask` | `LayerMask` | ✅ | — |
| `accurateGbufferNormals` | `bool` | ✅ | — |
| `depthAttachmentFormat` / `depthTextureFormat` | `DepthFormat` | ✅ | — |
| `shadowTransparentReceive` | `bool` | ✅ | — |
| `defaultStencilState` | `StencilStateData` | ✅ | — |

### 3.3 Depth Priming（深度预判）

```
public enum DepthPrimingMode { Auto, Disabled, Forced }
```
— [DepthPrimingMode API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.DepthPrimingMode.html)

**Android 上的硬限制（官方原文）**：
> "**Auto**: Performs depth priming only if a depth prepass already exists in the render pipeline. **This setting isn't supported on Android, iOS and Apple TV platforms.**"
> "Note: Depth priming isn't supported if you use a deferred rendering path or Multisample Anti-aliasing, or at runtime on mobile devices that use tile-based deferred rendering (TBDR)."
— [Universal Renderer asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html)

**并且**：用 Depth Priming 就必须给自定义 shader 补 `DepthOnly` / `DepthNormals` pass，否则物体被画成不可见：
> "Note: If you use custom shaders, Unity renders opaque objects as invisible unless you add passes with DepthOnly and DepthNormals tags." — [同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html)

→ **工程建议**：Whisper 的自制 shader 目前**没有** `DepthOnly`/`DepthNormals` pass【工程】（见 §1.4/§9），所以**在 shader 补齐之前，`depthPrimingMode` 应保持 `Disabled`**。Android 上 `Auto` 反正也不支持。

### 3.4 Depth Texture 模式（移动端重要）

```
public enum CopyDepthMode { AfterOpaques, AfterTransparents, ForcePrepass }
```
— [CopyDepthMode API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.CopyDepthMode.html)

官方移动端提示（**对恐怖游戏的雾/水下/SSAO 都相关**）：
> "**Note: On mobile devices, the After Transparents option can lead to a significant improvement in memory bandwidth.** This is because the Copy Depth pass causes a switch in render target between the Opaque pass and the Transparents pass… The impact increases significantly when MSAA is enabled…" — [Universal Renderer asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html)

（源码默认值即 `CopyDepthMode.AfterTransparents`【源码】[UniversalRendererData.cs](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Runtime/UniversalRendererData.cs)。）

### 3.5 Rendering Layers（灯光只照某些物体）

- 开启开关属性：`useRenderingLayers`（URP Asset 上，**只读**，字段 `m_SupportsLightLayers`）；
- 官方说明："With this option selected, you can configure certain Lights to affect only specific GameObjects." — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)；
- 完整用法：[Rendering Layers（features/rendering-layers）](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/features/rendering-layers.html)；
- 代价：Deferred 下会多一张 render target（见 §3.1 引文）。
- **与 Forward+ 的关系（易错）**：Forward+ 是「每 tile 的灯列表」结构，而 Rendering Layers 是**选择组**（"Rendering layers don't define draw order. They're selection groups you assign objects to." — [同上 rendering-layers 页](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/features/rendering-layers.html)）。两者不是同一机制。
- **恐怖游戏价值**：手电筒只照「可被照亮的层」、鬼影只被「鬼影层」灯影响——这是零 GPU 成本的玩法向光照控制（对比：附加光数量/阴影才是成本项）。

### 3.6 阴影（URP Asset 侧，不在 Renderer Data）

| 设置 | 属性/字段 | 可写 | 官方要点 |
|---|---|---|---|
| 主光阴影 | `supportsMainLightShadows` / `m_MainLightShadowsSupported` | ❌ 只读 | — |
| 附加光阴影 | `supportsAdditionalLightShadows` / `m_AdditionalLightShadowsSupported` | ❌ 只读 | "Disable" → 更省（[optimize 页](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/optimize-for-better-performance.html)） |
| 最大阴影距离 | `shadowDistance` / `m_ShadowDistance` | ✅ | "reduce Max Distance so that URP processes fewer objects in the shadow pass"（[configure 页](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html)） |
| 级联数 | `shadowCascadeCount` / `m_ShadowCascadeCount` | ✅ | "reduce Cascade Count to reduce the number of render passes"（[同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html)） |
| 软阴影 | `supportsSoftShadows` / `m_SoftShadowsSupported` | ❌ 只读 | **移动端高开销**（见下） |
| 软阴影质量 | （只读）`m_SoftShadowQuality` | ❌ | `Low` = 4 PCF taps，官方称"good balance … for mobile platforms" |

**软阴影的官方性能警告原文**（这条对 Android 尤其关键）：
> "**Soft Shadows** … **Performance impact: High impact on platforms that use tile-based rendering, such as mobile platforms and untethered XR platforms.** When this option is disabled, Unity samples the shadow map once with the default hardware filtering."
> "**Quality** — Low: good balance of quality and performance for **mobile platforms**. Filtering method: 4 PCF taps. Medium: … 5x5 tent filter. This is the default value." — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)

→ **工程建议**：恐怖游戏想要「软边阴影」的观感，但移动端官方明说是高开销。折中：**开启软阴影 + 质量档 `Low`（4 taps）**，并把级联数压到 1–2、`shadowDistance` 压到 15–25 m（室内恐怖场景本来就短视距）。**数值本身是工程判断，不是官方推荐值**；官方只给了「调小/调低」的方向。

**级联设置的 API 细节**：`cascade2Split`（`float`）、`cascade3Split`（`Vector2`）、`cascade4Split`（`Vector3`）、`cascadeBorder`（`float`）都是可写属性（[API 页](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html)），含义见 [URP asset reference 的 Shadows 段](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)。

### 3.7 渲染特性（Renderer Features）：类名、创建、挂载

URP 17.0 中存在的内置特性类（`UnityEngine.Rendering.Universal` 命名空间）【官】[URP 17.0 API 命名空间列表](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.html)：

| 特性类 | 说明（API 页原文） | 恐怖游戏用途 |
|---|---|---|
| `ScreenSpaceAmbientOcclusion` | "The class for the SSAO renderer feature." | 墙角/缝隙变暗，廉价提升压迫感 |
| `DecalRendererFeature` | "The class for the decal renderer feature." | 血迹、涂鸦、弹孔、水渍 |
| `RenderObjects` | "The class for the render objects renderer feature." | 分层覆盖材质（如"鬼影可见"） |
| `FullScreenPassRendererFeature` | "This renderer feature lets you create single-pass full screen post processing effects without needing to write code." | 神光/径向模糊、胶片损伤、自定义全屏 FX |
| `ScreenSpaceAmbientOcclusion` 配套 | — | — |

**注意：URP 17 里没有名为 `ScreenSpaceShadows` 的特性类**（API 命名空间列表里检索不到；`@17.0/api/UnityEngine.Rendering.Universal.ScreenSpaceShadows.html` 返回 404）。手册侧边栏存在 "Add screen space shadows in URP" 条目（见 [SSAO 落地页](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/post-processing-ssao-landing.html) 的导航），但其正文 URL 未在官方站内检索到。
> **UNKNOWN — needs editor validation**：Unity 6.3 的 "Screen Space Shadows" 究竟是独立特性、还是挂在 SSAO 特性内的一个选项。**以编辑器 Inspector 为准**。

**从代码创建并挂载特性（唯一可行路径）**：

`ScriptableRendererData` 暴露：
```
public List<ScriptableRendererFeature> rendererFeatures { get; }                 // 只读属性，但 List 可变
public bool useNativeRenderPass { get; set; }
public void SetDirty()                                                          // "Use SetDirty when changing settings in the ScriptableRendererData."
public bool TryGetRendererFeature<T>(out T rendererFeature) where T : ScriptableRendererFeature
```
— [ScriptableRendererData API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScriptableRendererData.html)

```csharp
static T EnsureFeature<T>(UniversalRendererData data, string featureName) where T : ScriptableRendererFeature
{
    // ① 幂等：已存在就复用，避免每次 CI 都堆一个
    if (data.TryGetRendererFeature<T>(out var existing))
        return existing;

    // ② 创建：ScriptableObject.CreateInstance 会触发特性自己的 Create()
    var feature = ScriptableObject.CreateInstance<T>();
    feature.name = featureName;

    // ③ 作为「子资产」挂进 renderer data 文件（不是独立 .asset！）
    data.rendererFeatures.Add(feature);
    AssetDatabase.AddObjectToAsset(feature, data);

    data.SetDirty();
    EditorUtility.SetDirty(data);
    return feature;
}

static void ConfigureRendererData(UniversalRendererData data)
{
    data.renderingMode        = RenderingMode.Forward;          // 移动端保守选择（§3.1）
    data.depthPrimingMode     = DepthPrimingMode.Disabled;      // shader 没补 DepthOnly 前不要开（§3.3）
    data.copyDepthMode        = CopyDepthMode.AfterTransparents; // 移动端带宽友好（§3.4）
    data.intermediateTextureMode = IntermediateTextureMode.Auto; // 只在必要时走中间纹理
    data.shadowTransparentReceive = false;

    EnsureFeature<ScreenSpaceAmbientOcclusion>(data, "Whisper SSAO");
    EnsureFeature<DecalRendererFeature>(data, "Whisper Decals");

    EditorUtility.SetDirty(data);
    AssetDatabase.SaveAssets();
}
```
（`AssetDatabase.AddObjectToAsset` 的官方语义：[文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.AddObjectToAsset.html)；`ScriptableRendererFeature.SetActive(bool)` 来自基类继承列表，见 [ScriptableRendererFeature API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScriptableRendererFeature.html)。）

⚠️ **特性的参数没有公开属性**：`ScreenSpaceAmbientOcclusion` / `DecalRendererFeature` 的 API 页只暴露 `Create()` / `AddRenderPasses()` / `SetupRenderPasses()` / `OnCameraPreCull()` / `Dispose()` 这些 override（[SSAO API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion.html)、[Decal API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.DecalRendererFeature.html)），**参数是私有 `[SerializeField]`**。
→ 要改 SSAO 的强度/半径、Decal 的技术选型（DBuffer / Screen Space / Forward+），只能：① 用 `SerializedObject`（先 `DumpSerializedFields` 拿字段名）；② 或者预先在编辑器里做好 **Preset** 资产（`ProjectSettings/PresetManager.asset`）再套用。
> **UNKNOWN — needs editor validation**：这些特性的具体序列化字段名（本轮未从官方文档获得）。不要手写猜测名，跑 `DumpSerializedFields`。

### 3.8 Decals 的官方限制

> "| Decals | Built-in: No | **URP: Yes — Affect opaque objects only.** | HDRP: Yes — Affect opaque and transparent objects. |" — [Render pipeline feature comparison](https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html)

并且官方性能页建议「少用」：
> "Minimize the use of the Decal Renderer Feature, because URP creates an additional render pass to render decals." — [Configure for better performance in URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html)

用法（组件与投影器）：`DecalProjector`（`UnityEngine.Rendering.Universal`）+ [Decal Renderer Feature 文档](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-feature-decal.html)。

### 3.9 SSAO 的官方落地步骤

> "URP implements the Screen Space Ambient Occlusion (SSAO) effect as a Renderer Feature. … 1. Use the steps on How to add a Renderer Feature to a Renderer and add in Step 2, select **Screen Space Ambient Occlusion** Renderer Feature. 2. Configure the properties…" — [Add an SSAO Renderer Feature to a URP Renderer](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/add-ssao-renderer-feature-to-renderer.html)

⚠️ **SSAO 与自定义 shader 的耦合**：若走 Deferred + `UniversalForwardOnly`，必须补 `DepthNormalsOnly` pass，否则「Unity does not generate the ambient occlusion around the Mesh」：
> "**DepthNormalsOnly** … In the Deferred Rendering Path, if the Pass with the DepthNormalsOnly tag value is missing, Unity does not generate the ambient occlusion around the Mesh." — [ShaderLab Pass tags in URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html)

---

## 4. URP Volume 后处理组件：精确类名与参数名

### 4.1 总览（URP 官方效果清单）

URP 官方列出的 Volume Override 共 18 个（含 Color Curves / Color Lookup）：
Bloom、Channel Mixer、Chromatic Aberration、Color Adjustments、Color Curves、Color Lookup、Depth of Field、Film Grain、Lens Distortion、Lift Gamma Gain、Motion Blur、Panini Projection、**Screen Space Lens Flare**、Shadows Midtones Highlights、Split Toning、Tonemapping、Vignette、White Balance。
— [Post-processing Volume Overrides reference for URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/EffectList.html)

全部类位于 `UnityEngine.Rendering.Universal`，程序集 `Unity.RenderPipelines.Universal.Runtime.dll`（各 API 页顶部 `Namespace:` / `Assembly:` 行）。全部继承 `VolumeComponent`（`UnityEngine.Rendering`，core 包）。

### 4.2 逐组件参数表（参数名 = C# 字段名，可直接在代码里赋值）

> 下表每一行的字段名与类型，均来自对应 API 页的 `Fields` 段。链接给出各页 URL；`VolumeParameter<T>` 类型来自 core 包（如 `ClampedFloatParameter`、`MinFloatParameter`、`ColorParameter`、`Vector2Parameter`、`Vector4Parameter`、`BoolParameter`、`TextureParameter`、`ClampedIntParameter`）。

| 组件类 | 字段（参数）| 类型 | URP 支持 | 备注/限制 |
|---|---|---|---|---|
| `Bloom` | `intensity` | `MinFloatParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.Bloom.html) |
| | `threshold`, `clamp`, `dirtIntensity`, `scatter` | `MinFloatParameter` | | `scatter` 用于「柔光/雾感」；移动端成本高（多次降采样） |
| | `tint` | `ColorParameter` | | |
| | `dirtTexture` | `TextureParameter` | | 镜头脏污（恐怖游戏很吃这套） |
| | `downscale` | `DownscaleParameter` | | `BloomDownscaleMode`（Quarter/Half）→ 性能旋钮 |
| | `highQualityFiltering` | `BoolParameter` | | 关闭省 GPU |
| | `maxIterations` | `ClampedIntParameter` | | |
| `Vignette` | `color` | `ColorParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.Vignette.html) |
| | `center` | `Vector2Parameter` | | |
| | `intensity`, `smoothness` | `ClampedFloatParameter` | | |
| | `rounded` | `BoolParameter` | | |
| `FilmGrain` | `type` | `FilmGrainLookupParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.FilmGrain.html) |
| | `intensity`, `response` | `ClampedFloatParameter` | | 恐怖片颗粒感 |
| | `texture` | `NoInterpTextureParameter` | | 自定义颗粒图（`NoInterp` = 不做插值混合） |
| `ChromaticAberration` | `intensity` | `ClampedFloatParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ChromaticAberration.html) |
| | （URP 17 **只有** `intensity` 一个参数） | | | HDRP 侧参数更多；不要照抄 HDRP 教程 |
| `ColorAdjustments` | `postExposure` | `FloatParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ColorAdjustments.html) |
| | `contrast`, `hueShift`, `saturation` | `ClampedFloatParameter` | | |
| | `colorFilter` | `ColorParameter` | | 冷/暖整体偏移 |
| `Tonemapping` | `mode` | `TonemappingModeParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.Tonemapping.html) |
| | `neutralHDRRangeReductionMode` | `NeutralRangeReductionModeParameter` | | Neutral 模式的细分 |
| | `acesPreset` | `HDRACESPresetParameter` | | ACES 预设 |
| | `detectBrightnessLimits`, `detectPaperWhite` | `BoolParameter` | | HDR 输出相关 |
| | `minNits`, `maxNits`, `paperWhite`, `hueShiftAmount` | `ClampedFloatParameter` | | HDR 显示器相关 |
| `DepthOfField` | `mode` | `DepthOfFieldModeParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.DepthOfField.html)；官方对照表：URP 支持 Bokeh **与 Gaussian**，Built-in/HDRP 只 Bokeh → [对照表](https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html) |
| | `focusDistance`, `gaussianStart`, `gaussianEnd` | `MinFloatParameter` | | Gaussian 档更省 |
| | `aperture`, `focalLength` | `ClampedFloatParameter`（`MinFloatParameter` for focalLength） | | Bokeh 档 |
| | `bladeCount` | `ClampedIntParameter` | | |
| | `bladeCurvature`, `bladeRotation`, `gaussianMaxRadius` | `ClampedFloatParameter` | | |
| | `highQualitySampling` | `BoolParameter` | | **移动端建议关**（官方优化页要求降采样类特性从简，见 §9） |
| `MotionBlur` | `mode` | `MotionBlurModeParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.MotionBlur.html)；对照表 URP 支持 Camera/Object Motion Blur |
| | `quality` | `MotionBlurQualityParameter` | | |
| | `intensity`, `clamp` | `ClampedFloatParameter` | | |
| `LensDistortion` | `intensity`, `scale`, `xMultiplier`, `yMultiplier` | `ClampedFloatParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.LensDistortion.html) |
| | `center` | `Vector2Parameter` | | |
| `PaniniProjection` | `distance`, `cropToFit` | `ClampedFloatParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.PaniniProjection.html)；对照表：Built-in 无、URP 有 |
| `WhiteBalance` | `temperature`, `tint` | `ClampedFloatParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.WhiteBalance.html) |
| `ShadowsMidtonesHighlights` | `shadows`, `midtones`, `highlights` | `Vector4Parameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ShadowsMidtonesHighlights.html) |
| | `shadowsStart`, `shadowsEnd`, `highlightsStart`, `highlightsEnd` | `MinFloatParameter` | | |
| `SplitToning` | `shadows`, `highlights` | `ColorParameter` | ✅（**有文档冲突**，见 §4.3）| [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.SplitToning.html) |
| | `balance` | `ClampedFloatParameter` | | |
| `LiftGammaGain` | `lift`, `gamma`, `gain` | `Vector4Parameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.LiftGammaGain.html) |
| `ChannelMixer` | `redOutRedIn`, `redOutGreenIn`, `redOutBlueIn`, `greenOutRedIn`, …（共 9 个） | `ClampedFloatParameter` | ✅ | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ChannelMixer.html) |
| `ColorCurves` | （曲线参数，`TextureCurveParameter` 系列） | — | ✅ | 见 [EffectList](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/EffectList.html) |
| `ColorLookup` | （LUT 纹理参数） | — | ✅ | 同上 |
| `ScreenSpaceLensFlare` | 见 §5.2 | — | ✅ | **URP 17 中是 Volume Override**（不是 Renderer Feature） |

### 4.3 三处文档冲突：Split Toning / White Balance / Channel Mixer

官方对照表把它们在 **URP 列**标为 `No`：
> "| Split Toning | No | No | Yes |"、"| White balance | No | Yes | Yes |"、"| Color Channel Mixer | No | Yes | Yes |" — [Render pipeline feature comparison](https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html)

但 **URP 自己的效果清单**明确列出 "Split Toning Volume Override reference"（[EffectList](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/EffectList.html)），且 **URP 17 API 里存在 `SplitToning` 类并带 `shadows`/`highlights`/`balance` 字段**（[SplitToning API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.SplitToning.html)）。

**结论**：以 **EffectList + API 类存在性** 为准（Split Toning 在 URP 可用）；对照表的该行视为陈旧/笔误。
> **UNKNOWN — needs editor validation**：`SplitToning` 在 6000.3.25f1 的 Volume 组件「Add Override」列表里是否真的出现。

### 4.4 Volume 侧 API：怎么从代码创建 / 修改

**`VolumeProfile`**（`UnityEngine.Rendering`，core 包）：
```
public List<VolumeComponent> components            // 公开字段（可直接读/改）
public T Add<T>(bool overrides = false) where T : VolumeComponent
public VolumeComponent Add(Type type, bool overrides = false)
public bool Has<T>() where T : VolumeComponent
public void Remove<T>() where T : VolumeComponent
```
— [VolumeProfile API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.Rendering.VolumeProfile.html)

**`VolumeComponent`**（基类）：`active`（`bool`）、`displayName`、`parameters`（`ReadOnlyCollection<VolumeParameter>`）、`Override(state, interpFactor)`、`AnyPropertiesIsOverridden()` — [VolumeComponent API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.Rendering.VolumeComponent.html)

**`Volume`（场景组件）**：`isGlobal`、`priority`、`blendDistance`、`weight`、`sharedProfile`、`profile`、`colliders` — [Volume API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.Rendering.Volume.html)

**每个 `VolumeParameter` 都有 `value` 与 `overrideState`**（后者是 URP 17 升级指南特别强调的）：
> "your implementation must set the `VolumeParameter.overrideState` property to `true` whenever the VolumeParameter value is changed." — [Upgrade to URP 17 (Unity 6.0)](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-unity-6.html)

### 4.5 用脚本填充那个空的 `DefaultVolumeProfile.asset`

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

static void FillDefaultVolumeProfile(UniversalRenderPipelineAsset urp)
{
    // 复用工程里已有的 DefaultVolumeProfile（components: []）
    var path    = "Assets/DefaultVolumeProfile.asset";
    var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
    if (profile == null) throw new System.InvalidOperationException($"找不到 {path}");

    // ① 幂等：只补缺的，不清空已有的
    var bloom = profile.Has<Bloom>() ? null : profile.Add<Bloom>(overrides: true);
    var vig   = profile.Has<Vignette>() ? null : profile.Add<Vignette>(overrides: true);
    var grade = profile.Has<ColorAdjustments>() ? null : profile.Add<ColorAdjustments>(overrides: true);
    var tone  = profile.Has<Tonemapping>() ? null : profile.Add<Tonemapping>(overrides: true);
    var grain = profile.Has<FilmGrain>() ? null : profile.Add<FilmGrain>(overrides: true);

    // ② 值 + overrideState 一起写（URP 17 要求，见 §4.4）
    if (bloom != null)
    {
        bloom.intensity.value = 0.35f;  bloom.intensity.overrideState = true;
        bloom.threshold.value = 0.95f;  bloom.threshold.overrideState = true;
        bloom.highQualityFiltering.value = false; bloom.highQualityFiltering.overrideState = true;
    }
    if (vig != null)
    {
        vig.intensity.value = 0.42f; vig.intensity.overrideState = true;
        vig.smoothness.value = 0.45f; vig.smoothness.overrideState = true;
    }
    if (grade != null)
    {
        grade.saturation.value = -18f; grade.saturation.overrideState = true;
        grade.contrast.value = 12f;    grade.contrast.overrideState = true;
        grade.postExposure.value = -0.15f; grade.postExposure.overrideState = true;
    }
    if (tone != null)
    {
        tone.mode.value = TonemappingMode.ACES; tone.mode.overrideState = true;
    }
    if (grain != null)
    {
        grain.intensity.value = 0.25f; grain.intensity.overrideState = true;
        grain.response.value = 0.7f;   grain.response.overrideState = true;
    }

    // ③ 新建出来的 VolumeComponent 是「子资产」，必须挂进 profile 文件
    foreach (var c in profile.components)
        if (AssetDatabase.GetAssetPath(c) != path)      // 尚未成为子资产
            AssetDatabase.AddObjectToAsset(c, profile);

    EditorUtility.SetDirty(profile);
    AssetDatabase.SaveAssets();

    // ④ 让 URP Asset 引用它（Volume Profile 属性可写）
    urp.volumeProfile = profile;
    EditorUtility.SetDirty(urp);
    AssetDatabase.SaveAssets();
}
```

**为什么必须 `AddObjectToAsset`**：`VolumeProfile.Add<T>()` 只是 new 了一个 `ScriptableObject`（`VolumeComponent` 是 `ScriptableObject` — [VolumeComponent API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.Rendering.VolumeComponent.html)）。不调用 [AssetDatabase.AddObjectToAsset](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.AddObjectToAsset.html) 把它作为 `profile` 的子资产持久化，`profile.components` 的引用在下次打开工程时会变成 `null`。
> **UNKNOWN — needs editor validation**：官方文档没有直接给出「脚本创建 VolumeProfile 子资产」的完整样例；上述 `AddObjectToAsset` 组合是编辑器脚本的标准做法，需在 CI 里实测 `DefaultVolumeProfile.asset` 落盘后 `components` 非空（用 `git diff` + 再次加载校验）。

**移动端要慎用的组件（工程判断，依据见 §9 官方优化清单）**：`DepthOfField`（多次降采样+模糊）、`MotionBlur`（需要运动向量 + 额外 pass）、`Bloom` 的 `highQualityFiltering`、`ScreenSpaceLensFlare`（需要 Bloom > 0 且额外 pass）、`Tonemapping` 的 `Neutral`+HDR 组合（HDR 本身有带宽成本）。**恐怖游戏里性价比最高的四件套**：`ColorAdjustments`（去饱和+冷色）+ `Vignette` + `FilmGrain` + `Bloom`（低强度、关高质量过滤）。

---

## 5. 体积光 / 神光（god rays）在 URP 17：官方有什么、没有什么

### 5.1 结论先行

**URP 17 没有任何内置的体积雾（volumetric fog）/ 体积光（volumetric lighting）**，也没有 HDRP 的 `Fog` Volume Override / Local Volumetrics / Volumetric Clouds。这是官方对照表逐条写死的：

| 特性 | Built-in | **URP** | HDRP | 出处 |
|---|---|---|---|---|
| Linear Fog / Exponential Fog / Exponential Squared | Yes（Graphics Settings） | **Yes（Graphics Settings）** | Yes（Fog Override） | [对照表](https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html) |
| **Local Volumetrics** | No | **No** | Yes | 同上 |
| **3D Render texture for local volumetric fog** | No | **No** | Yes | 同上 |
| **Volumetric Material（ShaderGraph 做局部体积雾）** | No | **No** | Yes | 同上 |
| **Fog Scattering & Atmospheric scattering** | No | **No** | Yes | 同上 |
| **Volumetric Clouds** | No | **No** | Yes | 同上 |
| **Fog Volume**（World building） | No | **No** | Yes | 同上 |

**注意区分**：URP 的 "Linear/Exponential Fog" 是**传统距离雾**（在 Graphics Settings / Lighting 窗口配置，作用在 shader 的 fog 项上），**不会产生光束（light shafts）**，也不是体积雾。HDRP 的 Fog Override 才有 `Volumetric Fog`：
- HDRP 侧文档：[Create a global fog effect（HDRP 17.0）](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/create-a-global-fog-effect.html)

> **UNKNOWN — needs editor validation**：Unity 6.3（6000.3）的 Graphics Settings 里 URP 的 fog 选项是否与 Built-in 完全一致（同页对照表给了 Yes，但没说字段名）。本项目自研 shader **刻意不用 `#pragma multi_compile_fog`**（见 `WhisperLitPbr.shader` 血泪纪律①）【工程】，所以即使切 URP 也不受影响。

### 5.2 URP 17 官方**有**的、可当"光效"用的东西

| 能力 | 类型（精确名） | 命名空间 / 程序集 | 说明 |
|---|---|---|---|
| **屏幕空间镜头光晕** | `ScreenSpaceLensFlare` | `UnityEngine.Rendering.Universal` / `Unity.RenderPipelines.Universal.Runtime.dll` | **URP 17 中是 Volume Override（不是 Renderer Feature）** |
| 数据驱动镜头光晕 | `LensFlareComponentSRP`、`LensFlareDataSRP`、`LensFlareDataElementSRP`、`LensFlareCommonSRP` | `UnityEngine.Rendering`（core 包） | 场景里按光源位置发光晕 |
| 灯光锚点工具 | `LightAnchor` | `UnityEngine.Rendering`（core 包） | "Represents camera-space light controls around a virtual pivot point." |
| 灯 Cookie | `UniversalRenderPipelineAsset.supportsLightCookies`（只读）+ `LightCookieFormat` / `LightCookieResolution` | `UnityEngine.Rendering.Universal` | **只对附加光（point/spot）生效** |
| 全屏自定义 pass | `FullScreenPassRendererFeature` | `UnityEngine.Rendering.Universal` | 官方「低代码」自定义后处理入口（可做径向模糊神光） |

**出处**：
- [URP 17.0 API 命名空间列表](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.html)（确认 `ScreenSpaceLensFlare` / `FullScreenPassRendererFeature` 存在；**同时确认没有**任何 `Volumetric*` 类型）
- [ScreenSpaceLensFlare API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScreenSpaceLensFlare.html)（原文："A volume component that holds settings for the Screen Space Lens Flare effect."）
- [Add screen space lens flares in URP](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/shared/lens-flare/post-processing-screen-space-lens-flare.html)
- [LightAnchor API（core 17.0）](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.LightAnchor.html)；对照表 "Light anchor tool | Yes | Yes | Yes" — [对照表](https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html)
- [LightCookieFormat API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.LightCookieFormat.html)（原文："…for the Light Cookie atlas texture **for additional lights (point, spot)**."）
- [Full Screen Pass Renderer Feature 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-features/renderer-feature-full-screen-pass.html) / [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.FullScreenPassRendererFeature.html)

#### Screen Space Lens Flare 的全部参数（可代码赋值）

来自 [API 页 Fields 段](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScreenSpaceLensFlare.html)：

`intensity`（`MinFloatParameter`）、`tintColor`（`ColorParameter`）、`bloomMip`（`ClampedIntParameter`）、`scale`（`ClampedFloatParameter`）、`firstFlareIntensity` / `secondaryFlareIntensity` / `warpedFlareIntensity`（`MinFloatParameter`）、`warpedFlareScale`（`Vector2Parameter`）、`streaksIntensity`（`MinFloatParameter`）、`streaksLength` / `streaksOrientation`（`ClampedFloatParameter` / `FloatParameter`）、`streaksThreshold`（`ClampedFloatParameter`）、`chromaticAbberationIntensity`（`ClampedFloatParameter`，注意官方拼写是 **Abberation**，双 b）、`samples`（`ClampedIntParameter`）、`sampleDimmer`（`ClampedFloatParameter`）、`vignetteEffect`（`ClampedFloatParameter`）、`resolution`（`ScreenSpaceLensFlareResolutionParameter`）、`startingPosition`（`ClampedFloatParameter`）。

**硬耦合（官方原文）**：镜头光晕复用 Bloom 的亮部 buffer，**Bloom 的 `intensity` 必须 > 0**，否则光晕不出现：
> "Note: If you have a Bloom override in the volume, set Intensity in the Bloom override to a value higher than 0 or lens flares won't appear." — [Add screen space lens flares in URP](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/shared/lens-flare/post-processing-screen-space-lens-flare.html)

**URP Asset 上的开关（只读）**：`supportScreenSpaceLensFlare`、`supportDataDrivenLensFlare`（字段 `m_SupportScreenSpaceLensFlare` / `m_SupportDataDrivenLensFlare`）——它们只负责「分配 shader 变体与显存」，官方原文：
> "| Data Driven Lens Flare | Allocate the shader variants and memory URP needs for lens flares effect. | Screen Space Lens Flare | Allocate the shader variants and memory URP needs for screen space lens flares. |" — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)

> **UNKNOWN — needs editor validation**：`ScreenSpaceLensFlare` 在 URP 17 里从 Renderer Feature 变成 Volume Override 的确切版本分界（URP 16 手册把它列在 Renderer Features 下）。如果将来要写跨版本代码，必须按版本分支。

#### Light Cookies 的 URP 限制（做"手电筒光锥纹理"时很重要）

- URP **支持彩色 cookie**："| Light Cookies | Built-in: Yes — Only shape, no color (Alpha channel) | **URP: Yes — Colored cookie (RGB)** | HDRP: Yes — Colored cookie (RGB) |" — [对照表](https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html)
- 但 cookie 图集是**为附加光准备的**（API 原文见上）；URP Asset 侧的 Cookie Atlas 设置只出现在 Additional Lights 段：
  > "| Cookie Atlas Resolution | The size of the cookie atlas **the additional lights** use. All additional lights are packed into a single cookie atlas. |" — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)
- **UNKNOWN — needs editor validation**：主方向光（main directional light）在 URP 17 能否使用 cookie。官方文档只描述了附加光的 cookie 图集，未提及主光 cookie；**不要假设可以**。

### 5.3 现实可行的移动端"神光"方案（按成本从低到高）

| 方案 | 做法 | 成本 | 官方支持度 |
|---|---|---|---|
| ① 加法混合"光锥"网格 | 手写一个 additive/soft-particle shader，贴在锥体或相机朝向的 billboard 上；用深度纹理做遮挡淡出 | 极低（几个 draw call） | 自研，无官方文档；需要 `_CameraDepthTexture`（URP Asset `supportsCameraDepthTexture`） |
| ② 灯光 cookie + 体积感雾片 | spot light + 彩色 cookie（见 §5.2）+ 若干半透明雾片 | 低 | cookie 官方支持（附加光） |
| ③ 径向模糊「假神光」 | `FullScreenPassRendererFeature`（Injection Point = Before Rendering Post Processing，Requirements = Depth / Color），材质的 shader 做「从屏幕亮部向光源屏幕坐标拉射线」的模糊 | 中（一次全屏 pass，移动端需降分辨率） | ✅ 官方特性 + [官方参考页](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-features/renderer-feature-full-screen-pass.html)；纯自研 shader 逻辑 |
| ④ `ScreenSpaceLensFlare` | 直接开 Volume Override | 中（依赖 Bloom） | ✅ 官方 |
| ⑤ 真体积光（raymarch） | 自研 compute/fragment raymarch | 高，Android GLES3 上风险大 | ❌ 官方无；需自研 |

**关于 `FullScreenPassRendererFeature` 的关键参数（官方原文）**：`Injection Point`（`Before Rendering Transparents` / `Before Rendering Post Processing` / `After Rendering Post Processing`，默认后者）、`Requirements`（`None` / `Everything` / `Depth` / `Normal` / `Color` / `Motion`）、`Fetch Color Buffer`、`Bind Depth-Stencil`、`Pass Material`、`Pass`。— [Full Screen Pass Renderer Feature 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-features/renderer-feature-full-screen-pass.html)

**明确不要做的事**：不要照抄 HDRP 的 Volumetric Fog 教程（`Fog` Volume Override / `VolumetricFog`）——URP 没有这些组件，官方对照表已列 `No`。

---

## 6. URP 水面渲染：平面反射、折射、以及 Unity 6 的渲染请求 API

### 6.1 官方立场：URP 没有水系统

官方对照表把**整套 Water system** 都标成 HDRP-only（Built-in = No，**URP = No**，HDRP = Yes）：
Physically Based Shader、Compute based wave simulation、CPU wave simulation、Local currents、Local foam、Surface deformer、Water excluder、Support for decals、Underwater Caustics、Under water rendering、**Under water volumetrics**。
— [Render pipeline feature comparison](https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html)

同时官方对照表还明确：

| 特性 | Built-in | **URP** | HDRP |
|---|---|---|---|
| Screen Space Reflections | Yes | **No** | Yes |
| **Planar Reflections** | No | **No** | Yes |
| Screen Space Refractions | No | **No** | Yes |

→ **URP 里"水面"必须自己搭**：平面反射要自己用第二个相机渲染到 `RenderTexture`；折射要自己采样 `_CameraOpaqueTexture`。下面给的是 Unity 6 的**正确**做法。

### 6.2 从代码渲染到 RenderTexture：Unity 6 的正确 API（替代 `Camera.Render`）

**官方推荐路径是 render request，不是 `Camera.Render()`**：

> "You can use a render request in a C# script to trigger a Camera to render to a render texture, **outside the Unity rendering loop**. The request is processed sequentially in your script, so there's no callback involved." — [Render Requests（core RP 17.0 手册）](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/manual/User-Render-Requests.html)

URP 专用步骤（官方原文，逐条对应）：
> "To trigger a camera to render to a render texture outside of the Universal Render Pipeline (URP) rendering loop, use the **SingleCameraRequest** and **SubmitRenderRequest** APIs in a C# script." — [Render to a render texture outside the URP rendering loop（URP 17 手册）](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/User-Render-Requests.html)

**API 签名（精确）**：

| 成员 | 声明 | 出处 |
|---|---|---|
| `UniversalRenderPipeline.SingleCameraRequest` | `public class UniversalRenderPipeline.SingleCameraRequest`（`UnityEngine.Rendering.Universal`，`Unity.RenderPipelines.Universal.Runtime.dll`） | [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest.html) |
| 字段 | `public RenderTexture destination`、`public CubemapFace face`、`mipLevel`、`slice` | 同上 |
| `RenderPipeline.SupportsRenderRequest` | "Checks if the render pipeline supports the RequestData type with the camera."（官方样例调用形式：`RenderPipeline.SupportsRenderRequest(cam, request)`） | [RenderPipeline（6000.3 API）](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.RenderPipeline.html) / [core 手册样例](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/manual/User-Render-Requests.html) |
| `RenderPipeline.SubmitRenderRequest` | "Submits a render request to a camera using the render pipeline, outside of the Unity render loop."（官方样例调用形式：`RenderPipeline.SubmitRenderRequest(cam, request)`） | 同上 |
| `RenderPipeline.StandardRequest` | `class in UnityEngine.Rendering`，字段 `destination` / `face` / `mipLevel` / `slice` | [StandardRequest（6000.3 API）](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.RenderPipeline.StandardRequest.html) |

**`StandardRequest` vs `SingleCameraRequest` 的区别（官方原文，选错就白干）**：
> "SRP Compatibility: **URP**: Renders a **full URP camera stack**. **Only works on base cameras.** To render a single camera in a stack, use `UniversalRenderPipeline.SingleCameraRequest`. HDRP: Renders an HDRP camera without AOVs." — [RenderPipeline.StandardRequest](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.RenderPipeline.StandardRequest.html)

**平面反射（水面）参考实现**：

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Camera))]
public sealed class PlanarReflectionCamera : MonoBehaviour
{
    public RenderTexture reflectionRT;      // 建议 half 分辨率 + 与主相机同格式
    public Transform     mirrorPlane;       // 水面
    Camera _cam;

    void Awake() { _cam = GetComponent<Camera>(); _cam.enabled = false; } // 只手动渲染

    void LateUpdate()
    {
        // ① 镜像相机变换（略：按 mirrorPlane 反射 position/rotation + 斜切投影矩阵）
        //    这一步是纯数学，与管线无关。

        // ② URP 专用：SingleCameraRequest（不是 Camera.Render！）
        var request = new UniversalRenderPipeline.SingleCameraRequest();
        if (!RenderPipeline.SupportsRenderRequest(_cam, request))
        {
            Debug.LogError("当前管线不支持 SingleCameraRequest");
            return;
        }
        request.destination = reflectionRT;
        RenderPipeline.SubmitRenderRequest(_cam, request);
    }
}
```

**必读的两个坑（官方原文）**：
1. **要在所有相机渲染完之后再取图**：
   > "To make sure all cameras finish rendering before you render to the render texture, use either of the following approaches: A coroutine that waits for the end of the frame… A callback… `RenderPipelineManager.endContextRendering`." — [URP 17 手册](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/User-Render-Requests.html)
2. **Stack 里只有 base camera 能用 `StandardRequest`**（原文见上表）；反射相机应该是**独立的 base camera**，不要塞进主相机的 camera stack。

**旧 API 的现状（本工程最可能踩到的）**：

| 旧写法 | Unity 6 / URP 17 现状 | 依据 |
|---|---|---|
| `UniversalRenderPipeline.RenderSingleCamera(context, camera)` | **已 obsolete**（Unity 官方仓库 issue 明确指出："'UniversalRenderPipeline.RenderSingleCamera(ScriptableRenderContext, Camera)' is obsolete"） | [BoatAttack issue #206（Unity 官方仓库）](https://github.com/Unity-Technologies/BoatAttack/issues/206) |
| `Camera.Render()` | 仍存在于 API 文档，但引擎内该 API 属于 Built-in 的渲染循环接管；**SRP 下官方推荐的等价物是 render request**。官方 `Camera.Render` 页**没有**任何 SRP 说明 | [Camera.Render（6000.3）](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Camera.Render.html) + [Render Requests](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/manual/User-Render-Requests.html) |
| `ScriptableRenderContext.ExecuteCommandBuffer(cmd)` | URP 14 时代的自定义 pass 范例里仍出现（`context.ExecuteCommandBuffer(cmd)`）——见 [Upgrade to URP 16](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-2022-2.html)；但 **URP 17 起自定义 render pass 必须用 Render Graph API 重写** | [Upgrade to URP 17 (Unity 6.0)](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-unity-6.html) |

> "URP 17 introduces the render graph system, which includes significant changes to the way you write custom render passes. **If your project contains custom render passes, rewrite your passes using the render graph API.**" —— 并给了逃生口："For compatibility purpose, Unity 6 includes the option to disable the render graph system and use the rendering API from previous URP versions… **Project Settings > Graphics > Render Graph > Compatibility Mode (Render Graph Disabled)**" — [Upgrade to URP 17](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-unity-6.html)

> **UNKNOWN — needs editor validation**：Unity 6 里在 SRP 下直接调 `Camera.Render()` 到底是「日志报错并跳过」还是「静默无效」。官方 Scripting API 页没有写 SRP 行为，本文不猜。若必须用，请在 CI/真机里跑一个最小场景验证。

### 6.3 水面折射：`_CameraOpaqueTexture`

**这是 URP 官方指定的 GrabPass 替代品**：
> "| Opaque Texture | Enable this to create a `_CameraOpaqueTexture` as default for all cameras in your scene. **This works like the GrabPass in the built-in render pipeline.** The Opaque Texture provides a snapshot of the scene right before URP renders any transparent meshes. **You can use this in transparent Shaders to create effects like frosted glass, water refraction, or heat waves.** You can override this for individual cameras in the Camera Inspector. |" — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)

配套设置：
- URP Asset：`supportsCameraOpaqueTexture`（**可写**，字段 `m_RequireOpaqueTexture`）—— [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html)；
- 单相机覆盖：`UniversalAdditionalCameraData.requiresColorTexture` / `requiresColorOption`（`{ get; set; }`）—— [UniversalAdditionalCameraData API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalAdditionalCameraData.html)；
- 性能：官方要求「不需要就别开」——"Disable Opaque Texture, so that URP doesn't store a snapshot of the opaques in a scene unless it needs to." — [Configure for better performance](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html)；
- 分辨率旋钮 `Opaque Downsampling`（`None` / `2x Bilinear` / `4x Box` / `4x Bilinear`，只读属性 `opaqueDownsampling`）—— [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)。

**在自定义 Renderer Feature 里取相机颜色/深度（Unity 6 的正确属性名）**：
> "The public interfaces `ScriptableRenderer.cameraColorTarget` and `ScriptableRenderer.cameraDepthTarget` are marked as **obsolete**. Replace them with `ScriptableRenderer.cameraColorTargetHandle` and `ScriptableRenderer.cameraDepthTargetHandle` respectively." — [Upgrade to URP 16](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-2022-2.html)

同时注意声明输入（否则可能被优化掉中间纹理）：
> "URP expects Renderer Features to declare their inputs using the `ScriptableRenderPass.ConfigureInput` method." — [同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-2022-2.html)

（`ScriptableRenderPassInput` 的取值包含 `Color` / `Depth` / `Normal` / `Motion`，语义与 Full Screen Pass 的 `Requirements` 一致，见 [Full Screen Pass 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-features/renderer-feature-full-screen-pass.html)。）

### 6.4 水面着色器要用的 shader 侧设施

- 折射：采样 `_CameraOpaqueTexture`（需开启 Opaque Texture）；
- 深度淡出/岸边泡沫：采样 `_CameraDepthTexture`（需开启 Depth Texture，`supportsCameraDepthTexture`）；
- 反射：采样你自己的平面反射 `RenderTexture`（`Shader.SetGlobalTexture` 全局传，或材质属性）；
- **本工程自研 shader 的现状**：`WhisperLitPbr.shader` 的所有 pass 都各自声明了自己的 uniform【工程】——迁移到 URP 时，新增 `_CameraOpaqueTexture` / `_CameraDepthTexture` 采样也必须遵守这条纪律（每个 pass 各自声明），否则就是"整个着色器编译失败 → 真机整屏品红"（该文件纪律④的真实事故记录）。

---

## 7. Android 上的抗锯齿（URP）

### 7.1 四种方式的官方定位

> "The anti-aliasing methods available are: Fast Approximate Anti-aliasing (FXAA) / Subpixel Morphological Anti-aliasing (SMAA) / Temporal Anti-aliasing (TAA) / Multisample Anti-aliasing (MSAA)" — [Add anti-aliasing in the Universal Render Pipeline](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/anti-aliasing.html)

**官方对移动端的直接建议（这是本节的结论）**：
> "Note: **For anti-aliasing on mobile platforms, Unity recommends that you use FXAA.**" — [同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/anti-aliasing.html)

### 7.2 MSAA（管线资产级）

- 属性：`UniversalRenderPipelineAsset.msaaSampleCount`（`int`，**可写**）—— [API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html)；
- 序列化字段 `m_MSAA`，类型 `MsaaQuality`（源码默认值 `MsaaQuality.Disabled`）【源码】[UniversalRenderPipelineAsset.cs](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAsset.cs)（该枚举各成员的确切拼写本轮未逐条核实，写代码时用 `System.Enum.GetNames(typeof(MsaaQuality))` 打印，或直接用 `msaaSampleCount` 的 `int` 语义 1/2/4/8）；
- 官方描述：
  > "| Anti Aliasing (MSAA) | Use Multisample Anti-aliasing by default for every Camera… select how many samples to use per pixel: 2x, 4x, or 8x… |" — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)
- **Android 平台级限制（官方原文，务必看）**：
  > "Note: **On mobile platforms that do not support the StoreAndResolve store action, if Opaque Texture is selected in the URP asset, Unity ignores the Anti Aliasing (MSAA) property at runtime (as if Anti Aliasing (MSAA) is set to Disabled).**" — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)（同一句也出现在 [Add anti-aliasing](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/anti-aliasing.html)）
- MSAA 的收益条件与代价：
  > "MSAA is more resource intensive than other forms of anti-aliasing on most hardware. However, **when run on a tiled GPU with no post-processing anti-aliasing or custom render features in use, MSAA is a cheaper option** than other anti-aliasing types." / "However, **you can't use MSAA with TAA**." — [Add anti-aliasing](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/anti-aliasing.html)
- 还会被 Depth Priming 互斥（"Depth priming isn't supported if you use a deferred rendering path or **Multisample Anti-aliasing**" — [Universal Renderer 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html)）；
- 官方优化页也建议降它："**Reduce or disable Anti-aliasing (MSAA)**, so that URP doesn't use memory bandwidth to copy frame buffer attachments into and out of memory." — [Configure for better performance](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html)

**恐怖游戏权衡（工程判断）**：Whisper 是全黑场景 + 大量自发光小面积高对比边缘（灯、屏幕、鬼眼），MSAA **不解决 shader/贴图走样**（官方原文："it does not fix shader aliasing issues such as specular or texture aliasing"）。所以：
- 若**不开 Opaque Texture**（即不做水面折射）：MSAA 2x/4x 在 TBDR 手机上是划算的；
- 若**必须开 Opaque Texture**：在不支持 StoreAndResolve 的机器上 MSAA 会被静默忽略 → 此时应依赖 FXAA。

### 7.3 相机级 AA（FXAA / SMAA / TAA）

属性在 `UniversalAdditionalCameraData` 上：

| 属性 | 类型 | 可写 |
|---|---|---|
| `antialiasing` | `AntialiasingMode` | ✅ `{ get; set; }` |
| `antialiasingQuality` | `AntialiasingQuality` | ✅ `{ get; set; }` |
| `renderPostProcessing` | `bool` | ✅（FXAA/SMAA/TAA 都是后处理 pass，**必须开**） |
| `stopNaN` | `bool` | ✅ |
| `dithering` | `bool` | ✅ |
| `renderShadows` | `bool` | ✅ |
| `requiresColorTexture` / `requiresDepthTexture` | `bool` | ✅ |
| `resetHistory` | `bool` | ✅（切镜头/传送后清 TAA 历史） |
| `taaSettings` | `ref TemporalAA.Settings` | 只读 ref |

— 全部来自 [UniversalAdditionalCameraData API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalAdditionalCameraData.html)

```
public enum AntialiasingMode {
    None,                            // "Use this to have no post-processing anti-aliasing pass performed."
    FastApproximateAntialiasing,     // FXAA
    SubpixelMorphologicalAntiAliasing, // SMAA
    TemporalAntiAliasing            // TAA
}
public enum AntialiasingQuality { Low, Medium, High }   // 只影响 SMAA
```
— [AntialiasingMode API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.AntialiasingMode.html) / [AntialiasingQuality API](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.AntialiasingQuality.html)

**TAA 的官方互斥清单**：
> "The following features cannot be used with TAA: Multisample anti-aliasing (MSAA) / **Camera Stacking** / **Dynamic Resolution**" — [Add anti-aliasing](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/anti-aliasing.html)

TAA 还需要运动向量，且"often creates ghosting artifacts"（同页）。**STP 上采样会强制相机 AA 变成 TAA**：
> "Selecting this option forces the Anti-Aliasing setting in the Camera Inspector window to Temporal Anti-aliasing (TAA)." — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)

### 7.4 GLES3 / Vulkan 上"到底支持什么"

**有官方明确依据的**：

| 项 | 结论 | 依据 |
|---|---|---|
| URP 支持哪些图形 API | "URP supports the following graphics APIs: DirectX 11 / DirectX 12 / **Vulkan** / Metal / **OpenGL ES 3.0 and later** / OpenGL Core / WebGL2 / WebGPU (experimental)" | [Requirements and compatibility for URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/requirements.html) |
| MSAA + Opaque Texture | 在不支持 StoreAndResolve 的移动平台上，MSAA 被运行时忽略 | [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html) |
| Depth Priming `Auto` | Android/iOS/tvOS **不支持**；TBDR 移动设备运行时也不支持 | [Universal Renderer 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html) |
| Native RenderPass | "Enabling this property **has no effect on OpenGL ES**." | [同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html) |
| STP 上采样 | "supported only on **non-GLES** devices that support compute shaders. On unsupported devices, Unity uses Automatic instead." | [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html) |
| FSR 1.0 上采样 | "only supported on devices that support **Unity shader model 4.5** or higher. On devices that do not support Unity shader model 4.5, Unity uses the Automatic option instead." | [同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html) |
| 移动端 AA 推荐 | FXAA | [Add anti-aliasing](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/anti-aliasing.html) |

**UNKNOWN — needs editor validation（本节最需要实测的部分）**：
- SMAA 是否需要 compute shader、GLES3 上是否可用：**官方 anti-aliasing 页与 URP asset 参考页都没有这句限制**。不要凭印象写"SMAA 需要 compute"。
- TAA 在 GLES3/Vulkan 的具体可用性：官方只给了互斥清单与移动端推荐 FXAA，**没有**「TAA 在 GLES3 不可用」这类明文。
- 实测方法：在真机上按机型分别设 `antialiasing = SMAA / TAA`，读 `Application.platform` + `SystemInfo.supportsComputeShaders` + `SystemInfo.graphicsDeviceType` 并抓画面 logcat，形成本项目的真源表。

---

## 8. 颜色空间（Color Space）：Gamma vs Linear

### 8.1 本工程现状

`unity/ProjectSettings/ProjectSettings.asset` 第 50 行：`m_ActiveColorSpace: 0`【工程】——`0` = **Gamma**。
这与 `WhisperLitPbr.shader` 文件头的自述一致【工程】：
> "引擎侧是 **Built-in + Gamma 空间**（实测 `m_ActiveColorSpace: 0`）→ 不做线性化工作流，高光项按屏显量级标定（照抄线性空间的 1.0 阈值会得到"永远没有高光"）。"

**这是一条真实的耦合**：该 shader 的高光/环境项数值是**按 Gamma 屏显量级人工标定过的**（文件里还记录了"第一版写的是 (0.55 + 0.90*up) * occ，真机整片偏暗"的调参历史）。切到 Linear 会**改变全部材质的明暗与高光表现**，不是"换个开关没影响"。

### 8.2 官方 API 与官方对两种空间的定性

- 设置 API：`public static ColorSpace PlayerSettings.colorSpace;` —— "Set the rendering color space for the current project." 并提示 "Note that changing the project color space may cause a reimport of some assets." — [PlayerSettings.colorSpace](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings-colorSpace.html)
- Linear 的定性："**Working in linear color space gives more accurate rendering than working in gamma color space.**" — [Linear color space](https://docs.unity3d.com/6000.3/Documentation/Manual/linear-color-space.html)
- Gamma 的定性（注意这句是为 Gamma 辩护的）："While a linear workflow ensures more precise rendering, **sometimes you may want a gamma workflow (for example, on some platforms the hardware only supports the gamma format).**" — [Gamma color space](https://docs.unity3d.com/6000.3/Documentation/Manual/gamma-color-space.html)
- 原理与纹理采样差异：[Color spaces in Unity](https://docs.unity3d.com/6000.3/Documentation/Manual/color-spaces.html)、[Gamma Textures in linear color space](https://docs.unity3d.com/6000.3/Documentation/Manual/gamma-textures-linear-color-space.html)

### 8.3 URP 到底要不要求 Linear？（**这是本题最容易被以讹传讹的一条**）

**官方 URP 兼容性页面只列了图形 API，没有列颜色空间**：
> "Graphics API compatibility — URP supports the following graphics APIs: DirectX 11 … Vulkan, Metal, OpenGL ES 3.0 and later, OpenGL Core, WebGL2, WebGPU (experimental)" — [Requirements and compatibility for URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/requirements.html)

同样，官方的 [Install URP into an existing project](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/InstallURPIntoAProject.html) 步骤里**没有**"把 Color Space 改成 Linear"这一步（它只要求：删掉 Post Processing V2 包、装 URP 包、创建 URP Asset、在 Graphics 设置里挂上、以及改 shader）。

→ **UNKNOWN — needs editor validation**：**官方文档没有「URP 需要 Linear」或「URP 不支持 Gamma」的明文**。因此：
- ❌ 不要在交付文档/CI 注释里写"URP 必须 Linear"（无官方依据）；
- ✅ 可以写"URP 在 Gamma 下能跑，但线性工作流更准确（官方对 Linear 的定性）"；
- ✅ 必须在编辑器里实测：Gamma 下 URP 的后处理（尤其 HDR Bloom / Tonemapping / Color Grading LUT）是否出现色偏，以及 URP Asset 是否给出 Console 警告。
- ❌ **不要发明"Unity 6 禁止 URP + Gamma"这种说法**——本轮检索未找到任何官方依据。

### 8.4 Android 侧的已知信息

- Unity 官方博客曾专门发布 Android/iOS 的线性渲染支持说明：[Linear Rendering Support on Android and iOS — Unity Blog](https://blog.unity.com/technology/linear-rendering-support-on-android-and-ios)（**官方博客，非手册**；本条链接来自检索结果，本轮环境未能直接抓取正文；且年代较早，需以目标机的 `SystemInfo` 实测为准）。
- URP 里有一个与颜色空间转换有关的性能开关（可写属性，但**只读**，见 §2.4 清单）：`useFastSRGBLinearConversion`（字段 `m_UseFastSRGBLinearConversion`）：
  > "| Fast sRGB/Linear Conversions | Select this option to use faster, but less accurate approximation functions when converting between the sRGB and Linear color spaces. |" — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)（注意：该条在 API 页上是 `{ get; }`，**只能 SerializedObject 改**）
- `m_BuildTargetGraphicsAPIs: []`【工程】：本工程**没有**显式指定图形 API，即由 Unity 决定顺序。要显式固定（例如强制 Vulkan 以获得更好的 compute 支持），用 `PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]{ GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 })` — [PlayerSettings.SetGraphicsAPIs](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings.SetGraphicsAPIs.html)。
  > **UNKNOWN — needs editor validation**：Unity 6.3 在 Android 上「API 列表为空」时的**默认顺序**（Vulkan 优先还是 GLES3 优先）。不要假设；在 CI 里打印 `PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)`。

### 8.5 本工程的建议（工程判断，非官方结论）

| 选项 | 代价 | 建议 |
|---|---|---|
| A. 保持 Gamma + 切 URP | shader 数值不用重调；但后处理/HDR 的线性假设可能不准 | **推荐作为第一步**（最小变更，先把管线切过来） |
| B. 切 Linear + 切 URP | 所有材质观感会变，`WhisperLitPbr` 的高光/环境标定必须重调（文件头已记录这类事故） | 作为**独立里程碑**做，且必须有真机截图对比 |

---

## 9. 自研 shader 迁移到 URP（`WhisperLitPbr` / `WhisperUnlitColor` / `WhisperPostFx`）

### 9.1 官方立场（先看这句，避免心存侥幸）

> "**Custom Shaders written for the Built-In Render Pipeline are not compatible with the Universal Render Pipeline (URP), and you can't upgrade them automatically with the Render Pipeline Converter. Instead, you must rewrite the incompatible sections of shader code to work with URP.**" — [Upgrade custom shaders for URP compatibility](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/birp-urp-custom-shader-upgrade-guide.html)

### 9.2 「硬失败」还是「品红」？——官方原文是**品红**

> "Note: **You can identify any materials in a scene that use custom shaders when you upgrade to URP as they turn magenta (bright pink) to indicate an error.**" — [Upgrade custom shaders for URP compatibility](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/birp-urp-custom-shader-upgrade-guide.html)

补充（由 pass tag 规则推出的必然结果）：如果一个 shader 连一个 URP 认得的 `LightMode` 都没有，URP 会把它当作 `SRPDefaultUnlit` 来画：
> "If you do not set the LightMode tag in a Pass, **URP uses the SRPDefaultUnlit tag value for that Pass**." — [ShaderLab Pass tags in URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html)

而 `ForwardAdd` 这类**不被支持的 tag** 官方明确列出：
> "**Note: URP does not support the following LightMode tags: `Always`, `ForwardAdd`, `PrepassBase`, `PrepassFinal`, `Vertex`, `VertexLMRGBM`, `VertexLM`.**" — [同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html)

→ **本工程 `WhisperLitPbr.shader` 的 Pass 2（`ForwardAdd`）在 URP 下不会被执行**：手电筒/室内点光将**全部失效**（只剩主方向光）。这不是"效果差一点"，而是恐怖游戏最核心的照明玩法直接没了。

### 9.3 URP 允许的 `LightMode` 取值（完整表）

| LightMode | PassType | 官方描述（节选） |
|---|---|---|
| `UniversalForward` | ScriptableRenderPipeline | 画几何 + 算全部光照；**Forward 路径下用这个** |
| `UniversalForwardOnly` | ScriptableRenderPipeline | 同 UniversalForward，但 Forward/Deferred **两条路径都能用**；"If a shader must render using the Forward Rendering Path regardless… declare only a Pass with the LightMode tag set to UniversalForwardOnly." |
| `UniversalGBuffer` | ScriptableRenderPipeline | Deferred 路径下画几何、不算光照 |
| `DepthNormalsOnly` | ScriptableRenderPipeline | 与 `UniversalForwardOnly` 配合；"If you use the SSAO Renderer Feature, add a Pass with the LightMode tag set to DepthNormalsOnly." |
| `ShadowCaster` | ShadowCaster | 渲染进 shadow map / depth texture |
| `DepthOnly` | ScriptableRenderPipeline | 只渲深度（Depth Priming / 深度纹理需要） |
| `DepthNormals` | （见下） | 深度+法线预pass（Depth Priming 需要） |
| `Meta` | Meta | 只用于烘焙 lightmap，构建时被剥离 |
| `SRPDefaultUnlit` | ScriptableRenderPipelineDefaultUnlit | 额外 pass（如描边）；**没写 LightMode 时的默认值** |
| `MotionVectors` | MotionVectors | 运动向量（TAA/Motion Blur 需要） |
| `Universal2D` | ScriptableRenderPipeline | 2D Renderer |

— 全部来自 [ShaderLab Pass tags in URP reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html)

> 该表把 `DepthOnly` 列为 `ScriptableRenderPipeline`，而 Depth Priming 的说明要求自定义 shader 提供 `DepthOnly` **和** `DepthNormals`（"Unity renders opaque objects as invisible unless you add passes with DepthOnly and DepthNormals tags" — [Universal Renderer 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html)）。
> **UNKNOWN — needs editor validation**：`DepthNormals` 在同一张官方表里**没有单独列出**（表里有 `DepthNormalsOnly`，语义不同）。`DepthNormals` 的确切 URP 17 拼写与用途需在编辑器里确认（写错 tag = pass 静默不执行）。

### 9.4 官方迁移清单（照这个做就够了）

来自 [Upgrade custom shaders for URP compatibility](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/birp-urp-custom-shader-upgrade-guide.html)（**官方给出的步骤，逐条可核对**）：

| # | 动作 | 原文/要点 |
|---|---|---|
| 1 | `CGPROGRAM` / `ENDCG` → **`HLSLPROGRAM` / `ENDHLSL`** | 官方第 1 步 |
| 2 | 换 include：`#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"` | "Note: Core.hlsl includes the core SRP library, URP shader variables, and matrix defines and transformations, but it **does not include lighting functions or default structs**." |
| 3 | SubShader 加 tag：`Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }` | 官方第 3 步 |
| 4 | `struct v2f` → **`struct Varyings`**；新增 **`struct Attributes`**（等价于 `appdata`） | 位置语义必须是 `SV_POSITION` |
| 5 | `UnityObjectToClipPos(v.vertex)` → **`TransformObjectToHClip(IN.positionOS.xyz)`** | "URP shaders use suffixes to indicate the space. **OS** means object space, and **HCS** means homogeneous clip space." |
| 6 | 所有材质属性包进 **`CBUFFER_START(UnityPerMaterial)` … `CBUFFER_END`** | "For a shader to be SRP Batcher compatible, you must declare all material properties within a CBUFFER code block. **Even if a shader has multiple passes, all passes must use the same CBUFFER block.**" |
| 7 | `fixed4` → **`half4`** | "**URP shaders do not support fixed types**" |
| 8 | 贴图采样：`tex2D(_Tex, uv)` → `SAMPLE_TEXTURE2D(_Tex, sampler_Tex, uv)`，并声明 `TEXTURE2D(_Tex); SAMPLER(sampler_Tex);` | 官方第 8–11 步 |
| 9 | Tiling/Offset：把 `sampler2D _BaseMap` 换成 `float4 _BaseMap_ST`，UV 用 `TRANSFORM_TEX(IN.uv, _BaseMap)` | "**It's important that you define the vert function after the CBUFFER block**, as the TRANSFORM_TEX macro uses the parameter with the `_ST` suffix." |
| 10 | 主贴图/主色改名 + 加属性：`[MainTexture] _BaseMap`、`[MainColor] _Color` | 让 `Material.mainTexture` / `Material.color` 正确映射 |

**光照/阴影（`WhisperLitPbr` 的 `ForwardBase`/`ForwardAdd` 该换成什么）**：

> "To use shadows in a custom Universal Render Pipeline (URP) shader, follow these steps: Add `#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"` inside the HLSLPROGRAM in your shader file." — [Use shadows in a custom URP shader](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/use-built-in-shader-methods-shadows.html)

同页给出的关键方法与 pragma（**照抄这几条**）：
- `#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN`
- `#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS`
- `Light GetMainLight(float4 shadowCoordinates)` → 带 `shadowAttenuation`
- `half MainLightRealtimeShadow(float4 shadowCoordinates)`
- `float4 GetShadowCoord(VertexPositionInputs)` / `float4 TransformWorldToShadowCoord(float3 positionInWorldSpace)`
- 前提："Make sure there are objects in your scene that have a **ShadowCaster** shader pass"

**替换对照表（Built-in → URP）**：

| Built-in 写法 | URP 写法 | 依据 |
|---|---|---|
| `#include "UnityCG.cginc"` | `#include ".../ShaderLibrary/Core.hlsl"` | [迁移指南](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/birp-urp-custom-shader-upgrade-guide.html) |
| `#include "Lighting.cginc"` / `"AutoLight.cginc"` | `#include ".../ShaderLibrary/Lighting.hlsl"` | [阴影页](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/use-built-in-shader-methods-shadows.html) |
| `_WorldSpaceLightPos0` | `GetMainLight()` / `_MainLightPosition`（URP 侧常量） | 同上（官方推荐用 `GetMainLight`） |
| `_LightColor0` | `GetMainLight().color` / `Light` 结构 | 同上 |
| `UNITY_LIGHT_ATTENUATION` / `UNITY_TRANSFER_SHADOW` | `GetMainLight(shadowCoord)` / `MainLightRealtimeShadow()` | 同上 |
| `UnityObjectToClipPos` | `TransformObjectToHClip` | [迁移指南](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/birp-urp-custom-shader-upgrade-guide.html) |
| `UNITY_MATRIX_MVP` / `unity_ObjectToWorld` | URP 侧矩阵宏（`Core.hlsl` 提供 matrix defines and transformations） | 同上（官方只说 Core.hlsl 提供，未逐个列名） |
| `fixed4` / `fixed` | `half4` / `half` | 同上 |
| `ForwardBase` | `UniversalForward`（或 `UniversalForwardOnly`） | [pass tag 表](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html) |
| `ForwardAdd` | **无对应 tag**；附加光在 `UniversalForward` 里由 URP 的光照循环处理（或走 Forward+ 的 clustered 结构） | 同上（`ForwardAdd` 在不支持清单里） |
| `OnRenderImage` | **见 §9.6** | — |

**SRP Batcher 的意义（性能，不是可选项）**：
> "Enable the SRP Batcher. This is useful if you have many different Materials that use the same Shader… **Note: If assets or shaders in a project are not optimized for use with the SRP Batcher, low performance devices might be more performant when you disable the SRP Batcher.**" — [URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)

（另见 [Make a URP shader compatible with the SRP Batcher](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shaders-in-universalrp-srp-batcher.html)。）

### 9.5 `WhisperLitPbr.shader` 的逐条改造清单（结合本工程）

现状【工程】：Pass 1 = `ForwardBase`（主光+环境+自发光），Pass 2 = `ForwardAdd`（`Blend One One` + `#pragma multi_compile_fwdadd` + `UNITY_LIGHT_ATTENUATION`），两 pass 各自声明了全部 uniform（这是**好习惯**，迁移时必须保留）。

必须做的：
1. 两个 pass 的 tag：`ForwardBase` → `UniversalForward`；`ForwardAdd` **删除**（附加光交给 URP 的光照循环；若确实要手写加光，需要**自研 RendererFeature + Rendering Layers**，成本高，先不做）。
2. `CGPROGRAM` → `HLSLPROGRAM`；`UnityCG.cginc` / `Lighting.cginc` / `AutoLight.cginc` → `Core.hlsl` + `Lighting.hlsl`。
3. `fixed4 _Color` → `half4 _Color`；所有属性进 `CBUFFER_START(UnityPerMaterial)`。
4. `_WorldSpaceLightPos0` → `Light mainLight = GetMainLight(shadowCoord);`，`_LightColor0` → `mainLight.color`，`NdotL` 用 `mainLight.direction`。
5. **补 `ShadowCaster` pass**（否则物体不投影；文件末尾自述"没有 ShadowCaster pass"【工程】）。
6. **补 `DepthOnly` pass**（否则将来开 Depth Priming / 深度纹理相关效果时物体会消失，见 §3.3）。
7. 若走 SSAO + Deferred：补 `DepthNormalsOnly`（见 §3.9）。
8. `_WhisperFog*` 自定义雾**可以保留**（它是自研距离雾，不依赖 `multi_compile_fog`）——这点是好事，迁移时不受 URP 雾系统影响。
9. 顶点色 `COLOR` 语义在 URP 下仍可用，但要在 `Attributes` 里以 `float4 color : COLOR;` 声明（同 Built-in）。
10. `_WhisperAmbient` 这类自定环境项保留即可；URP 的 SH/环境光另有来源（`SampleSH`），但本工程刻意不读引擎环境项【工程】，迁移时**不要顺手改成 `UNITY_LIGHTMODEL_AMBIENT`**（那个宏属于 Built-in）。

### 9.6 `WhisperPostFx.shader`：`OnRenderImage` 在 URP 下没有位置

- 官方给 Built-in → URP 的迁移文档里，自定义后处理的**唯一**官方路径是 Renderer Feature / Full Screen Pass，而不是 `OnRenderImage`：
  > "| Custom post-processing | Built-in: Yes | **URP: Yes — Available with Full Screen Pass Renderer Feature.** | HDRP: Yes — Either with script or ShaderGraph. |" — [Render pipeline feature comparison](https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html)
- URP 的自定义后处理文档：[Custom post-processing in URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/post-processing/custom-post-processing.html)
- 低代码入口：[Full Screen Pass Renderer Feature 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-features/renderer-feature-full-screen-pass.html)（`Injection Point` / `Requirements` / `Pass Material` 见 §5.3）
- 走 Volume 的完整做法：[Full Screen Pass Renderer Feature 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-features/renderer-feature-full-screen-pass.html) 页面同时链到 "Create a custom post-processing effect with Volume support in URP"

> **UNKNOWN — needs editor validation**：Unity 6.3 文档里**没有**一句「`OnRenderImage` 在 URP 下被忽略/报错」的明文（`Camera.onRenderImage` 的 Scripting API 页本轮未检索到对应 6000.3 URL）。已知的确定事实是：URP 官方**只提供** Renderer Feature / Full Screen Pass 这条自定义后处理路径（对照表原文）。**结论仍然成立**（`WhisperPostFx.shader` 必须重写为 Full Screen Pass 材质 + 特性），但"具体报错形态"需实测。

**建议的迁移顺序（把风险切成可回滚的小步）**：
1. 先只做 `WhisperUnlitColor`（unlit 最简单：`HLSLPROGRAM` + `Core.hlsl` + `CBUFFER` + `UniversalForward`）；
2. 再 `WhisperLitPbr`（补 `ShadowCaster` + `DepthOnly`，主光走 `GetMainLight`）；
3. 最后 `WhisperPostFx`（改造成 Full Screen Pass 材质 + `FullScreenPassRendererFeature`）；
4. 每一步都在真机抓一张同机位截图，和 Gamma+Built-in 的基线对比——**这是唯一能防住"切了管线但画面全变"的手段**。

---

## 10. Android 移动端 URP Asset 推荐配置（恐怖游戏）

### 10.1 官方给出的"往哪调"（方向是官方的，数值是工程的）

官方优化页原文（[Configure for better performance in URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html) / [Adjust settings to improve performance in URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/optimize-for-better-performance.html)）：

- 不需要就关 **Depth Texture**、**Opaque Texture**；
- 低端移动端把 **Store Actions** 设为 Auto/Discard（省带宽）；
- 关 **HDR**，或若需要 HDR 就把 **HDR Precision 设为 32 Bit**；
- 降 **Main Light > Shadow Resolution**、降 **Additional Lights > Shadow Atlas Resolution**；
- 不需要就关 **Light Cookies**（或降 Cookie Atlas Resolution / Format 到 `Color Low`）；
- **Intermediate Texture** 设为 `Auto`；
- 少用 **Decal Renderer Feature**；
- **Volume Update Mode** 设为 `Via Scripting`（不每帧更新 Volume）；
- 低端移动端关 **Probe Blending** / **Box Projection**；
- 降 **Shadows > Max Distance**、降 **Cascade Count**；
- 附加光 **Cast Shadows** 关掉；
- **减少相机数量**（每台相机都要 culling/rendering 资源）；
- 降或关 **MSAA**；
- 用渲染路径选择来省（Forward / Forward+ / Deferred 的取舍）。

### 10.2 建议配置表（Whisper = 室内黑场恐怖 + 手机）

| 设置 | 属性（可写性） | 建议值 | 依据/说明 |
|---|---|---|---|
| Rendering Path | `UniversalRendererData.renderingMode` ✅ | `Forward`（或 `ForwardPlus`） | Deferred 需 G-buffer，移动端带宽贵（[configure 页](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html)） |
| HDR | `supportsHDR` ✅ | **开**（恐怖游戏的 Bloom/自发光需要 >1 亮度） | "the brightest part of the image can be greater than 1 … useful if you want a wide range of lighting or to use bloom"（[asset 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)） |
| HDR Precision | `hdrColorBufferPrecision` ✅ | `_32Bits`（默认） | "The 64 bit precision lets you avoid banding artifacts, but requires higher bandwidth"（[同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)） |
| MSAA | `msaaSampleCount` ✅ | **2x**（保守）；若开 Opaque Texture 则视为可能被忽略 | StoreAndResolve 限制见 §7.2 |
| Camera AA | `UniversalAdditionalCameraData.antialiasing` ✅ | **FXAA**（官方移动端推荐） | §7.1 |
| Render Scale | `renderScale` ✅ | 0.8–1.0（先 1.0，真机掉帧再降） | "This slider scales the render target resolution… **This only scales the game rendering. UI rendering is left at the native resolution**"（[asset 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)） |
| Upscaling Filter | `upscalingFilter` ✅ | `Linear`（GLES3 安全）或 `FSR`（需 shader model 4.5） | FSR：sm4.5 才生效，否则回退 `Auto`；STP：非 GLES + compute（[同上](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)、[What's new in URP 17](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/whats-new/urp-whats-new.html)） |
| FSR 锐化 | `fsrOverrideSharpness` / `fsrSharpness` ✅ | 关（用默认） | — |
| Shadow Distance | `shadowDistance` ✅ | 15–25 m（室内） | 官方方向："reduce Max Distance" |
| Cascade Count | `shadowCascadeCount` ✅ | **1–2** | 官方方向："reduce Cascade Count to reduce the number of render passes" |
| 主光阴影 | `supportsMainLightShadows` ❌ 只读 | 开 | SerializedObject：`m_MainLightShadowsSupported` |
| 主光阴影分辨率 | `mainLightShadowmapResolution` ✅ | 1024 | 官方方向："reduce Main Light > Shadow Resolution" |
| 软阴影 | `supportsSoftShadows` ❌ 只读 | 开 + 质量 `Low`（4 taps） | 移动端高开销警告见 §3.6 |
| 附加光阴影 | `supportsAdditionalLightShadows` ❌ 只读 | **关**（手电筒不投影，改用法线/贴花做假阴影） | 官方方向："Additional Lights > Cast Shadows — Disable" |
| 附加光模式 | `additionalLightsRenderingMode` ❌ 只读 | `PerPixel`，但**每物体上限压到 2–4**（`maxAdditionalLightsCount` ✅） | Forward+ 下 per-object limit 被忽略："Unity ignores this setting if you select the Forward+ rendering path"（[asset 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)） |
| Depth Texture | `supportsCameraDepthTexture` ✅ | **开**（自研雾/遮挡淡出/水面泡沫需要） | 官方要求"不需要就别开"；本工程需要 |
| Opaque Texture | `supportsCameraOpaqueTexture` ✅ | **按需**（只有做水面折射才开） | 开了会与 MSAA 冲突（§7.2） |
| Opaque Downsampling | `opaqueDownsampling` ❌ 只读 | `_2xBilinear` 或 `_4xBox` | [asset 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html) |
| SRP Batcher | `useSRPBatcher` ✅ | **开**（前提：shader 全部按 §9.4 改造出 `CBUFFER`） | §9.4 引文 |
| Dynamic Batching | `supportsDynamicBatching` ✅ | 关（有 GPU instancing 时官方建议关） | [asset 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html) |
| Depth Priming | `UniversalRendererData.depthPrimingMode` ✅ | `Disabled`（Android `Auto` 不支持；且需 `DepthOnly`/`DepthNormals`） | §3.3 |
| Intermediate Texture | `intermediateTextureMode` ✅ | `Auto` | 官方建议 |
| Rendering Layers | `useRenderingLayers` ❌ 只读 | 开（玩法用，成本≈0） | §3.5 |
| Light Cookies | `supportsLightCookies` ❌ 只读 | 开（手电筒光锥） | §5.2 |
| Volume Update Mode | `volumeFrameworkUpdateMode` ❌ 只读 | `EveryFrame`（恐怖游戏要动态变化）或 `ViaScripting`（省 CPU） | 官方方向：ViaScripting 更省 |
| Color Grading Mode | `colorGradingMode` ✅ | `LowDynamicRange`（默认、更省）或 `HighDynamicRange`（HDR 分级更准） | "HDR: … Unity applies color grading before tonemapping. LDR: … after tonemapping"（[asset 参考](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html)） |
| LUT Size | `colorGradingLutSize` ✅ | 32（默认） | 同上 |
| Fast sRGB/Linear | `useFastSRGBLinearConversion` ❌ 只读 | 视真机实测 | §8.4 |

> **数值均为工程建议**：官方只给"往哪调"的方向 + 少数硬限制（FSR/STP/MSAA/Depth Priming 的平台条件）。**任何数值都必须以真机帧率为准**，不许把上表当成官方推荐值引用。

### 10.3 与本工程既有构建链路的关系

- `Assets/Editor/BuildScript.cs` 已经在 `BuildPipeline` 之前调用 `BuildConfigurator.Configure()`【工程】；新增的 `UrpSetup.ConfigureUrp()` 应当**在同一位置、`BuildConfigurator` 之后**调用（先保证包的 URP 资产存在，再谈渲染设置），或由 CI 单独先跑一次 `-executeMethod Whisper.Editor.UrpSetup.ConfigureUrp`。
- 若走 CI 单独一步，**必须先跑 URP 配置再跑构建**，否则构建出来的包仍然是 Built-in。
- 门禁建议：CI 里断言 `GraphicsSettings.currentRenderPipeline != null` 且其类型为 `UniversalRenderPipelineAsset`，并把类型名写进构建日志（本项目最忌讳"构建成功但产品不对"）。

---

## 11. UNKNOWN 汇总（必须在编辑器/真机验证，不许猜）

| # | 问题 | 为什么文档答不了 | 建议验证方式 |
|---|---|---|---|
| 1 | `RenderingMode.DeferredPlus` 在 6000.3.25f1 + URP 17.0.3 是否存在 | 手册列 4 项、API 枚举页列 3 项、源码有分支，三处不一致 | `Enum.GetNames(typeof(RenderingMode))` 打进 CI 日志 |
| 2 | `QualitySettings.renderPipeline` setter 是否按"当前质量档"持久化到 `QualitySettings.asset` | API 只说"for the current quality level"，未说落盘语义 | 循环赋值后 `git diff ProjectSettings/QualitySettings.asset` |
| 3 | `-quit -batchmode` 下 ProjectSettings 何时写盘、被 kill 是否丢 | 官方未文档化 | 构建后 diff + 失败重跑观察 |
| 4 | `ScreenSpaceAmbientOcclusion` / `DecalRendererFeature` 的序列化字段名 | 参数是私有 `[SerializeField]`，API 页无属性 | `DumpSerializedFields()` |
| 5 | Unity 6.3 的 "Screen Space Shadows" 是独立特性还是 SSAO 内选项 | 手册有导航条目但正文 URL 未检索到；API 无对应类 | 编辑器 Inspector 查 |
| 6 | 主方向光（main light）在 URP 17 是否支持 cookie | 官方只描述附加光 cookie 图集 | 编辑器实测 |
| 7 | URP + **Gamma** 是否被支持、后处理是否出错 | 官方 requirements 页无颜色空间要求；无任何"URP 需要 Linear"明文 | 编辑器实测 + Console 警告 |
| 8 | Unity 6.3 Android 在 `m_BuildTargetGraphicsAPIs: []` 时的默认 API 顺序 | 官方未文档化 | `PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)` |
| 9 | SMAA / TAA 在 GLES3、Vulkan 的具体可用性 | 官方只给互斥清单 + "移动端推荐 FXAA" | 真机 `SystemInfo.graphicsDeviceType` / `supportsComputeShaders` + 截图 |
| 10 | URP 下 `OnRenderImage` 的确切行为（忽略/报错） | 无明文；只有"官方只提供 Renderer Feature 路径" | 真机 + Console |
| 11 | `DepthNormals`（非 `DepthNormalsOnly`）在 URP 17 的确切 tag 拼写与用途 | pass tag 表未单独列出 | 编辑器 + Frame Debugger |
| 12 | Split Toning 是否真的出现在 URP 的 Add Override 列表 | 对照表说 No，EffectList/API 说 Yes | 编辑器 Add Override 列表 |

---

## 12. 参考链接索引（官方）

### Unity 6.3 手册（`docs.unity3d.com/6000.3/Documentation/Manual/`）
- 变更/检测当前渲染管线（default vs quality override）：<https://docs.unity3d.com/6000.3/Documentation/Manual/srp-setting-render-pipeline-asset.html>
- URP Asset 是什么 + 如何挂载：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-asset-and-renderer.html>
- 装 URP 到既有工程：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/InstallURPIntoAProject.html>
- URP Asset 参考（HDR/MSAA/RenderScale/FSR/STP/Shadows/Post-processing/Volumes）：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html>
- Universal Renderer 参考（Rendering Path / Depth Priming / Copy Depth / Rendering Layers 开关）：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html>
- 渲染路径对比：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/rendering-paths-comparison.html>
- **渲染管线特性对照总表（"URP 没有什么"的唯一权威来源）**：<https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-feature-comparison.html>
- URP 后处理效果清单：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/EffectList.html>
- 抗锯齿：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/anti-aliasing.html>
- Camera stacking：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/camera-stacking.html>
- 自定义后处理：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/post-processing/custom-post-processing.html>
- Full Screen Pass Renderer Feature：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-features/renderer-feature-full-screen-pass.html>
- Decal Renderer Feature：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/renderer-feature-decal.html>
- SSAO 落地页 / 添加步骤：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/post-processing-ssao-landing.html>、<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/add-ssao-renderer-feature-to-renderer.html>
- Rendering Layers：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/features/rendering-layers.html>
- ShaderLab Pass tags（URP 支持/不支持的 LightMode 全集）：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html>
- **Built-in → URP 自定义 shader 迁移指南**：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/birp-urp-custom-shader-upgrade-guide.html>
- 自定义 URP shader 里用阴影：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/use-built-in-shader-methods-shadows.html>
- 写一个基础 URP unlit shader：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/writing-shaders-urp-basic-unlit-structure.html>
- SRP Batcher 兼容：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shaders-in-universalrp-srp-batcher.html>
- URP 17 升级指南（Render Graph 改写要求）：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-unity-6.html>
- URP 16 升级指南（`cameraColorTargetHandle` 重命名、ConfigureInput、Intermediate Texture）：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guide-2022-2.html>
- What's new in URP 17：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/whats-new/urp-whats-new.html>
- URP 要求与兼容性（图形 API 清单）：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/requirements.html>
- 性能：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/understand-performance.html>、<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html>、<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/optimize-for-better-performance.html>
- Render Graph：<https://docs.unity3d.com/6000.3/Documentation/Manual/urp/render-graph.html>
- 颜色空间：<https://docs.unity3d.com/6000.3/Documentation/Manual/color-spaces.html>、<https://docs.unity3d.com/6000.3/Documentation/Manual/linear-color-space.html>、<https://docs.unity3d.com/6000.3/Documentation/Manual/gamma-color-space.html>、<https://docs.unity3d.com/6000.3/Documentation/Manual/gamma-textures-linear-color-space.html>
- Unity 6 升级指南（总）：<https://docs.unity3d.com/6000.3/Documentation/Manual/UpgradeGuideUnity6.html>

### URP 17.0 包手册（`docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/`）
- Render to a render texture outside the URP rendering loop：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/User-Render-Requests.html>
- Screen Space Lens Flare：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/shared/lens-flare/post-processing-screen-space-lens-flare.html>
- URP 抗锯齿（与 6000.3 手册同文，版本更贴近 17.0.3）：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/anti-aliasing.html>

### Core RP 17.0（core 包）
- **Render Requests 手册（`SubmitRenderRequest` 完整样例）**：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/manual/User-Render-Requests.html>
- `VolumeProfile` API：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.Rendering.VolumeProfile.html>
- `VolumeComponent` API：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.Rendering.VolumeComponent.html>
- `Volume` API：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.Rendering.Volume.html>
- `LightAnchor` API：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/api/UnityEngine.LightAnchor.html>

### Unity Scripting API（6000.3）
- `GraphicsSettings.defaultRenderPipeline`：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.GraphicsSettings-defaultRenderPipeline.html>
- `GraphicsSettings.currentRenderPipeline`：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.GraphicsSettings-currentRenderPipeline.html>
- `QualitySettings.renderPipeline`：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/QualitySettings-renderPipeline.html>
- `RenderPipeline`（`SubmitRenderRequest` / `SupportsRenderRequest`）：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.RenderPipeline.html>
- `RenderPipeline.StandardRequest`：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.RenderPipeline.StandardRequest.html>
- `PlayerSettings.colorSpace`：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings-colorSpace.html>
- `PlayerSettings.SetGraphicsAPIs`：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings.SetGraphicsAPIs.html>
- `Camera.Render`：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Camera.Render.html>
- `AssetDatabase.CreateAsset` / `AddObjectToAsset` / `SaveAssets`：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.CreateAsset.html>、<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.AddObjectToAsset.html>、<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.SaveAssets.html>
- `SerializedObject` / `FindProperty` / `EditorUtility.SetDirty`：<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SerializedObject.html>、<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SerializedObject.FindProperty.html>、<https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorUtility.SetDirty.html>

### URP 17.0 API（关键类型）
- `UniversalRenderPipelineAsset`（含 `Create(ScriptableRendererData)`、全部属性可写性）：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html>
- `UniversalRendererData`：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRendererData.html>
- `ScriptableRendererData`（`rendererFeatures` / `SetDirty` / `TryGetRendererFeature`）：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScriptableRendererData.html>
- `ScriptableRendererFeature`：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScriptableRendererFeature.html>
- 枚举：`RenderingMode`、`DepthPrimingMode`、`CopyDepthMode`、`AntialiasingMode`、`AntialiasingQuality`、`UpscalingFilterSelection`、`LightRenderingMode`、`LightCookieFormat`（把类型名替换进 `…/api/UnityEngine.Rendering.Universal.<TypeName>.html`）
- URP 17.0 全部类型列表（用来确认"某个类到底存不存在"）：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.html>
- `ScreenSpaceAmbientOcclusion` / `DecalRendererFeature` / `FullScreenPassRendererFeature` / `RenderObjects`：同目录下同名页面
- `UniversalAdditionalCameraData`：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalAdditionalCameraData.html>
- `UniversalRenderPipeline.SingleCameraRequest`：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest.html>

### 官方源码（序列化字段名等文档空白处）
- `UniversalRenderPipelineAsset.cs`：<https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAsset.cs>
- `UniversalRendererData.cs`：<https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.universal/Runtime/UniversalRendererData.cs>
- `RenderSingleCamera` 已过时的官方仓库证据：<https://github.com/Unity-Technologies/BoatAttack/issues/206>

### 其他官方来源
- Unity 官方博客（Android/iOS 线性渲染支持）：<https://blog.unity.com/technology/linear-rendering-support-on-android-and-ios>
- HDRP 全局雾（用来对照"URP 没有体积雾"）：<https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/create-a-global-fog-effect.html>
- Unity Discussions（URP Asset 有参数无法从脚本改的社区讨论，**非官方文档**）：<https://discussions.unity.com/t/how-to-create-change-universalrenderpipelineasset-from-editor-script-some-parameters-cannot-be-changed/1531138>

---

*本文只做一件事：把"从 Built-in 切到 URP 17"的所有官方依据、精确 API 与必须实测的空白点钉死。凡标 **UNKNOWN** 的，先跑探测再动手；凡标【工程】的，可现场复核文件原文。*
