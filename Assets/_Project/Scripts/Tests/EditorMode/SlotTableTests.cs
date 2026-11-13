using NUnit.Framework;
using TinyTumble.Networking.Lobby;

namespace TinyTumble.Tests
{
    public sealed class SlotTableTests
    {
        private const ulong Host = 0;
        private const ulong ClientA = 1;
        private const ulong ClientB = 2;
        private const ulong HostKey = 100;
        private const ulong KeyA = 101;
        private const ulong KeyB = 102;

        [Test]
        public void Assign_FillsLowestFreeSlot()
        {
            var table = new SlotTable();

            Assert.IsTrue(table.TryAssign(HostKey, Host, 0, out int first));
            Assert.IsTrue(table.TryAssign(KeyA, ClientA, 0, out int second));

            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second);
            Assert.AreEqual(2, table.OccupiedCount);
        }

        [Test]
        public void Assign_SamePlayerTwice_KeepsOneSlot()
        {
            var table = new SlotTable();
            table.TryAssign(KeyA, ClientA, 1, out int first);

            Assert.IsTrue(table.TryAssign(KeyA, ClientA, 1, out int again));

            Assert.AreEqual(first, again);
            Assert.AreEqual(1, table.OccupiedCount);
        }

        [Test]
        public void Assign_WhenFull_Fails()
        {
            var table = new SlotTable();
            for (int i = 0; i < SlotTable.Capacity; i++)
            {
                Assert.IsTrue(table.TryAssign(KeyA, ClientA, i, out _));
            }

            Assert.IsFalse(table.TryAssign(KeyB, ClientB, 0, out int slot));
            Assert.AreEqual(-1, slot);
            Assert.AreEqual(0, table.FreeCount);
        }

        [Test]
        public void Disconnect_KeepsSlotReserved_AndItIsNotFree()
        {
            var table = new SlotTable();
            table.TryAssign(HostKey, Host, 0, out _);
            table.TryAssign(KeyA, ClientA, 0, out int slotA);
            table.SetReady(ClientA, true);

            Assert.AreEqual(KeyA, table.Disconnect(ClientA));

            SlotEntry entry = table[slotA];
            Assert.IsTrue(entry.Occupied, "The slot stays with the player while they reconnect.");
            Assert.IsFalse(entry.Connected);
            Assert.IsFalse(entry.Ready);
            Assert.AreEqual(SlotTable.NoClient, entry.ClientId);
            Assert.AreEqual(2, SlotTable.Capacity - table.FreeCount, "A reserved slot is not free for newcomers.");
            Assert.IsFalse(table.HasClient(ClientA));
            Assert.AreEqual(0, table.Disconnect(ClientA), "Second disconnect of the same client changes nothing.");
        }

        [Test]
        public void Reconnect_ReturnsTheSameSlots_UnderTheNewClientId()
        {
            var table = new SlotTable();
            table.TryAssign(HostKey, Host, 0, out _);
            table.TryAssign(KeyA, ClientA, 0, out int first);
            table.TryAssign(KeyA, ClientA, 1, out int second);
            table.Disconnect(ClientA);

            const ulong newClientId = 7;
            Assert.AreEqual(2, table.Reconnect(KeyA, newClientId));

            Assert.AreEqual(newClientId, table[first].ClientId);
            Assert.AreEqual(newClientId, table[second].ClientId);
            Assert.IsTrue(table[first].Connected);

            // Клиент после переподключения снова просит слоты для своих игроков — и получает прежние.
            Assert.IsTrue(table.TryAssign(KeyA, newClientId, 1, out int again));
            Assert.AreEqual(second, again);
        }

        [Test]
        public void RemovePlayer_FreesReservedSlots_AndReusesTheGap()
        {
            var table = new SlotTable();
            table.TryAssign(HostKey, Host, 0, out _);
            table.TryAssign(KeyA, ClientA, 0, out _);
            table.TryAssign(KeyB, ClientB, 0, out _);
            table.Disconnect(ClientA);

            Assert.AreEqual(1, table.RemovePlayer(KeyA));
            Assert.IsFalse(table.HasPlayer(KeyA));

            table.TryAssign(KeyB, ClientB, 1, out int reused);
            Assert.AreEqual(1, reused, "The first freed slot is reused.");
        }

        [Test]
        public void Ready_IsPerClient_HostIsAlwaysReady_AndReservedSlotsDoNotBlockStart()
        {
            var table = new SlotTable();
            table.TryAssign(HostKey, Host, 0, out _);
            table.TryAssign(KeyA, ClientA, 0, out _);
            table.TryAssign(KeyA, ClientA, 1, out _);
            table.TryAssign(KeyB, ClientB, 0, out _);

            Assert.IsFalse(table.AreAllReady(Host));

            table.SetReady(ClientA, true);
            Assert.IsTrue(table.IsReady(ClientA), "Ready covers every local player of the client.");
            Assert.IsFalse(table.AreAllReady(Host));

            table.Disconnect(ClientB);
            Assert.IsTrue(table.AreAllReady(Host), "A dropped player keeps the slot but does not hold the start.");

            table.ResetReady();
            Assert.IsFalse(table.IsReady(ClientA));
        }

        [Test]
        public void ForNewHost_KeepsSlots_NewHostConnected_OthersReserved()
        {
            var table = new SlotTable();
            table.TryAssign(HostKey, Host, 0, out int oldHostSlot);
            table.TryAssign(KeyA, ClientA, 0, out int slotA);
            table.TryAssign(KeyB, ClientB, 0, out int slotB);
            table.SetReady(ClientA, true);

            SlotTable migrated = table.ForNewHost(KeyA, Host);

            Assert.AreEqual(3, migrated.OccupiedCount, "Nobody loses their slot or color.");
            Assert.IsTrue(migrated[slotA].Connected);
            Assert.AreEqual(Host, migrated[slotA].ClientId);
            Assert.IsFalse(migrated[slotA].Ready);
            Assert.IsFalse(migrated[oldHostSlot].Connected, "The old host may come back as a client.");
            Assert.AreEqual(HostKey, migrated[oldHostSlot].PlayerKey);
            Assert.IsFalse(migrated[slotB].Connected);
        }

        [Test]
        public void Equality_DetectsChanges()
        {
            var a = new SlotTable();
            var b = new SlotTable();
            Assert.IsTrue(a.Equals(b));

            b.TryAssign(KeyA, ClientA, 0, out _);
            Assert.IsFalse(a.Equals(b), "NetworkVariable syncs only when the value differs.");

            a.TryAssign(KeyA, ClientA, 0, out _);
            Assert.IsTrue(a.Equals(b));

            b.SetReady(ClientA, true);
            Assert.IsFalse(a.Equals(b));

            a.SetReady(ClientA, true);
            b.Disconnect(ClientA);
            Assert.IsFalse(a.Equals(b));
        }
    }
}
