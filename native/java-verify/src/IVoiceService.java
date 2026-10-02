package com.whisper.probe;

/** 契约镜像：对应 IVoiceService.cs（V9 §13.2 / §13.5）。 */
public interface IVoiceService {
    boolean isMuted();
    float localEnergy01();                             // 采集源优先级 ①（Vivox 能量回调）
    void joinChannel(String channelName);              // 频道名 = 房间码 + 局序号
    void leaveChannel();
    void setLocalMute(String participantId, boolean muted);
    void onParticipantEnergy(String participantId, float energy01);
}
