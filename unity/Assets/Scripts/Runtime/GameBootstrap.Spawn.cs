using UnityEngine;
using UnityEngine.UI;
using Whisper.Core.Contracts;
using Whisper.Gameplay.Progression;   // Progression / Shop / TaskSystem（等级-商店-任务，恐鬼症对齐）
using Whisper.Gameplay.Objectives;    // ObjectiveSystem（**局内任务**：合同日志里的可选目标，与每日任务是两套）
using Whisper.Net;
using Whisper.Net.Direct;   // LanSession / LanAddress / RoomCode（零信令直连层）
using Whisper.Audio;
using Whisper.Backend;
using Whisper.Core;
using Whisper.Gameplay.Config;
using Whisper.Gameplay.Level;

namespace Whisper.Runtime
{
    /// <summary>
    /// 出生点与相机摆放（`GameBootstrap` 的 partial 拆分）。
    ///
    /// 【为什么拆】出生逻辑加进来后主文件涨到 613 行，触发规模纪律（gate-code C5，>600 判红）。
    /// 这三件事是一个内聚整体：**选出生格 → 决定朝向 → 摆相机**，与"装配场景/怪物/HUD"无关。
    ///
    /// ## 出生朝向的两轮真机修正（都别退回去）
    /// ① 最初 `PlayerMotion` 的初始 Yaw 是 0°（+Z），会把 `PlaceCamera` 算好的朝向**静默覆盖** →
    ///    玩家睁眼看到 +Z 方向 0.3m 的墙，整屏单色（`pixel-region-audit` 判 empty）。
    ///    修法：朝向必须落在 `PlayerMotion` 上，两边用**同一个角**。
    /// ② 后来朝向取"朝最近的门口"，但 `entrance_safe` 的门在 **3m 窄边**上 →
    ///    朝门 = 朝墙（真机 0.1.20 出生画面：`朝向 98°`、右侧 2/3 被约 1m 外的灰墙占满）。
    ///    修法：改用 `LevelGeometry.SuggestSpawn` 的**最长视线方向**，纵深至少 max(SizeX,SizeZ)。
    /// </summary>
    public sealed partial class GameBootstrap
    {        /// <summary>解析出生点：先试入口房间，失败则在整张图上找空可走格。</summary>
        Spawn ResolveSpawn(Room start, System.Text.StringBuilder lines)
        {
            var geo = _levelBuilder.Geometry;

            // 【2026-10-04 真机试玩修正 · 出生朝向对着墙】原实现：房间中心 + **朝向最近的门口**。
            // 真机实测（0.1.20）出生画面：`朝向 98°` 时视野右侧 2/3 被一面约 1m 外的灰墙占满 ——
            // 因为 `entrance_safe` 是 4m×3m，门开在 **3m 的窄边（east）** 上，
            // "朝门"就等于朝那面 1.5m 外的墙。
            // 现在用 `LevelGeometry.SuggestSpawn`：房间几何中心 + **最长视线方向**（长边），
            // 至少保证有 max(SizeX,SizeZ) 米的纵深；同时它返回该朝向的 yaw，供玩家控制器使用。
            if (start != null && geo.SuggestSpawn(start, out float sx, out float sz, out float suggestYaw))
            {
                lines.AppendLine($"出生点：房间 {start.Id} → 世界 ({sx:0.0}, {sz:0.0}) · 朝向 {suggestYaw:0}°（最长视线方向）");
                return new Spawn(true, sx, sz, suggestYaw);
            }
            // 兜底：SuggestSpawn 失败时退回"中心附近找空格"，朝向保持 +X
            if (start != null && geo.TryFindFreeCell(start.CenterX, start.CenterZ, out sx, out sz))
            {
                lines.AppendLine($"出生点（兜底）：房间 {start.Id} → 世界 ({sx:0.0}, {sz:0.0})");
                return new Spawn(true, sx, sz);
            }
            // 再兜底：别把玩家丢在 (0,0)——那里常被家具占据（本项目 ward_03 中心就是病床，踩过这个坑）
            if (geo.TryFindFreeCell(0f, 0f, out sx, out sz))
            {
                lines.AppendLine($"⚠ 入口房间无空位，玩家改放 ({sx:0.0}, {sz:0.0})");
                return new Spawn(true, sx, sz);
            }
            lines.AppendLine("⚠ 整张图都找不到空可走格，玩家控制不启用");
            return new Spawn(false, 0f, 0f);
        }

