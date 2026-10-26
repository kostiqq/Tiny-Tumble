using TinyTumble.Gameplay.Characters;
using UnityEngine;

namespace TinyTumble.Gameplay.Combat
{
    /// <summary>
    /// То, что можно схватить, нести и бросить: предмет на арене или другой игрок (раздел 4 GDD).
    /// Все методы, кроме свойств, вызываются только на хосте.
    /// </summary>
    public interface IGrabbable
    {
        ulong NetworkObjectId { get; }

        Rigidbody Body { get; }

        /// <summary>Тяжёлое замедляет несущего (<see cref="Core.Configs.CombatConfig.HeavyCarrySlowdown"/>).</summary>
        bool IsHeavy { get; }

        /// <summary>Расстояние от pivot до низа объекта: держим так, чтобы низ был над головой несущего.</summary>
        float HoldBottomOffset { get; }

        bool CanBeGrabbedBy(PlayerCharacter grabber, float now);

        void OnGrabbed(PlayerCharacter grabber, float now);

        /// <summary>Несущий отпустил объект: бросил (<paramref name="thrown"/>) или положил.</summary>
        void OnReleased(PlayerCharacter grabber, Vector3 velocity, bool thrown, float now);
    }
}
