using System;

namespace TinyTumble.Core.Scenes
{
    /// <summary>
    /// Экран загрузки со счётчиком удержаний: виден, пока жив хотя бы один холд.
    /// Так загрузчик сцен и матч (который ещё грузит арену) не спорят, кто его прячет.
    /// </summary>
    public interface ILoadingScreen
    {
        bool IsVisible { get; }

        /// <summary>Показывает экран до вызова Dispose у результата. Повторный Dispose безопасен.</summary>
        IDisposable Hold();
    }
}
