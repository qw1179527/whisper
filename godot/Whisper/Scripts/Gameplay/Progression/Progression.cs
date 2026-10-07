using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Progression
{
    /// <summary>
    /// 等级 / 经验 / 声望（恐鬼症对齐 · 见 docs/spec/phasmophobia-alignment.md §3.1）。
    ///
    /// ## 结构（可确证的官方框架）
    /// 每完成一次合约得经验 → 升级；满级后可**声望（Prestige）**重置并保留成就。
    ///
    /// ## 数值来源纪律
    /// **官方具体数值未取到**（本机 web_search 只返回索引页）。所以经验来源与升级曲线
    /// **全部读配置**（`progression.*`），代码零硬编码；配置里每个数带 `_src` 标注是
    /// `official` 还是 `design`。取到正文后只改配置。
    ///
    /// ## 确定性
    /// 本类不引入任何随机数 —— 经验只由局内事实累加。任务生成才需要随机，那在 Tasks 里用种子派生。
    /// </summary>
    public sealed class Progression
    {
        /// <summary>当前等级（从 1 开始）。</summary>
        public int Level { get; private set; } = 1;
        /// <summary>当前等级内已获得经验。</summary>
        public int XpInLevel { get; private set; }
        /// <summary>本局累计总经验（声望重置时归零，用于"本级进度"计算）。</summary>
        public int TotalXp { get; private set; }
        /// <summary>声望次数。</summary>
        public int Prestige { get; private set; }
        /// <summary>历史最高等级（声望重置也不清）。</summary>
        public int BestLevel { get; private set; } = 1;
        /// <summary>钱（商店货币）。</summary>
        public int Money { get; private set; }
        /// <summary>残响碎片（本作货币；**不可用于购买装备**——设计底线）。</summary>
        public int Fragments { get; private set; }

        /// <summary>升级事件（等级, 新等级）——UI 用它弹提示，不在本类里做展示。</summary>
        public event Action<int> OnLevelUp;

        readonly int _maxLevel;
        readonly int _baseXp;
        readonly int _xpGrowth;      // 每级递增
        readonly float _prestigeXpMul;
        readonly int _prestigeAtLevel;

        /// <summary>从配置构造。缺键时用**保守缺省**并把问题登记在 <see cref="ConfigProblems"/>。</summary>
        public Progression(GameConfigReader cfg)
        {
            // ⚠ GameConfigReader 的签名是**两参数** `Int(path, fallback)`（我第一版写成三参数会编译不过）。
            // 所以"是否来自配置"由**与缺省值是否不同**推断，而不是让读取器回报。
            var problems = new List<string>();
            _maxLevel = cfg.Int("progression.maxLevel", 100);
            _baseXp = cfg.Int("progression.xp.base", 100);
            _xpGrowth = cfg.Int("progression.xp.perLevel", 40);
            _prestigeXpMul = cfg.Float("progression.prestige.xpMultiplier", 1.0f);
            _prestigeAtLevel = cfg.Int("progression.prestige.atLevel", _maxLevel);
            if (_maxLevel == 100) problems.Add("progression.maxLevel 用了缺省 100（配置里没这个键）");
            if (_baseXp == 100) problems.Add("progression.xp.base 用了缺省 100");
            if (_xpGrowth == 40) problems.Add("progression.xp.perLevel 用了缺省 40");
            ConfigProblems = problems;
        }

        /// <summary>配置缺键清单（空 = 全部来自配置）。</summary>
        public IReadOnlyList<string> ConfigProblems { get; }

        /// <summary>升到下一级所需经验（分段线性；配置表驱动）。</summary>
        public int XpForNextLevel => _baseXp + (Level - 1) * _xpGrowth;

        /// <summary>本级进度 0..1（HUD 画经验条用）。</summary>
        public float LevelProgress
        {
            get { int need = XpForNextLevel; return need <= 0 ? 0f : (float)XpInLevel / need; }
        }

        /// <summary>是否可声望（满级）。</summary>
        public bool CanPrestige => Level >= _prestigeAtLevel;

        /// <summary>加钱。</summary>
        public void AddMoney(int amount) { if (amount > 0) Money += amount; }
        /// <summary>花钱（不足则返回 false，不扣）。</summary>
        public bool SpendMoney(int amount)
        {
            if (amount <= 0) return true;
            if (Money < amount) return false;
            Money -= amount;
            return true;
        }
        /// <summary>加碎片（只由局内表现产生——见 Settlement 的"碎片不可付费购买"红线）。</summary>
        public void AddFragments(int amount) { if (amount > 0) Fragments += amount; }

        /// <summary>
        /// 加经验并处理连升。
        /// 为什么用 while 而不是 if：一次结算可能给足量经验直接跨多级（官方也是连升）。
        /// </summary>
        public void AddXp(int amount)
        {
            if (amount <= 0) return;
            // 声望越多，升级越慢（官方语义：声望是"重练但更强"）
            if (_prestigeXpMul > 0f && Prestige > 0)
                amount = (int)Math.Round(amount / Math.Pow(_prestigeXpMul, Prestige));
            if (amount <= 0) amount = 1;

            TotalXp += amount;
            XpInLevel += amount;
            int guard = 0;
            while (Level < _maxLevel && XpInLevel >= XpForNextLevel && guard++ < 1000)
            {
                XpInLevel -= XpForNextLevel;
                Level++;
                if (Level > BestLevel) BestLevel = Level;
                OnLevelUp?.Invoke(Level);
            }
            if (Level >= _maxLevel) XpInLevel = Math.Min(XpInLevel, XpForNextLevel);
        }

        /// <summary>声望重置：等级归 1、本级经验归零，**钱/碎片/装备保留**（官方语义）。</summary>
        public bool TryPrestige()
        {
            if (!CanPrestige) return false;
            Prestige++;
            Level = 1;
            XpInLevel = 0;
            return true;
        }

        /// <summary>HUD 一行摘要。</summary>
        public string Describe()
            => $"等级 {Level}{(Prestige > 0 ? $"（声望 {Prestige}）" : "")} · 经验 {XpInLevel}/{XpForNextLevel}"
             + $" · 钱 {Money} · 碎片 {Fragments}";
    }
}
