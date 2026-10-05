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
    /// 本脚本同时承担 §13.8 的**包体硬门禁**（APK ≤200MB），超限直接构建失败，
    /// 避免"包悄悄变大到装不下"。
    ///
    /// 用法（GameCI / 本地均可）：
    ///   Unity -quit -batchmode -projectPath unity -executeMethod Whisper.Editor.BuildScript.BuildAndroid
    /// </summary>
    public static class BuildScript
    {
        const long MaxApkBytes = 200L * 1024 * 1024;   // V9 §13.8

        [MenuItem("Whisper/Build Android APK")]
        public static void BuildAndroid()
        {
            // 先应用构建设置（包名/版本/IL2CPP/横屏/SDK 版本）——没有 ProjectSettings.asset，
            // 这些只能由代码表达；不先做这一步就会产出 "com.DefaultCompany.unity / 竖屏" 的包（真机事故）。
            BuildConfigurator.Configure();

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
            if (size > MaxApkBytes)
                throw new Exception($"✗ 超过 V9 §13.8 包体门禁：{size} > {MaxApkBytes}");
            Debug.Log("✓ 包体门禁通过");
        }
    }
}
