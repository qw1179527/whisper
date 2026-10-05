using System;
using System.Collections.Generic;
using Whisper.Gameplay.Config;

namespace Whisper.Gameplay.Progression
{
    /// <summary>商店里一件装备的定义（恐鬼症对齐 · 见 spec §3.2）。</summary>
    public struct ShopItem
    {
        public string Id;
        /// <summary>层级：1/2/3（官方 Tier I/II/III 语义：越高越好用越贵）。</summary>
        public int Tier;
        public int Price;
        public float WeightKg;
        public string Label;
        /// <summary>该装备的效果数值（例如手电照射距离、EMF 判定窗口），局内系统按 id 读。</summary>
        public Dictionary<string, float> Effects;
    }

    /// <summary>
    /// 商店 / 装备（恐鬼症对齐 · spec §3.2）。
    ///
    /// ## 结构（可确证的官方框架）
    /// 用**合约赚的钱**买装备；同种装备分 **Tier I/II/III**。买了即"拥有"，可装备/卸下。
    ///
    /// ## 两条硬规则
    /// 1. **Tier 递进**：要买 Tier N 必须先拥有同 id 的 Tier N-1（官方语义，防止跳级买最强）。
    /// 2. **碎片不可购买装备** —— 本工程的 `Fragments` 与 `Money` 是两条通道，
    ///    商店只认 `Money`。这是 spec 里写死的设计底线，不允许在别处开口子。
    ///
    /// ## 数值来源
    /// 装备表**从配置读**（`shop.items[]`），代码零硬编码。官方具体价格未取到 → 配置里标 `design`。
    /// </summary>
    public sealed class Shop
    {
        readonly Dictionary<string, ShopItem> _items = new Dictionary<string, ShopItem>(StringComparer.Ordinal);
        readonly HashSet<string> _owned = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _equipped = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>载入装备表。</summary>
        public void Load(GameConfigReader cfg)
        {
            _items.Clear();
            // 【2026-10-05 修正】原先这里读的是**静态** GameConfig.Get，于是构造/载入时传进来的 cfg
            // 完全没用上 —— 注入配置的测试与替身永远读到空表（EditMode 当场抓到 "没有这件装备"）。
            // 正解：走传进来的 cfg（GameConfigReader 现在有 Get 了）。
            var arr = cfg.Get("shop.items") as System.Collections.IList;
            if (arr == null) { LoadProblem = "配置里没有 shop.items"; return; }
            for (int i = 0; i < arr.Count; i++)
            {
                if (!(arr[i] is Dictionary<string, object> m)) continue;
                var it = new ShopItem
                {
                    Effects = new Dictionary<string, float>(StringComparer.Ordinal),
                };
                it.Id = m.TryGetValue("id", out var v) && v is string s ? s : "item" + i;
                it.Tier = m.TryGetValue("tier", out var t) && t is long tl ? (int)tl : 1;
                it.Price = m.TryGetValue("price", out var p) && p is long pl ? (int)pl : 0;
                it.WeightKg = m.TryGetValue("weightKg", out var w) && w is double wd ? (float)wd
                           : (m.TryGetValue("weightKg", out var w2) && w2 is long wl ? wl : 0f);
                it.Label = m.TryGetValue("label", out var lb) && lb is string ls ? ls : it.Id;
                if (m.TryGetValue("effects", out var e) && e is Dictionary<string, object> em)
                    foreach (var kv in em)
                        it.Effects[kv.Key] = kv.Value is double d ? (float)d : (kv.Value is long l2 ? l2 : 0f);
                _items[it.Id] = it;
            }
            LoadProblem = _items.Count == 0 ? "shop.items 为空" : null;
        }

        /// <summary>载入失败原因（null = 正常）。</summary>
        public string LoadProblem { get; private set; }
        /// <summary>装备表条目数。</summary>
        public int Count => _items.Count;

        /// <summary>按 id 取定义。</summary>
        public bool TryGet(string id, out ShopItem item) => _items.TryGetValue(id ?? "", out item);

