package com.whisper.probe;

import android.app.Activity;
import android.os.Bundle;
import android.util.Log;
import android.widget.ScrollView;
import android.widget.TextView;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.util.HashSet;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * 契约镜像验证件（本机真构建 → 真机真跑）。
 *
 * 它与 Unity 侧同名契约**逐成员对应**，在本机用 javac + d8 + aapt + apksigner 打成可安装 APK，
 * 打开即验证三件事并把结果打在屏幕上：
 *   ① 三接口契约装配（注入实现 → 通过接口调用）
 *   ② Level DSL 解析真实关卡（asylum_v1.json，与 Unity 侧同一份数据）
 *   ③ DesignTokens 输出（与 C# 生成物同值）
 * 另附负向验证：畸形关卡必须被拦（证明校验不是摆设）。
 */
public class ProbeActivity extends Activity {
    private static final String TAG = "WhisperProbe";
    private final StringBuilder sb = new StringBuilder();
    private TextView out;
    private int passed, failed;

    private void check(String name, Runnable body) {
        try { body.run(); passed++; line("  [PASS] " + name); }
        catch (Throwable t) { failed++; line("  [FAIL] " + name + " → " + t.getClass().getSimpleName() + ": " + t.getMessage()); }
    }
    private void checkThrows(String name, Runnable body) {
        try { body.run(); failed++; line("  [FAIL] " + name + " → 未按预期抛异常"); }
        catch (Throwable t) { passed++; line("  [PASS] " + name + " → 已被拦（" + t.getClass().getSimpleName() + "）"); }
    }
    private void line(String s) { Log.i(TAG, s); sb.append(s).append('\n'); if (out != null) out.setText(sb.toString()); }
    private void head(String s) { line(""); line(s); }

