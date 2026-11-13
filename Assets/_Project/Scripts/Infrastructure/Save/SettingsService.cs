using TinyTumble.Core.Logging;
using TinyTumble.Core.Save;
using UnityEngine;
using VContainer.Unity;

namespace TinyTumble.Infrastructure.Save
{
    /// <summary>
    /// Загружает настройки при старте корня и применяет их к движку, поэтому они действуют при Play из любой сцены.
    /// </summary>
    public sealed class SettingsService : ISettingsService, IInitializable
    {
        public const string SaveKey = "settings";
        private const string LogTag = "Settings";

        private readonly ISaveService _saveService;
        private readonly IGameLog _log;

        public SettingsService(ISaveService saveService, IGameLog log)
        {
            _saveService = saveService;
            _log = log;
            Current = new GameSettings();
        }

        public GameSettings Current { get; private set; }

        public void Initialize()
        {
            if (_saveService.TryLoad(SaveKey, out GameSettings loaded))
            {
                Current = loaded;
            }
            else
            {
                _log.Info(LogTag, "No saved settings, using defaults.");
            }

            ApplyToEngine(Current);
        }

        public void Apply(GameSettings settings)
        {
            Current = settings.Clone();
            ApplyToEngine(Current);
            _saveService.Save(SaveKey, Current);
        }

        private static void ApplyToEngine(GameSettings settings)
        {
            AudioListener.volume = Mathf.Clamp01(settings.MasterVolume);
            QualitySettings.vSyncCount = settings.VSync ? 1 : 0;
#if !UNITY_EDITOR
            Screen.fullScreen = settings.Fullscreen;
#endif
        }
    }
}
