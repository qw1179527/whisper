package com.whisper.probe;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Level DSL 加载与校验（Java 镜像）：与 unity/.../LevelLoader.cs **同一套规则**。
 * 与 C# 侧的关键一致性约束：
 *   · `_` 前缀键为注释，解析时忽略；
 *   · 结构/类型错误统一以 IllegalArgumentException 抛出（Bootstrap 侧兜底 catch）；
 *   · kit 必须在 asset-manifest 中；lightZone ∈ {safe,pressure,high-risk}；墙 ∈ {north,south,east,west}；
 *   · door.offset 缺省 = 0；events 数 2~3；extraction 双点存在且不同。
 */
public final class LevelLoader {
    public static final Set<String> WALLS = new HashSet<String>(java.util.Arrays.asList("north", "south", "east", "west"));
    public static final Set<String> ZONES = new HashSet<String>(java.util.Arrays.asList("safe", "pressure", "high-risk"));
    public static final Set<String> EVENT_TYPES = new HashSet<String>(java.util.Arrays.asList("blackout", "doorlock", "static", "mirror", "overload", "laugh"));

    public static final class Level {
        public String levelId;
        public final List<String> roomIds = new ArrayList<String>();
        public int roomCount, corridorCount, eventCount, evidenceCount;
        public int safeCount, pressureCount, highRiskCount;
        public String extractionStandard, extractionDeep;
    }

