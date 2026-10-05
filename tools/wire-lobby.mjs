// 主界面扩为**大厅**：左侧等级/钱/碎片 + 任务板，右侧加「商店」「每日任务」按钮（恐鬼症对齐 spec §3.6）。
// **本文件不得出现反引号**（会截断 JS 模板，已失败 9 次）。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const P = path.join(ROOT, 'unity/Assets/Scripts/Runtime/MenuScene.cs');
let s = fs.readFileSync(P, 'utf8');
const log = [];

// ① 拿到组合根引用（读档案用）
if (!s.includes('_boot')) {
  s = s.replace('        Camera _cam;',
`        Camera _cam;
        /// <summary>组合根（读等级/商店/任务；主界面不自己持有档案，避免两份真相源）。</summary>
        GameBootstrap _boot;
        /// <summary>大厅左侧信息面板（等级/钱/碎片/任务）。</summary>
        Text _lobbyPanel;`);
  s = s.replace('        public void Build(Transform parent, Camera cam)\n        {\n            if (_root != null) return;\n            _cam = cam;',
`        public void Build(Transform parent, Camera cam, GameBootstrap boot = null)
        {
            if (_root != null) return;
            _cam = cam;
            _boot = boot;`);
  log.push('  ✓ 组合根引用');
}

// ② 按钮清单加「商店」「每日任务」
const oldItems = '            string[] items = { "开始调查（单人）", "创建房间（多人）", "加入房间（IPv6 直连）", "音量与语音", "退出" };';
const newItems = '            // 恐鬼症大厅的入口结构：开始 / 商店 / 任务 / 联机（spec §3.6）\n'
               + '            string[] items = { "开始调查（单人）", "商店", "每日任务", "创建房间（多人）", "加入房间（IPv6 直连）", "退出" };';
if (s.includes(oldItems)) { s = s.replace(oldItems, newItems); log.push('  ✓ 按钮清单'); }

// ③ OnOption 分支改成新索引
const oldSwitch = `            switch (index)
            {
                case 0: // 单人
                    if (Services.HasNet) ((Whisper.Net.LocalNetService)Services.Net).SetPhase(Whisper.Core.Contracts.MatchPhase.Playing);
                    StartMatch();
                    break;
                case 1: // 建房间
                    if (Services.HasNet)
                    {`;
const newSwitch = `            switch (index)
            {
                case 0: // 单人
                    if (Services.HasNet) ((Whisper.Net.LocalNetService)Services.Net).SetPhase(Whisper.Core.Contracts.MatchPhase.Playing);
                    StartMatch();
                    break;
                case 1: OpenShop(); break;
                case 2: ShowTasks(); break;
                case 3: // 建房间
                    if (Services.HasNet)
                    {`;
if (s.includes(oldSwitch)) { s = s.replace(oldSwitch, newSwitch); log.push('  ✓ switch 分支'); }

// ④ 旧 case 2/3 的"加入房间/音量"顺延到 4/5
s = s.replace('                case 2: // 加入\n                    Note("IPv6 直连：请输入主机地址（联机服务已就绪时可用）");\n                    break;',
              '                case 4: // 加入\n                    Note("IPv6 直连：请输入主机地址（联机服务已就绪时可用）");\n                    break;');
s = s.replace('                case 3:\n                    Note("音量与语音：语音服务 " + (Services.HasVoice ? "已注入" : "未注入"));\n                    break;',
              '                case 5:\n                    Note("退出：真机上请用系统返回键"); break;');

