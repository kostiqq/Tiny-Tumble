using System;

namespace TinyTumble.Core.Debugging
{
    /// <summary>
    /// Debug-консоль: команды регистрирует тот, кто умеет их выполнять, на время своей жизни.
    /// </summary>
    public interface IDebugConsole
    {
        /// <summary>
        /// Регистрирует команду. Обработчик получает строку аргументов после имени (может быть пустой).
        /// Dispose результата снимает регистрацию.
        /// </summary>
        IDisposable Register(string command, string description, Action<string> handler);

        /// <summary>Выполняет строку вида "match.end" или "имя аргументы". false, если команда не найдена.</summary>
        bool Execute(string line);
    }
}
