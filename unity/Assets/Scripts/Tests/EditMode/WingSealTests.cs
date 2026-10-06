using NUnit.Framework;
using Whisper.Gameplay.Level;

namespace Whisper.Tests.EditMode
{
    /// <summary>
    /// **分翼封锁**机制测试（官方 Sunny Meadows 机制）。
    ///
    /// 官方原文（`docs/reference-official/04-…§7.2`）：
    /// &gt; **猎杀时所在分翼封锁**，极难躲藏；房间高度相似，**极易迷路**
    ///
    /// 这些断言存在的理由：这条机制**只有"封对门"才成立**。
    /// 封错（少封一间房的门）⇒ 玩家从缝里跑出去 ⇒ 机制等于没有；
    /// 封错（封了别的翼）⇒ 玩家被凭空困住 ⇒ 比没有更糟。
    /// 所以两侧都要有断言，不能只测"有没有封"。
    /// </summary>
    public class WingSealTests
    {
        /// <summary>
        /// 三间房、两个翼的紧凑夹具：
        ///   ward_a / ward_b 属 `ward`；morgue 属 `morgue`。彼此贴面共墙。
        /// </summary>
        static LevelData BuildLevel()
        {
            var level = new LevelData { LevelId = "wing_fixture" };
            AddRoom(level, "ward_a", 0f, 0f, 4f, 3f, "ward");
            AddRoom(level, "ward_b", 4f, 0f, 4f, 3f, "ward");
            AddRoom(level, "morgue", 8f, 0f, 4f, 3f, "morgue");
            return level;
        }

        static void AddRoom(LevelData level, string id, float x, float z, float sx, float sz, string wing)
        {
            var r = new Room
            {
                Id = id,
                PosX = x, PosZ = z,
                SizeX = sx, SizeY = 3f, SizeZ = sz,
                Floor = 0,
                Kit = "k",
                LightZone = "pressure",
                Wing = wing,
            };
            // 每间房两扇门 ⇒ 用来验证"封的是该翼全部房间的门"
            r.Doors.Add(new Door { Id = "d_south", Wall = "south", WidthM = 1f, OffsetM = 1f });
            r.Doors.Add(new Door { Id = "d_north", Wall = "north", WidthM = 1f, OffsetM = 1f });
            level.Rooms.Add(r);
        }

        [Test]
        public void 全图翼清单来自房间数据()
        {
            var w = new WingSystem(BuildLevel());
            Assert.AreEqual(2, w.AllWings.Count, "应识别出 2 个翼");
            Assert.Contains("ward", w.AllWings);
            Assert.Contains("morgue", w.AllWings);
        }

        [Test]
        public void 玩家所在翼按位置判定()
        {
            var w = new WingSystem(BuildLevel());
            w.UpdatePlayerWing(1f, 1f);
            Assert.AreEqual("ward", w.PlayerWing, "在 ward_a 内 → ward");
            w.UpdatePlayerWing(9f, 1f);
            Assert.AreEqual("morgue", w.PlayerWing, "在 morgue 内 → morgue");
        }

        [Test]
        public void 站在房间外时按最近房间归属而不是判成无翼()
        {
            // 【为什么要这条】玩家站在门洞/墙缝时 `ContainsPoint` 全不命中。
            // 若此时把玩家判成"无翼"，封锁就会在那**最需要生效的一刻**失效。
            var w = new WingSystem(BuildLevel());
            w.UpdatePlayerWing(12f, 1f);   // 三间房之外（x>12）
            Assert.AreEqual("morgue", w.PlayerWing, "最近的房间是 morgue → 应归属 morgue，而不是 null");
            Assert.IsNotNull(w.PlayerWing);
        }

        [Test]
        public void 封锁只封该翼的房间门且数量正确()
        {
            var w = new WingSystem(BuildLevel());
            int n = w.SealWing("ward");
            // ward 两间房 × 各 2 扇门 = 4
            Assert.AreEqual(4, n, "ward 翼有 2 间房、每间 2 扇门 ⇒ 应封 4 扇");
            Assert.AreEqual("ward", w.SealedWing);
            Assert.IsTrue(w.IsSealed);
            CollectionAssert.Contains(w.SealedDoorKeys, "ward_a/d_south");
            CollectionAssert.Contains(w.SealedDoorKeys, "ward_b/d_north");
        }

        [Test]
        public void 封锁不会封到别的翼()
        {
            // 【关键反向断言】封错翼比不封更糟 —— 玩家会被凭空困住。
            var w = new WingSystem(BuildLevel());
            w.SealWing("ward");
            foreach (var k in w.SealedDoorKeys)
                Assert.IsFalse(k.StartsWith("morgue/"), $"封锁清单里不该出现别的翼的门：{k}");
        }

        [Test]
        public void 门键口径与几何层一致()
        {
            // 口径不一致 ⇒ SetDoorOpen 找不到门 ⇒ 静默不生效（最难查的一类）
            var w = new WingSystem(BuildLevel());
            w.SealWing("morgue");
            Assert.AreEqual("morgue/d_south", LevelGeometry.DoorKey("morgue", "d_south"));
            CollectionAssert.Contains(w.SealedDoorKeys, LevelGeometry.DoorKey("morgue", "d_south"));
        }

        [Test]
        public void 解除封锁会清空清单()
        {
            var w = new WingSystem(BuildLevel());
            w.SealWing("ward");
            Assert.Greater(w.SealedDoorKeys.Count, 0);
            int n = w.Unseal();
            Assert.AreEqual(4, n, "解除应返回被解除的门数");
            Assert.AreEqual(0, w.SealedDoorKeys.Count);
            Assert.IsFalse(w.IsSealed);
            Assert.IsNull(w.SealedWing);
        }

        [Test]
        public void 重复封锁同一翼不会累积重复门键()
        {
            var w = new WingSystem(BuildLevel());
            w.SealWing("ward");
            w.SealWing("ward");
            Assert.AreEqual(4, w.SealedDoorKeys.Count, "重复封锁必须幂等，否则清单会无限增长");
        }

        [Test]
        public void 封锁不存在的翼是空操作()
        {
            var w = new WingSystem(BuildLevel());
            Assert.AreEqual(0, w.SealWing("no_such_wing"));
            Assert.IsFalse(w.IsSealed, "封不存在的翼不该进入'已封锁'状态（否则玩家被永久困住）");
        }
    }
}
