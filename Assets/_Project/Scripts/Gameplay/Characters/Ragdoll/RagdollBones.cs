namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Физические кости персонажа. Набор и порядок одинаковы для всех моделей: по ним строятся позы,
    /// и в этом порядке кости уходят в сетевой снимок. Родитель всегда раньше ребёнка.
    /// </summary>
    public static class RagdollBones
    {
        public const int Hips = 0;
        public const int Chest = 1;
        public const int Head = 2;
        public const int LeftUpperArm = 3;
        public const int LeftLowerArm = 4;
        public const int RightUpperArm = 5;
        public const int RightLowerArm = 6;
        public const int LeftUpperLeg = 7;
        public const int LeftLowerLeg = 8;
        public const int RightUpperLeg = 9;
        public const int RightLowerLeg = 10;

        public const int Count = 11;

        private static readonly int[] s_parents = { -1, Hips, Chest, Chest, LeftUpperArm, Chest, RightUpperArm, Hips, LeftUpperLeg, Hips, RightUpperLeg };

        private static readonly string[] s_names =
        {
            "Hips", "Chest", "Head", "LeftUpperArm", "LeftLowerArm", "RightUpperArm", "RightLowerArm",
            "LeftUpperLeg", "LeftLowerLeg", "RightUpperLeg", "RightLowerLeg",
        };

        /// <summary>Кость, к которой крепится сустав; у таза — -1.</summary>
        public static int Parent(int bone)
        {
            return s_parents[bone];
        }

        public static string Name(int bone)
        {
            return s_names[bone];
        }

        /// <summary>Кисть на конце этой кости: ей бьют толчком.</summary>
        public static bool IsHand(int bone)
        {
            return bone == LeftLowerArm || bone == RightLowerArm;
        }
    }
}