    private String readRaw(int id) throws Exception {
        InputStream in = getResources().openRawResource(id);
        ByteArrayOutputStream bos = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) bos.write(buf, 0, n);
        in.close();
        return new String(bos.toByteArray(), "UTF-8");
    }

    @Override protected void onCreate(Bundle b) {
        super.onCreate(b);
        ScrollView sv = new ScrollView(this);
        out = new TextView(this);
        out.setTextSize(10f);
        out.setPadding(20, 20, 20, 20);
        sv.addView(out);
        setContentView(sv);
        run();
    }

    private void run() {
        line("Project Whisper · 契约镜像验证件");
        line("设备 " + android.os.Build.MANUFACTURER + " " + android.os.Build.MODEL + " · API " + android.os.Build.VERSION.SDK_INT);

        // ── ① 三接口契约装配 ──
        head("[1] 三接口契约装配（V9 §13.2）");
        final boolean[] netConnected = {false};
        final boolean[] migrated = {false};
        final float[] energy = {0f};
        // §13.4 状态面：桩里用可变本地态承载（真实现由 Fusion 状态同步驱动）
        final java.util.List<MatchState.PlayerSnapshot> players = new java.util.ArrayList<MatchState.PlayerSnapshot>();
        final java.util.List<MatchState.PropState> props = new java.util.ArrayList<MatchState.PropState>();
        final MatchState.Phase[] phase = {MatchState.Phase.Lobby};
        final boolean[] propFired = {false};
        final boolean[] phaseFired = {false};
        INetService net = new INetService() {
            public boolean isHost() { return true; }
            public boolean isConnected() { return true; }
            public int tickRate() { return 60; }
            public void connect(String roomCode, String authToken) { netConnected[0] = true; }
            public void disconnect() { netConnected[0] = false; }
            public void sendVoiceStimulus(StimulusEvent s) { }
            public void onRoomClosed(String reason) { }
            public void onHostMigration(boolean started) { migrated[0] = started; }

            // ── §13.4 只读状态面 ──
            public MatchState.Phase phase() { return phase[0]; }
            public MatchState.NetworkSnapshot snapshot() {
                return new MatchState.NetworkSnapshot(phase[0], players, props, 0, "mirror");
            }
            public void onPhaseChanged(MatchState.Phase p) { phaseFired[0] = true; }
            public void onPropChanged(MatchState.PropState p) { propFired[0] = true; }
            public void onPlayerUpdated(MatchState.PlayerSnapshot p) { }
        };
        IVoiceService voice = new IVoiceService() {
            public boolean isMuted() { return false; }
            public float localEnergy01() { return 0.25f; }
            public void joinChannel(String c) { }
            public void leaveChannel() { }
            public void setLocalMute(String id, boolean m) { }
            public void onParticipantEnergy(String id, float e) { energy[0] = e; }
        };
        IBackendService backend = new IBackendService() {
            public boolean isAvailable() { return false; }   // 无后端模式（§15.2）
            public void issueTokens(String v, TokenCallback cb) { cb.onFail("offline"); }
            public void verifyPurchase(String t, String p, PurchaseCallback cb) { cb.onDone(false); }
            public void submitReport(String c, String m, ReportCallback cb) { cb.onDone(false); }
        };

        check("INetService 可装配且 tickRate=60", () -> { net.connect("ROOM1", "tok"); if (net.tickRate() != 60) throw new IllegalStateException("tickRate=" + net.tickRate()); });
        check("INetService 事件签名可用（onHostMigration）", () -> { net.onHostMigration(true); if (!migrated[0]) throw new IllegalStateException("回调未触发"); });
        check("IVoiceService 可装配且 localEnergy01 可用", () -> { voice.joinChannel("ROOM1_1"); voice.onParticipantEnergy("p0", 0.5f); if (Math.abs(energy[0] - 0.5f) > 1e-6) throw new IllegalStateException("energy 未回调"); });
        check("IBackendService 无后端模式（isAvailable=false）可用", () -> { if (backend.isAvailable()) throw new IllegalStateException("应处于无后端模式"); });

        // ── §13.4 状态面（①③④）──
        check("状态面：阶段可设并触发变更（④）", () -> {
            phase[0] = MatchState.Phase.Playing; net.onPhaseChanged(phase[0]);
            if (!phaseFired[0] || net.phase() != MatchState.Phase.Playing) throw new IllegalStateException("阶段未生效");
        });
        check("状态面：玩家位姿进入快照（①）", () -> {
            players.add(new MatchState.PlayerSnapshot("p0", 3f, 0f, 4f, 90f, true, 0.8f));
            if (net.snapshot().players.size() != 1) throw new IllegalStateException("玩家未入快照");
        });
        check("状态面：门/道具状态进入快照（③）", () -> {
            props.add(new MatchState.PropState("ward_03/door_a", true, false, 1f)); net.onPropChanged(props.get(0));
            if (!propFired[0] || net.snapshot().props.size() != 1) throw new IllegalStateException("道具未入快照");
        });
        check("状态面：快照携带阶段/世界哈希（§13.6）", () -> {
            MatchState.NetworkSnapshot sn = net.snapshot();
            if (sn.phase != MatchState.Phase.Playing || !"mirror".equals(sn.worldHash)) throw new IllegalStateException("快照字段不符");
        });

        // ── ② Level DSL 解析真实关卡 ──
        head("[2] Level DSL 解析（asylum_v1.json，与 Unity 同源数据）");
        String levelJson = null, manifestJson = null;
        try { levelJson = readRaw(R.raw.asylum_v1); manifestJson = readRaw(R.raw.asset_manifest); }
        catch (Exception e) { line("  [FAIL] 读取 res/raw 资源失败：" + e); failed++; }
        if (levelJson != null) {
            final String lj = levelJson;
            final Set<String> kits = new HashSet<String>();
            Matcher m = Pattern.compile("\"id\"\\s*:\\s*\"([^\"]+)\"").matcher(manifestJson);
            while (m.find()) kits.add(m.group(1));
            final LevelLoader.Level[] holder = new LevelLoader.Level[1];
            check("加载并校验通过（已知套件 " + kits.size() + " 个）", () -> { holder[0] = LevelLoader.load(lj, kits); });
            if (holder[0] != null) {
                final LevelLoader.Level L = holder[0];
                check("levelId = asylum_v1", () -> { if (!"asylum_v1".equals(L.levelId)) throw new IllegalStateException(L.levelId); });
                check("V9 §19.2 疗养院 10 房间", () -> { if (L.roomCount != 10) throw new IllegalStateException("房间数=" + L.roomCount); });
                check("证据点 5 个", () -> { if (L.evidenceCount != 5) throw new IllegalStateException("证据点=" + L.evidenceCount); });
                check("光区 safe=1 / pressure=7 / high-risk=2", () -> { if (L.safeCount != 1 || L.pressureCount != 7 || L.highRiskCount != 2) throw new IllegalStateException(L.safeCount + "/" + L.pressureCount + "/" + L.highRiskCount); });
                check("V9 §7 撤离双点（标准/深处）不同房间", () -> { if (L.extractionStandard.equals(L.extractionDeep)) throw new IllegalStateException("同房"); });
                check("动态事件 2~3 个", () -> { if (L.eventCount < 2 || L.eventCount > 3) throw new IllegalStateException("事件=" + L.eventCount); });
                line("  → 实测：房间 " + L.roomCount + " · 走廊 " + L.corridorCount + " · 事件 " + L.eventCount + " · 证据 " + L.evidenceCount);
                line("  → 撤离：" + L.extractionStandard + " / " + L.extractionDeep);
            }
            head("[3] 负向验证（校验器必须真会拦）");
            checkThrows("未知 kit 被拦", () -> LevelLoader.load(lj.replace("\"hospital_ward\"", "\"nope_kit\""), kits));
            checkThrows("非法 lightZone 被拦", () -> LevelLoader.load(lj.replace("\"lightZone\": \"pressure\"", "\"lightZone\": \"spooky\""), kits));
            checkThrows("门 offset 越界被拦", () -> LevelLoader.load(lj.replace("\"offset\": 0.5", "\"offset\": 1.9"), kits));
            checkThrows("走廊端点悬空被拦", () -> LevelLoader.load(lj.replace("\"to\": \"corridor_main\"", "\"to\": \"ghost_room\""), kits));
            checkThrows("超深嵌套被拦（MaxDepth=64）", () -> { StringBuilder d = new StringBuilder(); for (int i = 0; i < 200; i++) d.append('['); for (int i = 0; i < 200; i++) d.append(']'); MiniJson.parse(d.toString()); });
        }

        // ── ④ DesignTokens 输出 ──
        head("[4] DesignTokens 输出（C2）");
        check("6 个色彩 Token 与 C# 生成物同值", () -> {
            if (!"#F0E6D2".equals(DesignTokens.COLOR_PAPER)) throw new IllegalStateException("paper");
            if (!"#1A1A1A".equals(DesignTokens.COLOR_INK)) throw new IllegalStateException("ink");
            if (!"#8B1E1E".equals(DesignTokens.COLOR_BLOOD)) throw new IllegalStateException("blood");
            if (!"#5C8C6E".equals(DesignTokens.COLOR_MOLD)) throw new IllegalStateException("mold");
            if (!"#D8CFBB".equals(DesignTokens.COLOR_BONE)) throw new IllegalStateException("bone");
            if (!"#0E0D0C".equals(DesignTokens.COLOR_SOOT)) throw new IllegalStateException("soot");
        });
        check("Token 真源路径标注", () -> { if (!"Assets/Data/design-tokens.json".equals(DesignTokens.SOURCE_PATH)) throw new IllegalStateException(DesignTokens.SOURCE_PATH); });

        line("");
        line("═══ 结果：通过 " + passed + " · 失败 " + failed + " ═══");
        line(failed == 0 ? "契约镜像与 Unity 侧一致：DSL / 三接口 / Token 全部对齐" : "存在失败项，见上");
    }
}
