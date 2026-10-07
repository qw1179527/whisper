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

            // ② 脚本后端与架构
            //    **出货形态 = IL2CPP + ARM64**（V9 §13.8）——这是默认，行为一字不变。
            //    **开发形态 = Mono + ARMv7**（环境变量 `WHISPER_DEV_MONO=1` 切换）：
            //      · 为什么值得有：IL2CPP 那一段（C++ 转换 + 编译）实测占 CI 构建的 **14.7/21.4 分**
            //        （从 #24 日志时间戳解析），Mono 没有这一段；
            //      · 更要紧的是**手机端"换 DLL 热插拔"快速迭代**要的正是 Mono 形态
            //        —— Mono 下游戏代码是 `assets/bin/Data/Managed/*.dll` 明文文件，可直接替换重签；
            //        IL2CPP 下它被编进 `libil2cpp.so`，改不了。
            //      · 代价：Mono 在 Android 上**只有 Armv7（32 位）**（Unity 6 文档原文），
            //        所以开发包是 32 位、性能与出货包不等价 → **只能用于逻辑/玩法迭代**。
            //      · 本机设备已实测支持 32 位（`/system/bin/linker`、`app_process32`、`/system/lib` 554 个库）。
            //    切换用环境变量而**不是**改代码默认值：出货包的风险必须为零。
            bool devMono = ReadDevMonoFlag();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,
                devMono ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures =
                devMono ? AndroidArchitecture.ARMv7 : AndroidArchitecture.ARM64;
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

        /// <summary>
        /// 是否走 **Mono 开发形态**。
        ///
        /// ══════════════════════════════════════════════════════════════════════════════
        /// ⚠ 为什么改成"文件优先、环境变量兜底"（2026-10-07，一次真失败的教训）
        /// ══════════════════════════════════════════════════════════════════════════════
        /// `build-dev-mono.yml` 里把 `WHISPER_DEV_MONO: '1'` 放在**步骤级 `env:`**，
        /// 而那只是**容器**的环境变量。同一轮日志里 `BuildConfigurator` 打印的仍是
        /// `后端/架构 IL2CPP / ARM64` ⇒ **Unity 进程没拿到这个变量** ⇒ 32 分钟后
        /// 产物名对不上（`whisper-dev-mono.apk` 不存在）而失败。
        ///
        /// 环境变量要穿过 `action → docker run → xvfb-run → Unity` 四层，
        /// 任何一层没转发就静默丢失，而**失败发生在 30 分钟后**（代价极高）。
        /// ⇒ 改用**工作区里的标记文件**：它随 `checkout` 就在，`File.Exists` 一次即可判定，
        ///   而且**看得见**（`ls` 就能查），不存在"转发丢失"这种看不见的失效。
        ///
        /// 保留环境变量读法作为兜底（本机调试时 `WHISPER_DEV_MONO=1` 仍然好用）。
        /// </summary>
        static bool ReadDevMonoFlag()
        {
            const string marker = "whisper-dev-mono.flag";
            if (System.IO.File.Exists(marker))
            {
                Debug.Log($"[Whisper] 命中开发形态标记文件 `{marker}` ⇒ 切 Mono + ARMv7");
                return true;
            }
            bool byEnv = System.Environment.GetEnvironmentVariable("WHISPER_DEV_MONO") == "1";
            Debug.Log($"[Whisper] 无标记文件 `{marker}`；环境变量 WHISPER_DEV_MONO={(byEnv ? "1" : "未设/非1")}"
                + $" ⇒ 形态 = {(byEnv ? "Mono 开发包" : "IL2CPP 出货包")}");
            return byEnv;
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
        /// 关闭 Unity 启动画面。
        ///
        /// 这里有一段**必须记住的教训**（CI 构建 #18 失败）：
        /// 我最初写成 `SplashScreen.show = false;` —— 因为我在本机手写的 UnityEditor 桩里
        /// 就是这么定义的，于是本机语法门禁**通过**了。但真 Unity 里它叫
        /// `PlayerSettings.SplashScreen`（`UnityEditor.SplashScreen` 并不存在），CI 报：
        ///     CS0103: The name 'SplashScreen' does not exist in the current context
        /// 根因不是"名字记错了"，而是**桩是我自己写的：我编造一个 API，桩就替它背书，
        /// 于是这道门禁永远发现不了"我编造 API"这个错误类别**。桩能验证"内部一致性"，
        /// 不能验证"Unity 真的有这个成员"。
        ///
        /// 所以现在改成**反射动态查找**：不去记属性名，只要类型上存在名为 `show` 的
        /// bool 可写属性就关掉它；找不到就只记一条警告。这样既拿到了"关启动画面"的收益，
        /// 又不会因为 API 名字/位置变化而**再让整次构建失败**（一次 CI ≈ 45 分钟）。
        /// </summary>
        static void TryDisableSplash(System.Collections.Generic.List<string> problems)
        {
            var t = typeof(PlayerSettings).GetNestedType("SplashScreen", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (t == null)
            {
                problems.Add("未找到 PlayerSettings.SplashScreen 类型，跳过关闭启动画面（不影响功能）");
                return;
            }
            var prop = t.GetProperty("show", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop == null || !prop.CanWrite)
            {
                problems.Add("PlayerSettings.SplashScreen.show 不可写，跳过（不影响功能）");
                return;
            }
            try
            {
                prop.SetValue(null, false);
                Debug.Log("[Whisper] 已关闭 Unity 启动画面（PlayerSettings.SplashScreen.show = false）");
            }
            catch (Exception ex)
            {
                problems.Add($"关闭启动画面失败（{ex.GetType().Name}）：{ex.Message}（不影响功能）");
            }
        }
    }
}
