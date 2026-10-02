using System;

namespace Whisper.Core
{
    /// <summary>
    /// 服务定位器（Core 内，V9 §13.2）。玩法代码通过它取三接口，**不得**反查具体实现类型。
    /// 刻意做得极简：不引入 DI 框架（V9 §19 零依赖纪律），也不做运行时反射。
    /// </summary>
    public static class Services
    {
        static Contracts.INetService _net;
        static Contracts.IVoiceService _voice;
        static Contracts.IBackendService _backend;

        public static Contracts.INetService Net =>
            _net ?? throw new InvalidOperationException("INetService 未注入：请由 Net 模块的实现在 Bootstrap 阶段调用 Services.Install(...)");

        public static Contracts.IVoiceService Voice =>
            _voice ?? throw new InvalidOperationException("IVoiceService 未注入：请由 Audio 模块的实现在 Bootstrap 阶段调用 Services.Install(...)");

        public static Contracts.IBackendService Backend =>
            _backend ?? throw new InvalidOperationException("IBackendService 未注入：请由 Backend 模块的实现在 Bootstrap 阶段调用 Services.Install(...)");

        // 独立验证轨 D7：旧版二次 Install **静默覆盖**（identity A→B 无异常无日志），
        // 跨用例还会因静态态泄漏（无 volatile、无锁、无清理）。现改为 fail-fast：
        // 默认拒绝重复注入；确需替换（如 Host 迁移后重连）必须显式传 replace: true。
        static void Guard(string name, object current)
        {
            if (current != null)
                throw new InvalidOperationException(
                    $"{name} 已注入，拒绝静默覆盖（如需替换请显式传 replace: true；测试间请调用 Services.Reset()）");
        }

        public static void Install(Contracts.INetService net, bool replace = false)
        {
            if (!replace) Guard("INetService", _net);
            _net = net;
        }

        public static void Install(Contracts.IVoiceService voice, bool replace = false)
        {
            if (!replace) Guard("IVoiceService", _voice);
            _voice = voice;
        }

        public static void Install(Contracts.IBackendService backend, bool replace = false)
        {
            if (!replace) Guard("IBackendService", _backend);
            _backend = backend;
        }

        /// <summary>测试与「无后端模式」用：清空注入（V9 §15.2 Firebase 不可达时后端缺失不影响联机与语音）。</summary>
        public static void Reset()
        {
            _net = null;
            _voice = null;
            _backend = null;
        }

        public static bool HasNet => _net != null;
        public static bool HasVoice => _voice != null;
        public static bool HasBackend => _backend != null;
    }
}
