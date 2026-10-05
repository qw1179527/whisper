using System.IO;
using System.Text;
using UnityEngine;

namespace Whisper.Runtime
{
    /// <summary>
    /// 点击回执：把**每一次点击/触摸**落盘成一行日志，供真机复现定位（P0-3）。
    ///
    /// ## 为什么必须落盘，而不是 Debug.Log
    /// 用户报告"按钮点击失灵、长时间点无反应"，而这件事**只能在真机上复现**。
    /// 但 `Debug.Log` 要连 `adb logcat` 才看得到 —— 而这台设备的处境恰恰是
    /// **没有 PC、没有 adb**。日志写在屏幕上又会被"去掉所有小字"的要求挡掉。
    /// ⇒ 落盘到 `persistentDataPath`：**HUD 全关也能事后取**，用文件管理器就能看。
    ///
    /// ## 为什么不用 DateTime（重要）
    /// 本仓 `gate-physics` 明令禁用 `DateTime.Now/UtcNow`（不可复现 ⇒ 回放与测试都不稳定）。
    /// 所以时间戳用 **`Time.frameCount` + `Time.realtimeSinceStartup`**：
    /// 帧号足够定位"点了第几帧、隔了多少帧才有反应"，而它不会把不确定性引进来。
    ///
    /// ## 设计约束
    /// 1. **绝不抛异常** —— 取证设施反过来弄坏游戏是最坏的结局，所有 IO 都吞掉异常。
    /// 2. **有上限** —— 超过 <see cref="MaxLines"/> 行就整体重开（保留最近的一段），
    ///    防止长时间挂着玩把存储写满。
    /// 3. **每条都带上下文** —— 光记"点了一下"没用；要记**判定结果**（算不算命中、走的哪条输入路径、
    ///    屏幕坐标、命中的 GameObject 名），否则复现时仍然只能猜。
    /// </summary>
    public static class ClickReceipt
    {
        /// <summary>落盘文件的行数上限。超过则截断重写（保留最近一半）。</summary>
        const int MaxLines = 2000;

        static string _path;
        static int _lines;
        static bool _broken;          // 一旦 IO 出错就彻底放弃，不再每帧重试（避免拖慢游戏）

        /// <summary>回执文件路径。真机上用文件管理器打开这个路径即可。</summary>
        public static string Path
        {
            get
            {
                if (_path == null)
                {
                    try { _path = System.IO.Path.Combine(Application.persistentDataPath, "click-receipt.log"); }
                    catch { _path = "click-receipt.log"; }
                }
                return _path;
            }
        }

        /// <summary>最近一条回执（HUD 打开时可显示这一行，不必去翻文件）。</summary>
        public static string LastLine { get; private set; }

        /// <summary>
        /// 写一条回执。<paramref name="kind"/> 是事件类别（如 <c>touch</c>/<c>click</c>/<c>hit</c>），
        /// <paramref name="detail"/> 是自由文本。
        ///
        /// **本方法永不抛异常。** 任何 IO 失败都只记 <see cref="_broken"/> 并静默返回 ——
        /// 不能让"记录问题"这件事本身变成新问题。
        /// </summary>
        public static void Write(string kind, string detail)
        {
            if (_broken) return;
            try
            {
                var sb = new StringBuilder(160);
                sb.Append("f=").Append(Time.frameCount);
                sb.Append(" t=").Append(Time.realtimeSinceStartup.ToString("F2"));
                sb.Append(" [").Append(kind).Append("] ");
                sb.Append(detail);
                var line = sb.ToString();
                LastLine = line;

                if (_lines >= MaxLines) Roll();
                File.AppendAllText(Path, line + "\n");
                _lines++;
            }
            catch { _broken = true; }
        }

        /// <summary>截断重写：保留最近约一半行。用于长时间运行后防止文件无限增长。</summary>
        static void Roll()
        {
            try
            {
                var all = File.Exists(Path) ? File.ReadAllLines(Path) : new string[0];
                var keep = new string[all.Length / 2];
                System.Array.Copy(all, all.Length - keep.Length, keep, 0, keep.Length);
                File.WriteAllLines(Path, keep);
                _lines = keep.Length;
            }
            catch { _broken = true; }
        }

        /// <summary>开局写一行头（含时间与设备信息），便于把多份回执区分开。</summary>
        public static void Begin(string tag)
        {
            Write("begin", tag
                + " screen=" + Screen.width + "x" + Screen.height
                + " dpi=" + Screen.dpi
                + " touchSupported=" + Input.touchSupported
                + " path=" + Path);
        }
    }
}