    public static Level load(String json, Set<String> knownKits) {
        Map<String, Object> root = MiniJson.asMap(MiniJson.parse(json));
        Level lv = new Level();
        Object idv = MiniJson.get(root, "levelId");
        if (!(idv instanceof String) || ((String) idv).trim().isEmpty()) throw new IllegalArgumentException("levelId 必须是非空字符串");
        lv.levelId = (String) idv;

        Object roomsV = MiniJson.get(root, "rooms");
        if (!(roomsV instanceof List) || ((List<?>) roomsV).isEmpty()) throw new IllegalArgumentException("rooms 必须是非空数组");
        for (Object rv : MiniJson.asList(roomsV)) {
            Map<String, Object> r = MiniJson.asMap(rv);
            String rid = MiniJson.asString(MiniJson.get(r, "id"));
            if (lv.roomIds.contains(rid)) throw new IllegalArgumentException("房间 id 重复：" + rid);
            lv.roomIds.add(rid);
            lv.roomCount++;

            // D1：布局字段
            List<Object> posv = MiniJson.asList(MiniJson.get(r, "pos"));
            if (posv.size() != 2) throw new IllegalArgumentException("房间 " + rid + " 的 pos 必须是 [x,z]");
            MiniJson.asFloat(posv.get(0)); MiniJson.asFloat(posv.get(1));
            if (MiniJson.get(r, "rotY") != null) MiniJson.asFloat(MiniJson.get(r, "rotY"));
            if (MiniJson.get(r, "floor") != null) MiniJson.asFloat(MiniJson.get(r, "floor"));
            List<Object> size = MiniJson.asList(MiniJson.get(r, "size"));
            if (size.size() != 3) throw new IllegalArgumentException("房间 " + rid + " 的 size 必须是 [宽,高,深]");
            for (Object s : size) if (MiniJson.asFloat(s) <= 0) throw new IllegalArgumentException("房间 " + rid + " 的 size 必须为正数");

            String kit = MiniJson.asString(MiniJson.get(r, "kit"));
            if (knownKits != null && !knownKits.contains(kit)) throw new IllegalArgumentException("房间 " + rid + " 的 kit `" + kit + "` 不在 asset-manifest 中");

            String zone = MiniJson.asString(MiniJson.get(r, "lightZone"));
            if (!ZONES.contains(zone)) throw new IllegalArgumentException("房间 " + rid + " 的 lightZone 非法：" + zone);
            if ("safe".equals(zone)) lv.safeCount++;
            else if ("pressure".equals(zone)) lv.pressureCount++;
            else lv.highRiskCount++;

            Object ev = MiniJson.get(r, "evidencePoint");
            if (Boolean.TRUE.equals(ev)) lv.evidenceCount++;

            java.util.Set<String> doorIds = new java.util.HashSet<String>();
            Object doorsV = MiniJson.get(r, "doors");
            if (doorsV != null) {
                for (Object dv : MiniJson.asList(doorsV)) {
                    Map<String, Object> d = MiniJson.asMap(dv);
                    String wall = MiniJson.asString(MiniJson.get(d, "wall"));
                    if (!WALLS.contains(wall)) throw new IllegalArgumentException("房间 " + rid + " 的门 wall 非法：" + wall);
                    String did = MiniJson.asString(MiniJson.get(d, "id"));
                    if (did.trim().isEmpty()) throw new IllegalArgumentException("房间 " + rid + " 的门缺 id（D1）");
                    if (!doorIds.add(rid + "/" + did)) throw new IllegalArgumentException("房间 " + rid + " 门 id 重复：" + did);
                    Object off = MiniJson.get(d, "offset");
                    float o = off == null ? 0f : MiniJson.asFloat(off);   // 缺省 = 0（与 C# 一致）
                    if (o < 0f || o > 1f) throw new IllegalArgumentException("房间 " + rid + " 的门 offset 必须在 0..1：" + o);
                }
            }
            Object propsV = MiniJson.get(r, "props");
            if (propsV != null) {
                for (Object pv : MiniJson.asList(propsV)) {
                    Map<String, Object> p = MiniJson.asMap(pv);
                    String pk = MiniJson.asString(MiniJson.get(p, "kit"));
                    if (knownKits != null && !knownKits.contains(pk)) throw new IllegalArgumentException("房间 " + rid + " 的道具 kit `" + pk + "` 不在 asset-manifest 中");
                }
            }
        }

        Object corrV = MiniJson.get(root, "corridors");
        if (corrV != null) {
            for (Object cv : MiniJson.asList(corrV)) {
                Map<String, Object> c = MiniJson.asMap(cv);
                String from = MiniJson.asString(MiniJson.get(c, "from"));
                String to = MiniJson.asString(MiniJson.get(c, "to"));
                if (!lv.roomIds.contains(from)) throw new IllegalArgumentException("走廊起点不存在：" + from);
                if (!lv.roomIds.contains(to)) throw new IllegalArgumentException("走廊终点不存在：" + to);
                if (MiniJson.get(c, "doorA") == null || MiniJson.get(c, "doorB") == null)
                    throw new IllegalArgumentException("走廊 " + from + "→" + to + " 必须给 doorA/doorB（D1）");
                if (MiniJson.asFloat(MiniJson.get(c, "width")) <= 0) throw new IllegalArgumentException("走廊宽度必须为正");
                lv.corridorCount++;
            }
        }

        Object eventsV = MiniJson.get(root, "events");
        if (eventsV != null) {
            for (Object ev : MiniJson.asList(eventsV)) {
                Map<String, Object> e = MiniJson.asMap(ev);
                String t = MiniJson.asString(MiniJson.get(e, "type"));
                boolean builtin = EVENT_TYPES.contains(t);
                boolean ext = t.startsWith("x-") || t.startsWith("ext-") || t.startsWith("ns:");
                if (!builtin && !ext) throw new IllegalArgumentException("事件类型非法：" + t + "（内建 6 型或 x-/ext-/ns: 扩展）");
                Object cp = MiniJson.get(e, "counterplay");
                if (!(cp instanceof String) || ((String) cp).trim().isEmpty())
                    throw new IllegalArgumentException("事件 " + t + " 缺 counterplay（V9 §30.2）");
                if (MiniJson.get(e, "minute") == null) throw new IllegalArgumentException("事件 " + t + " 缺 minute");
                MiniJson.asFloat(MiniJson.get(e, "minute"));
                if (MiniJson.asFloat(MiniJson.get(e, "durationSec")) <= 0) throw new IllegalArgumentException("事件 " + t + " 的 durationSec 必须为正");
                lv.eventCount++;
            }
        }
        if (lv.eventCount > 0 && (lv.eventCount < 2 || lv.eventCount > 3)) throw new IllegalArgumentException("动态事件数量应为 2~3 个，当前 " + lv.eventCount);

        Object exV = MiniJson.get(root, "extraction");
        if (exV != null) {
            Map<String, Object> ex = MiniJson.asMap(exV);
            lv.extractionStandard = MiniJson.asString(MiniJson.get(ex, "standard"));
            lv.extractionDeep = MiniJson.asString(MiniJson.get(ex, "deep"));
            if (!lv.roomIds.contains(lv.extractionStandard)) throw new IllegalArgumentException("撤离标准点不存在：" + lv.extractionStandard);
            if (!lv.roomIds.contains(lv.extractionDeep)) throw new IllegalArgumentException("撤离深处点不存在：" + lv.extractionDeep);
            if (lv.extractionStandard.equals(lv.extractionDeep)) throw new IllegalArgumentException("撤离标准点与深处点不能是同一房间");
        }
        return lv;
    }
}
