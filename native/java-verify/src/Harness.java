import com.whisper.probe.*;
import java.io.*;
import java.util.*;

/** 无头跑手：把契约镜像与 DSL 加载器的验证逻辑真跑一遍并打印（不依赖 Android 框架）。 */
public class Harness {
    static int passed, failed;
    static List<String> failures = new ArrayList<String>();

    interface Body { void run() throws Exception; }
    static void check(String name, Body b) {
        try { b.run(); passed++; System.out.println("  [PASS] " + name); }
        catch (Throwable t) { failed++; failures.add(name + " → " + t); System.out.println("  [FAIL] " + name + " → " + t.getClass().getSimpleName() + ": " + t.getMessage()); }
    }
    static void checkThrows(String name, Body b) {
        try { b.run(); failed++; failures.add(name + " → 未抛异常"); System.out.println("  [FAIL] " + name + " → 未按预期抛异常"); }
        catch (Throwable t) { passed++; System.out.println("  [PASS] " + name + " → 已拦（" + t.getClass().getSimpleName() + "）"); }
    }

    public static void main(String[] args) throws Exception {
        System.out.println("Project Whisper · Java 契约镜像无头验证（dalvikvm / ART 真跑）");
        String level = read(args[0]);
        String manifest = read(args[1]);
        Set<String> kits = new HashSet<String>();
        java.util.regex.Matcher m = java.util.regex.Pattern.compile("\"id\"\\s*:\\s*\"([^\"]+)\"").matcher(manifest);
        while (m.find()) kits.add(m.group(1));

        System.out.println("\n[1] 三接口契约装配（V9 §13.2）");
        final boolean[] migrated = { false };
        final float[] energy = { 0f };
        INetService net = new INetService() {
            public boolean isHost() { return true; }
            public boolean isConnected() { return true; }
            public int tickRate() { return 60; }
            public void connect(String r, String t) { }
            public void disconnect() { }
            public void sendVoiceStimulus(StimulusEvent s) { }
            public void onRoomClosed(String r) { }
            public void onHostMigration(boolean s) { migrated[0] = s; }
        };
        IVoiceService voice = new IVoiceService() {
            public boolean isMuted() { return false; }
            public float localEnergy01() { return 0.25f; }
            public void joinChannel(String c) { }
            public void leaveChannel() { }
            public void setLocalMute(String i, boolean m) { }
            public void onParticipantEnergy(String i, float e) { energy[0] = e; }
        };
        IBackendService backend = new IBackendService() {
            public boolean isAvailable() { return false; }
            public void issueTokens(String v, IBackendService.TokenCallback cb) { cb.onFail("offline"); }
            public void verifyPurchase(String t, String p, IBackendService.PurchaseCallback cb) { cb.onDone(false); }
            public void submitReport(String c, String m, IBackendService.ReportCallback cb) { cb.onDone(false); }
        };
        check("INetService.tickRate = 60", () -> { if (net.tickRate() != 60) throw new IllegalStateException("" + net.tickRate()); });
        check("INetService.onHostMigration 事件可用", () -> { net.onHostMigration(true); if (!migrated[0]) throw new IllegalStateException("未回调"); });
        check("IVoiceService.localEnergy01 可用", () -> { if (Math.abs(voice.localEnergy01() - 0.25f) > 1e-6) throw new IllegalStateException("值不符"); });
        check("IVoiceService.onParticipantEnergy 回调可用", () -> { voice.onParticipantEnergy("p0", 0.5f); if (Math.abs(energy[0] - 0.5f) > 1e-6) throw new IllegalStateException("未回调"); });
        check("IBackendService 无后端模式可用（§15.2）", () -> { if (backend.isAvailable()) throw new IllegalStateException("应为 false"); });

        System.out.println("\n[2] Level DSL 解析（真实关卡，套件 " + kits.size() + " 个）");
        final LevelLoader.Level[] h = new LevelLoader.Level[1];
        check("加载并校验通过", () -> { h[0] = LevelLoader.load(level, kits); });
        if (h[0] != null) {
            final LevelLoader.Level L = h[0];
            check("levelId = asylum_v1", () -> { if (!"asylum_v1".equals(L.levelId)) throw new IllegalStateException(L.levelId); });
            check("V9 §19.2 疗养院 10 房间", () -> { if (L.roomCount != 10) throw new IllegalStateException("" + L.roomCount); });
            check("证据点 5 个", () -> { if (L.evidenceCount != 5) throw new IllegalStateException("" + L.evidenceCount); });
            check("光区 1/7/2", () -> { if (L.safeCount != 1 || L.pressureCount != 7 || L.highRiskCount != 2) throw new IllegalStateException(L.safeCount + "/" + L.pressureCount + "/" + L.highRiskCount); });
            check("撤离双点不同房间（§7）", () -> { if (L.extractionStandard.equals(L.extractionDeep)) throw new IllegalStateException("同房"); });
            System.out.println("  → 实测：房间 " + L.roomCount + " · 走廊 " + L.corridorCount + " · 事件 " + L.eventCount + " · 证据 " + L.evidenceCount + " · 撤离 " + L.extractionStandard + "/" + L.extractionDeep);
        }

        System.out.println("\n[3] 负向验证（校验器必须真会拦）");
        final String lj = level;
        checkThrows("未知 kit", () -> LevelLoader.load(lj.replace("\"hospital_ward\"", "\"nope_kit\""), kits));
        checkThrows("非法 lightZone", () -> LevelLoader.load(lj.replace("\"lightZone\": \"pressure\"", "\"lightZone\": \"spooky\""), kits));
        checkThrows("门 offset 越界", () -> LevelLoader.load(lj.replace("\"offset\": 0.5", "\"offset\": 1.9"), kits));
        checkThrows("走廊端点悬空", () -> LevelLoader.load(lj.replace("\"to\": \"corridor_main\"", "\"to\": \"ghost_room\""), kits));
        checkThrows("超深嵌套（MaxDepth=64）", () -> {
            StringBuilder d = new StringBuilder();
            for (int i = 0; i < 200; i++) d.append('[');
            for (int i = 0; i < 200; i++) d.append(']');
            MiniJson.parse(d.toString());
        });
        checkThrows("数值型房间 id（类型错误）", () -> LevelLoader.load("{\"levelId\":\"x\",\"rooms\":[{\"id\":1,\"size\":[1,1,1],\"kit\":\"k\",\"lightZone\":\"safe\"}]}", null));

        System.out.println("\n[4] DesignTokens（C2）");
        check("6 个色彩 Token 同值", () -> {
            if (!"#F0E6D2".equals(DesignTokens.COLOR_PAPER)) throw new IllegalStateException("paper");
            if (!"#1A1A1A".equals(DesignTokens.COLOR_INK)) throw new IllegalStateException("ink");
            if (!"#8B1E1E".equals(DesignTokens.COLOR_BLOOD)) throw new IllegalStateException("blood");
            if (!"#5C8C6E".equals(DesignTokens.COLOR_MOLD)) throw new IllegalStateException("mold");
            if (!"#D8CFBB".equals(DesignTokens.COLOR_BONE)) throw new IllegalStateException("bone");
            if (!"#0E0D0C".equals(DesignTokens.COLOR_SOOT)) throw new IllegalStateException("soot");
        });

        System.out.println("\n═══ 结果：通过 " + passed + " · 失败 " + failed + " ═══");
        if (failed > 0) { for (String f : failures) System.out.println("  - " + f); System.exit(1); }
    }

    static String read(String p) throws Exception {
        FileInputStream in = new FileInputStream(p);
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        byte[] buf = new byte[8192]; int n;
        while ((n = in.read(buf)) > 0) b.write(buf, 0, n);
        in.close();
        return new String(b.toByteArray(), "UTF-8");
    }
}