        /// <summary>是否已拥有。</summary>
        public bool IsOwned(string id) => _owned.Contains(id ?? "");
        /// <summary>已拥有数量。</summary>
        public int OwnedCount => _owned.Count;
        /// <summary>已拥有 id 快照（HUD/存档用）。</summary>
        public IReadOnlyCollection<string> Owned => _owned;

        /// <summary>
        /// 能否购买：钱够 + 未拥有 + **层级递进**（Tier N 需先有 Tier N-1）。
        /// </summary>
        public bool CanBuy(Progression prog, string id, out string reason)
        {
            reason = null;
            if (!_items.TryGetValue(id ?? "", out var it)) { reason = "没有这件装备：" + id; return false; }
            if (_owned.Contains(id)) { reason = "已拥有"; return false; }
            if (prog == null) { reason = "缺少档案"; return false; }
            if (prog.Money < it.Price) { reason = $"钱不够（需 {it.Price}，有 {prog.Money}）"; return false; }
            // 层级递进：找同 id 的下一层（Tier 更低）必须已拥有
            if (it.Tier > 1 && !HasPreviousTier(it))
            { reason = $"需先拥有 Tier {it.Tier - 1}"; return false; }
            return true;
        }

        /// <summary>执行购买（成功才扣钱）。</summary>
        public bool TryBuy(Progression prog, string id, out string reason)
        {
            if (!CanBuy(prog, id, out reason)) return false;
            var it = _items[id];
            if (!prog.SpendMoney(it.Price)) { reason = "扣款失败"; return false; }
            _owned.Add(id);
            reason = $"已购买 {it.Label}（Tier {it.Tier}）−{it.Price}";
            return true;
        }

        /// <summary>装备一件已拥有的装备（按"槽位"= 装备 id 的前缀，见 <see cref="SlotOf"/>）。</summary>
        public bool TryEquip(string id, out string reason)
        {
            if (!_items.TryGetValue(id ?? "", out var it)) { reason = "没有这件装备：" + id; return false; }
            if (!_owned.Contains(id)) { reason = "尚未拥有"; return false; }
            string slot = SlotOf(id);
            _equipped[slot] = id;
            reason = $"已装备 {it.Label}";
            return true;
        }

        /// <summary>卸下某槽位。</summary>
        public bool TryUnequip(string slot, out string reason)
        {
            if (_equipped.Remove(slot ?? "")) { reason = "已卸下 " + slot; return true; }
            reason = "该槽位未装备";
            return false;
        }

        /// <summary>取某槽位当前装备的 id（无则 null）。</summary>
        public string Equipped(string slot)
            => _equipped.TryGetValue(slot ?? "", out var v) ? v : null;

        /// <summary>取某槽位装备的效果值（局内系统读它，例如 hand flashlight 的 rangeM）。</summary>
        public float EquippedEffect(string slot, string key, float fallback)
        {
            string id = Equipped(slot);
            if (id != null && _items.TryGetValue(id, out var it) && it.Effects.TryGetValue(key, out var v)) return v;
            return fallback;
        }

        /// <summary>已装备的槽位清单（HUD 用）。</summary>
        public IReadOnlyDictionary<string, string> EquippedMap => _equipped;

        /// <summary>槽位 = 装备 id 去掉 `_tN` 后缀（`emf_t2` → `emf`）。</summary>
        public static string SlotOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            int i = id.LastIndexOf("_t", StringComparison.Ordinal);
            return i > 0 ? id.Substring(0, i) : id;
        }

        bool HasPreviousTier(ShopItem it)
        {
            string prev = SlotOf(it.Id) + "_t" + (it.Tier - 1);
            return _owned.Contains(prev);
        }

        /// <summary>某装备的下一层（用于商店 UI 的"可升级"提示）。</summary>
        public string NextTierOf(string id)
        {
            string next = SlotOf(id) + "_t" + (TierOf(id) + 1);
            return _items.ContainsKey(next) ? next : null;
        }

        int TierOf(string id) => _items.TryGetValue(id ?? "", out var it) ? it.Tier : 0;

        /// <summary>HUD 一行摘要。</summary>
        public string Describe()
            => LoadProblem != null ? $"商店：{LoadProblem}"
             : $"商店：{_items.Count} 件 · 已拥有 {_owned.Count} · 已装备 {_equipped.Count}";
    }
}
