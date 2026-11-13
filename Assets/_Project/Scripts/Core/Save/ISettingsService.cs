namespace TinyTumble.Core.Save
{
    public interface ISettingsService
    {
        /// <summary>Применённые настройки. Не меняйте объект напрямую: передайте изменённую копию в Apply.</summary>
        GameSettings Current { get; }

        /// <summary>Применяет настройки к движку и сохраняет их.</summary>
        void Apply(GameSettings settings);
    }
}
