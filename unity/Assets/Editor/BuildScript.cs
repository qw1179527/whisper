using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Whisper.Editor
{
    /// <summary>
    /// 命令行构建入口（V9 §19.1 C1「场景零手工」在构建侧的对应物）。
    ///
    /// 为什么必须由脚本构建而不是"在编辑器里点 Build"：
    /// 本项目的开发模式是"手机 + AI 编程"，人不在编辑器前 —— 构建必须能被 CI 一行命令驱动。
    /// 【包体上限已取消 · 用户 2026-10-06】
    /// 原实现有一条 `APK ≤200MB` 的**硬门禁**，来源是 V9 §13.8。
    /// 用户明确：「**并非有200MB上限**，把 pdf 方案里的所有要求全部抛弃」。
    /// 而且这条约束与当前方向**直接相冲** —— 要做高画质建模/贴图，包体必然上去，
    /// 拿一个过时的条文把构建卡死，等于把画质目标堵死。
    /// 现在只**如实打印包体**，不再判红。
    ///
    /// 用法（GameCI / 本地均可）：
    ///   Unity -quit -batchmode -projectPath unity -executeMethod Whisper.Editor.BuildScript.BuildAndroid
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("Whisper/Build Android APK")]
        public static void BuildAndroid()
        {
            // 先应用构建设置（包名/版本/IL2CPP/横屏/SDK 版本）——没有 ProjectSettings.asset，
            // 这些只能由代码表达；不先做这一步就会产出 "com.DefaultCompany.unity / 竖屏" 的包（真机事故）。
            BuildConfigurator.Configure();

            // ── 再挂 URP 渲染管线（2026-10-06）────────────────────────────────────
            // 【为什么必须在这里，而且必须在 BuildPlayer 之前】
            // 实测 `GraphicsSettings.asset:40 m_CustomRenderPipeline: {fileID: 0}` —— 本工程此前
            // **根本没有分配渲染管线资产**，实际跑的是 Built-in。而 `ProjectSettings/*.asset` 是
            // 编辑器生成的 YAML（`unity/ProjectSettings/README.md` 明令禁止手写），所以只能由
            // Editor 代码在构建前建好并挂上。**漏了这一步 = 包里的 URP 着色器全部变品红。**
            //
            // 它内部有**判决点**：最后断言 `GraphicsSettings.currentRenderPipeline` 非 null 且类型
            // 为 `UniversalRenderPipelineAsset`，不成立就抛异常中断构建 —— 宁可构建失败，
            // 也不要产出一个"构建成功但渲染管线是错的"包（本项目最忌讳的失效形态）。
            UrpSetup.ConfigureUrp();

            var outDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "Android");
            Directory.CreateDirectory(outDir);
            // 产物名区分出货与开发两种形态（后端由 BuildConfigurator 按 WHISPER_DEV_MONO 决定）：
            // 分开命名是为了**两个包能同时存在** —— 出货包用于真机验收/性能，开发包用于快速迭代；
            // 若同名，后构建的会把前一个覆盖掉，验收时就分不清手上是哪个形态了。
            bool devMono = System.Environment.GetEnvironmentVariable("WHISPER_DEV_MONO") == "1";
            var apk = Path.Combine(outDir, devMono ? "whisper-dev-mono.apk" : "whisper-android.apk");

            // 场景列表：Boot 是唯一场景，其余内容由代码装配（No-Editor 纪律）。
            // 场景文件不存在时**明确报错**而不是让构建悄悄产出一个空包。
            const string bootScene = "Assets/Scenes/Boot.unity";
            if (!File.Exists(bootScene))
            {
                // 允许 CI 先用脚本生成场景（EditorSceneBootstrap），再构建
                Debug.LogWarning($"[Whisper] 缺 {bootScene}，尝试由代码生成");
                EditorSceneBootstrap.CreateBootScene();
            }

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { bootScene },
                locationPathName = apk,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"构建失败：{summary.result} · 错误 {summary.totalErrors} 条");

            long size = new FileInfo(apk).Length;
            Debug.Log($"[Whisper] 构建成功：{apk} · {size} 字节（{size / 1048576} MB）");
            Debug.Log($"[Whisper] 包体 {size / 1048576.0:F1} MB（仅记录，无上限判据）");
        }
    }
}
