package com.whisper.probe;

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
}
