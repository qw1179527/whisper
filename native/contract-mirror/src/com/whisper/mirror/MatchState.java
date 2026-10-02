package com.whisper.mirror;

import java.util.ArrayList;
import java.util.List;

/** 契约镜像：对应 Core/Contracts/MatchState.cs（V9 §13.4 同步对象 ①③④）。 */
public final class MatchState {
    private MatchState() { }

    /** 对局阶段（同步对象④）。 */
    public enum Phase { Lobby, Loading, Playing, Extraction, Ended }

    /** 玩家位姿快照（同步对象①：10Hz 插值结果）。 */
    public static final class PlayerSnapshot {
        public final String playerId;
        public final float x, y, z, yaw, sanity01;
        public final boolean isLocal;

        public PlayerSnapshot(String playerId, float x, float y, float z, float yaw, boolean isLocal, float sanity01) {
            this.playerId = playerId; this.x = x; this.y = y; this.z = z;
            this.yaw = yaw; this.isLocal = isLocal; this.sanity01 = sanity01;
        }
    }

    /** 道具/门状态快照（同步对象③）。 */
    public static final class PropState {
        public final String propId;
        public final boolean open, locked;
        public final float charge01;

        public PropState(String propId, boolean open, boolean locked, float charge01) {
            this.propId = propId; this.open = open; this.locked = locked; this.charge01 = charge01;
        }
    }

    /** 只读网络状态快照（玩法层经 INetService.Snapshot 读取，无需引用 Net 模块）。 */
    public static final class NetworkSnapshot {
        public final Phase phase;
        public final List<PlayerSnapshot> players;
        public final List<PropState> props;
        public final int evidenceCount;
        public final String worldHash;

        public NetworkSnapshot(Phase phase, List<PlayerSnapshot> players, List<PropState> props, int evidenceCount, String worldHash) {
            this.phase = phase;
            this.players = players != null ? players : new ArrayList<PlayerSnapshot>();
            this.props = props != null ? props : new ArrayList<PropState>();
            this.evidenceCount = evidenceCount;
            this.worldHash = worldHash;
        }
    }
}