// ⑤ 大厅信息面板 + 商店/任务两个方法
if (!s.includes('void OpenShop()')) {
  s = s.replace('        void Note(string s) { if (_hint != null) _hint.text = s; }',
`        void Note(string s) { if (_hint != null) _hint.text = s; }

        /// <summary>商店面板：列出可买/已拥有/可升级，并允许点按钮直接买第一件买得起的。</summary>
        /// <remarks>
        /// 取舍说明：恐鬼症商店是完整 UI（分类/图标/重量/预览）。本工程先用**文字面板**把
        /// "Tier 递进 + 钱 + 已拥有"这三件机制跑通并可见 —— 机制先于皮。数值与规则都在 Shop 里，
        /// 换成图形 UI 时不需要动逻辑。
        /// </remarks>
        void OpenShop()
        {
            if (_boot == null || _boot.Shop == null || _lobbyPanel == null)
            { Note("商店不可用（档案未就绪）"); return; }
            var shop = _boot.Shop;
            var prog = _boot.Progression;
            var sb = new System.Text.StringBuilder();
            sb.Append("商店 · 钱 ").Append(prog != null ? prog.Money : 0).Append("（碎片不可用于购买）\\n");
            int shown = 0, bought = 0;
            foreach (var it in _shopAll)
            {
                bool owned = shop.IsOwned(it.Id);
                string mark = owned ? "已拥有" : (shop.CanBuy(prog, it.Id, out var why) ? "可买" : why);
                sb.Append("  ").Append(it.Label).Append(" · T").Append(it.Tier)
                  .Append(" · ").Append(it.Price).Append(" · ").Append(mark).Append('\\n');
                if (++shown >= 8) break;
                // 顺手买下第一件"可买"的（按钮点击即购买 —— 没有子菜单的最简交互）
                if (!owned && bought == 0 && shop.TryBuy(prog, it.Id, out var msg)) { bought++; sb.Append("  → ").Append(msg).Append('\\n'); }
            }
            _lobbyPanel.text = sb.ToString();
        }

        /// <summary>任务面板：3 日 + 1 周 + 进度条数字。</summary>
        void ShowTasks()
        {
            if (_boot == null || _boot.Tasks == null || _lobbyPanel == null)
            { Note("任务不可用（档案未就绪）"); return; }
            _lobbyPanel.text = _boot.Tasks.Describe();
        }

        /// <summary>刷新大厅左侧面板（每 0.5s 一次，别每帧拼字符串）。</summary>
        void RefreshLobby()
        {
            if (_lobbyPanel == null) return;
            if (_boot == null || _boot.Progression == null) { _lobbyPanel.text = "大厅：档案未就绪"; return; }
            var p = _boot.Progression;
            var sb = new System.Text.StringBuilder();
            sb.Append(p.Describe()).Append('\\n');
            sb.Append("经验条 ").Append((p.LevelProgress * 100f).ToString("F0")).Append("%");
            if (p.CanPrestige) sb.Append(" · 可声望");
            sb.Append('\\n');
            if (_boot.Shop != null) sb.Append(_boot.Shop.Describe()).Append('\\n');
            if (_boot.Power != null) sb.Append(_boot.Power.Describe()).Append('\\n');
            if (_boot.Interaction != null) sb.Append(_boot.Interaction.Describe()).Append('\\n');
            sb.Append("（点右侧「商店」/「每日任务」查看详情）");
            _lobbyPanel.text = sb.ToString();
        }`);
  log.push('  ✓ 商店/任务/大厅面板');
}

// ⑥ 建大厅面板控件
if (!s.includes('LobbyPanel')) {
  s = s.replace('            _hint = hint;',
`            _hint = hint;
            // 大厅左侧信息面板：等级/经验/钱/碎片 + 商店/电力/互动状态（spec §3.6）
            _lobbyPanel = SceneMaterials.Label(canvasGo.transform, "LobbyPanel", "大厅：加载中",
                new Vector2(0f, 1f), new Vector2(0f, 1f), 18, TextAnchor.UpperLeft);
            _lobbyPanel.color = new Color(0.78f, 0.82f, 0.90f);
            _lobbyPanel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _lobbyPanel.rectTransform.pivot = new Vector2(0f, 1f);
            _lobbyPanel.rectTransform.anchoredPosition = new Vector2(24f, -190f);
            _lobbyPanel.rectTransform.sizeDelta = new Vector2(760f, 420f);`);
  log.push('  ✓ 大厅面板控件');
}

// ⑦ _shopAll 缓存（商店列表按 tier 排序）
if (!s.includes('_shopAll')) {
  s = s.replace('        Text _lobbyPanel;',
`        Text _lobbyPanel;
        /// <summary>商店条目缓存（按 tier、id 排序，保证"先 Tier I 后 II"的展示顺序稳定）。</summary>
        readonly System.Collections.Generic.List<Whisper.Gameplay.Progression.ShopItem> _shopAll =
            new System.Collections.Generic.List<Whisper.Gameplay.Progression.ShopItem>();
        float _nextLobbyRefresh;`);
  s = s.replace('            PlaceMenuCamera();',
`            PlaceMenuCamera();
            if (_boot != null && _boot.Shop != null)
            {
                // 商店条目来自配置；这里排一次序（tier 升序、同 tier 按 id）——
                // 顺序稳定对玩家很重要：每次打开商店看到的位置都一样。
                var ids = new System.Collections.Generic.List<string>();
                // 直接遍历配置里的 id（Shop 不暴露全表，故从配置取）
                var arr = Whisper.Gameplay.Config.GameConfig.Get("shop.items") as System.Collections.IList;
                if (arr != null)
                    for (int i = 0; i < arr.Count; i++)
                        if (arr[i] is System.Collections.Generic.Dictionary<string, object> m
                            && m.TryGetValue("id", out var v) && v is string id) ids.Add(id);
                ids.Sort(System.StringComparer.Ordinal);
                foreach (var id in ids) if (_boot.Shop.TryGet(id, out var it)) _shopAll.Add(it);
                _shopAll.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : string.CompareOrdinal(a.Id, b.Id));
            }`);
  log.push('  ✓ 商店条目缓存');
}

// ⑧ Update 里定期刷新大厅面板
if (!s.includes('RefreshLobby();')) {
  s = s.replace('            float dt = Time.deltaTime;',
`            float dt = Time.deltaTime;

            // 大厅面板 0.5s 刷一次（每帧拼字符串会白白产生 GC）
            _nextLobbyRefresh -= dt;
            if (_nextLobbyRefresh <= 0f) { _nextLobbyRefresh = 0.5f; RefreshLobby(); }`);
  log.push('  ✓ 定期刷新');
}

fs.writeFileSync(P, s, 'utf8');
console.log(log.join('\n'));
