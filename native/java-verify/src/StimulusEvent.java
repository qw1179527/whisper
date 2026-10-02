package com.whisper.probe;

/** 契约镜像：对应 StimulusEvent.cs。数值必须来自配置表，代码内不得硬编码。 */
public final class StimulusEvent {
    public final String sourceKey;
    public final float intensity;
    public final float radiusMeters;
    public final boolean globalBroadcast;
    public final float x, z;
    public final long tick;

    public StimulusEvent(String sourceKey, float intensity, float radiusMeters, boolean globalBroadcast, float x, float z, long tick) {
        this.sourceKey = sourceKey; this.intensity = intensity; this.radiusMeters = radiusMeters;
        this.globalBroadcast = globalBroadcast; this.x = x; this.z = z; this.tick = tick;
    }
}
