using System;

namespace TinyTumble.Core.Save
{
    /// <summary>
    /// Настройки игрока. Поля публичные, потому что их пишет JsonUtility; менять их стоит через ISettingsService.
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public float MasterVolume = 1f;
        public float MusicVolume = 0.8f;
        public float SfxVolume = 1f;
        public bool Fullscreen = true;
        public bool VSync = true;
        public string Language = "ru";

        public GameSettings Clone()
        {
            return (GameSettings)MemberwiseClone();
        }
    }
}
