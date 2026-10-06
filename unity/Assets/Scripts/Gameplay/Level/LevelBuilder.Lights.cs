namespace Whisper.Gameplay.Level
{
    /// <summary>
    /// <see cref="LevelBuilder"/> 的**场景灯光**部分（partial）。
    ///
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// 为什么单独拆一个文件（2026-10-06）
    /// ══════════════════════════════════════════════════════════════════════════════════
    /// ① **规模纪律**：把建灯接进 `Build()` 后主文件涨到 606 行，触发 `gate-code` C5（>600 判红）。
    ///    门禁是对的 —— 拆法按职责：灯光那块自成一体（入口是 `BuildSceneLights`）。
    /// ② **它承载的是一处产品级缺陷的修复**，值得有自己的位置：
    ///    `LightRig.Build(...)` 早就写好（分区强度 / 闪烁相位 / 停电按房间关灯 /
    ///    用 FNV-1a 保证跨端相位一致），但**全仓只有 `HallScene`（大厅·菜单场景）调过它**
    ///    ⇒ **调查关卡里一盏灯都没有**。取证读数原文：
    ///    &gt; `[RENDER][灯] 场景灯 0 盏：点光 0 · 平行光 0 · 聚光 0 · 关闭/零强度 0`
    ///    后果是 `morgue_deep` 渲成**纯黑**（离主光最远、又无房间点光）。
    ///
    ///    而 `LevelBuilder.LightRig.cs` 的类注释**原本就写着**用法：
    ///    &gt; `var rig = LightRig.Build(levelGo.transform, level);   // 布景后建灯`
    ///    —— 漏了这一句。这是本项目反复出现的失效形态：**能力已存在，但没接上**。
    /// </summary>
    public sealed partial class LevelBuilder
    {
        /// <summary>
        /// 按房间布点光源。**必须在房间/墙/门/道具都建好之后调用** ——
        /// 灯的落点由房间盒推出（长边每 <see cref="LightRig.SpacingM"/> 一盏，灯高
        /// <see cref="LightRig.LampHeightM"/>），房间没建好就没有可依据的盒子。
        ///
        /// 返回建的灯数（诊断/取证可读）。
        /// </summary>
        public int BuildSceneLights(LevelData level)
        {
            if (level == null) return 0;
            var rig = LightRig.Build(transform, level);
            SceneLights = rig;
            return rig != null ? rig.Lights.Count : 0;
        }

        /// <summary>本场景的灯光装置（null = 未建）；停电事件等玩法通过它按房间关灯。</summary>
        public LightRig SceneLights { get; private set; }
    }
}
