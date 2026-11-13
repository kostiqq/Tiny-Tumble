namespace TinyTumble.Core.Assets
{
    /// <summary>
    /// Адреса и метки Addressables, на которые ссылается код. Контент без адреса в коде ищется по меткам.
    /// </summary>
    public static class AddressKeys
    {
        public const string TestArena = "Arena_Test";
        public const string Character = "Character_Capsule";
        public const string ThrowableItem = "Item_Block";

        public const string SessionState = "Net_Session";

        public const string ArenaLabel = "arena";
        public const string CharacterLabel = "character";
        public const string ItemLabel = "item";

        /// <summary>Сетевые префабы: все ассеты с этой меткой регистрируются в NGO до старта сессии.</summary>
        public const string NetworkLabel = "network";
    }
}
