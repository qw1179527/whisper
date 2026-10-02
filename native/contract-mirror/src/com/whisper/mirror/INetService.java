package com.whisper.mirror;

/** 契约镜像：与 unity/Assets/Scripts/Core/Contracts/INetService.cs 逐成员对应（V9 §13.2）。 */
public interface INetService {
    boolean isHost();
    boolean isConnected();
    int tickRate();                                   // 固定 60（V9 §13.4）
    void connect(String roomCode, String authToken);
    void disconnect();
    void sendVoiceStimulus(StimulusEvent stimulus);    // 声纹事件：瞬时 RPC
    void onRoomClosed(String reason);                  // 事件：RoomClosed
    void onHostMigration(boolean started);             // 事件：HostMigration

    // ── 只读状态面（V9 §13.4 同步对象 ①③④）；Java 无属性，用访问器表达 ──
    MatchState.Phase phase();                          // 同步对象④
    MatchState.NetworkSnapshot snapshot();             // ①③④ + 证据计数 + 世界哈希
    void onPhaseChanged(MatchState.Phase p);           // 阶段变化
    void onPropChanged(MatchState.PropState p);        // 门/道具变化
    void onPlayerUpdated(MatchState.PlayerSnapshot p); // 玩家位姿更新
}
