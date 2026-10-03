using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;

namespace Whisper.Editor
{
    /// <summary>
    /// PlayerSettings 的**代码真源**（V9 §19.1 C1「场景零手工」在构建设置上的延伸）。
    ///
    /// 为什么需要它（真机事故 2026-10-03）：
    ///   本工程此前**没有 ProjectSettings.asset**（本机无 Unity 编辑器，那份 YAML 生成不了），
    ///   于是所有 PlayerSettings 都是 Unity 的出厂默认值。装机后才发现的后果：
    ///     · 包名 = `com.DefaultCompany.unity`（默认公司名 + 默认产品名）
    ///     · 产品名 = "unity"，版本号 = 1.0（APK 文件名与包内信息对不上）
    ///     · 屏幕方向 = portrait|landscape（**竖屏**启动一款横屏恐怖游戏）
    ///     · 启动画面 = 默认 Unity 标志 + "Made with Unity"
    ///   这些都属于"构建成功但产品不对"——本项目最忌讳的假绿。
    ///
    /// 为什么用脚本而不是手写 ProjectSettings.asset：
    ///   ① 手写那份 YAML 没有对照物，写错一个字段整包构建失败，比不写更糟；
    ///   ② 官方 CI 文档本身就推荐用 `-executeMethod` + PlayerSettings API 表达构建设置；
    ///   ③ 脚本可被本机静态检查（`gate-code`）覆盖，"设置也是代码"才可回归。
    ///
    /// 调用点：<see cref="BuildScript.BuildAndroid"/> 在 BuildPipeline 之前调用。
    /// </summary>
    public static class BuildConfigurator
    {
        // ── 唯一真源常量（改这里就是改包身份）──
        public const string CompanyName = "Whisper";
        public const string ProductName = "Project Whisper";
        public const string ApplicationId = "com.whisper.projectwhisper";
        public const string FallbackBundleVersion = "0.1.1";
        const int FallbackVersionCode = 1;

        [MenuItem("Whisper/应用构建设置")]
        public static void Configure() => Configure(false);

        /// <summary>
        /// 把包身份与 Android 关键设置写到 PlayerSettings。
        /// 幂等：可重复调用（CI 每次构建调用一次）。
        /// </summary>
        /// <param name="quiet">true 时只警告不抛错（用于诊断命令）。</param>
        public static void Configure(bool quiet)
        {
            var problems = new System.Collections.Generic.List<string>();

            // ① 包身份
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.bundleVersion = ResolveBundleVersion();
            SetVersionCode(ResolveVersionCode(problems));

            // ② 脚本后端与架构（V9 §13.8：IL2CPP + ARM64）
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // 托管代码剥离：Medium 是 V9 §13.8 的取值；接口实现由 link.xml 保护
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);

            // ③ 屏幕方向：横屏（一款第一人称恐怖游戏不该竖屏跑）。
            //    同时关掉自动旋转——真机实测中方向变化会触发窗口重配，
            //    是"启动阶段画面闪烁"的常见来源。
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.useAnimatedAutorotation = false;

            // ④ SDK 版本：targetSdk 36 是 V9 §12 的硬要求（Android 16 合规）
            TrySetSdkVersions(problems);

            // ⑤ 启动画面：关掉 Unity 标志，直接进游戏。
            //    真机现象"Unity 界面淡出后黑屏"里，那一屏淡出就是它——
            //    对玩家而言"先看到 Unity 标再黑屏"比"直接黑屏"更像故障。
            TryDisableSplash(problems);

            // ⑥ 自检：把最终值打出来，CI 日志里直接可查（不接受"以为设上了"）
            var report = $"PlayerSettings 已应用："
                + $"\n  包名      {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}"
                + $"\n  公司/产品 {PlayerSettings.companyName} / {PlayerSettings.productName}"
                + $"\n  版本      {PlayerSettings.bundleVersion} (code {PlayerSettings.Android.bundleVersionCode})"
                + $"\n  后端/架构 {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)} / {PlayerSettings.Android.targetArchitectures}"
                + $"\n  剥离等级  {PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android)}"
                + $"\n  屏幕方向  {PlayerSettings.defaultInterfaceOrientation}"
                + $"\n  SDK       min {PlayerSettings.Android.minSdkVersion} / target {PlayerSettings.Android.targetSdkVersion}";
            Debug.Log("[Whisper] " + report);

            foreach (var p in problems)
            {
                if (quiet) Debug.LogWarning("[Whisper] PlayerSettings 警告：" + p);
                else Debug.LogWarning("[Whisper] PlayerSettings 警告（不阻断构建）：" + p);
            }
        }

        /// <summary>CI 里从 GITHUB_RUN_NUMBER 派生版本号，保证每次构建的包可区分（排障必需）。</summary>
        static string ResolveBundleVersion()
        {
            var run = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
            return int.TryParse(run, out int n) && n > 0 ? $"0.1.{n}" : FallbackBundleVersion;
        }

        static int ResolveVersionCode(System.Collections.Generic.List<string> problems)
        {
            var run = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
            if (int.TryParse(run, out int n) && n > 0) return n;
            problems.Add("未读到 GITHUB_RUN_NUMBER，版本号回落到 " + FallbackVersionCode);
            return FallbackVersionCode;
        }

        static void SetVersionCode(int code)
        {
            try { PlayerSettings.Android.bundleVersionCode = code; }
            catch (Exception ex) { Debug.LogWarning("[Whisper] bundleVersionCode 设置失败：" + ex.Message); }
        }

        /// <summary>
        /// targetSdk/minSdk 通过枚举设置；不同 Unity 版本可选值不同，
        /// 取不到就报告并保留原值——**不猜**（宁可显式警告，也不静默用错值）。
        /// </summary>
        static void TrySetSdkVersions(System.Collections.Generic.List<string> problems)
        {
            try
            {
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
            }
            catch (Exception ex)
            {
                problems.Add($"targetSdk 36 设置失败（{ex.GetType().Name}）：{ex.Message}；当前={PlayerSettings.Android.targetSdkVersion}");
            }
            try
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            }
            catch (Exception ex)
            {
                problems.Add($"minSdk 26 设置失败（{ex.GetType().Name}）：{ex.Message}；当前={PlayerSettings.Android.minSdkVersion}");
            }
        }

        /// <summary>
        /// 关闭 Unity 启动画面。Unity 6 的 SplashScreen API 有过改动，
        /// 因此全程 try/catch —— 关不掉只是观感问题，**绝不能因此让构建失败**。
        /// </summary>
        static void TryDisableSplash(System.Collections.Generic.List<string> problems)
        {
            try
            {
                SplashScreen.show = false;
                SplashScreen.logos = Array.Empty<SplashScreenLogo>();
            }
            catch (Exception ex)
            {
                problems.Add($"关闭启动画面失败（{ex.GetType().Name}）：{ex.Message}（不影响功能）");
            }
        }
    }
}