        /// <summary>
        /// 相机摆放（真机实测修正）：入口房间只有 4m×3m，房间中心附近没有"倒退 2.5m"的余量——
        /// 先前把相机放在 spawn - 2.5m，实际已落到墙外，屏幕上只有一堵贴脸的墙
        /// （截屏实测：整屏 #D8CFBB = ColorBone 安全区墙色 · 边缘密度 0.0%）。
        /// 现在：站在房间内、**朝最近的门口方向**看，门连通走廊，视角才有纵深。
        /// </summary>
        /// <returns>本次朝向的 Yaw 角（度）：0° = +Z，90° = +X。玩家控制器必须用同一个角，
        /// 否则它每帧写回相机会把这里算好的朝向覆盖成默认 0°（真机实测过：开局整屏单色）。</returns>
        float PlaceCamera(Room start, Spawn spawn, System.Text.StringBuilder lines)
        {
            if (_camera == null) return 0f;
            float dirX = 1f, dirZ = 0f;
            var door = start != null && start.Doors.Count > 0 ? start.Doors[0] : null;
            if (door != null)
            {
                door.ToWorld(start, out float dx, out float dz);
                float vx = dx - spawn.X, vz = dz - spawn.Z;
                float len = Mathf.Sqrt(vx * vx + vz * vz);
                if (len > 0.05f) { dirX = vx / len; dirZ = vz / len; }
            }
            // Unity 的 Yaw：0° 指向 +Z，+90° 指向 +X → yaw = atan2(x, z)
            float yawDeg = Mathf.Atan2(dirX, dirZ) * Mathf.Rad2Deg;
            lines.AppendLine($"相机朝向：{(door != null ? $"门 {door.Id}（{door.Wall}）" : "（房间无门）")}"
                + $"→ 方向 ({dirX:0.00}, {dirZ:0.00}) · Yaw {yawDeg:0}°");
            // 离中心留一点内缩，避免正好卡在中心家具里；眼高 1.7m
            float back = start != null ? Mathf.Min(0.8f, Mathf.Min(start.SizeX, start.SizeZ) * 0.25f) : 0.8f;
            float camX = spawn.X - dirX * back;
            float camZ = spawn.Z - dirZ * back;

            // 【终验·真机试玩第二次发现】内缩会把相机推到**贴着墙**，视野被门洞两侧的墙掐住，
            // 于是开局仍是一大片平色面（真机判据：3 色 / 边缘 0.087%）。定量事实：
            // 出生点 (2.3,1.8) 在 entrance_safe（x 0~4 · z 0~3），朝门口方向约 +X，
            // 而 +X 方向其实**能走 14.9m**（门口以东就是长走廊）——相机却退到了 x≈1.5，离墙太近。
            // 修法：① 内缩后**夹回房间内**并留出余量（不许贴墙/穿墙）
            //       ② 给"门洞通行"留出足够余量：门宽通常 1.5~2m，玩家半径 0.34m，
            //          相机离门中心的轴线偏移越小越好 → 这里只保证不贴墙，朝向已对准门口。
            if (start != null)
            {
                const float margin = 0.45f;   // 离墙余量：> 玩家半径 0.34m，避免贴脸
                camX = Mathf.Clamp(camX, start.MinX + margin, start.MaxX - margin);
                camZ = Mathf.Clamp(camZ, start.MinZ + margin, start.MaxZ - margin);
            }
            lines.AppendLine($"相机位置：({camX:0.00}, {camZ:0.00}) · 内缩 {back:0.00}m · 房间 x[{start?.MinX}~{start?.MaxX}] z[{start?.MinZ}~{start?.MaxZ}]");

            _camera.transform.position = new Vector3(camX, PlayerController.EyeHeightM, camZ);
            // 俯角 -0.05（≈ -2.9°）：原来 -0.12（≈ -6.9°）偏俯，叠加"相机被摆在后下方"后
            // 真机开局实测 `俯仰 -31°`，看起来像一进游戏就在看地板。轻微下视即可保留"走廊纵深"。
            _camera.transform.rotation = Quaternion.LookRotation(new Vector3(dirX, -0.05f, dirZ));
            return yawDeg;
        }

    }
}
