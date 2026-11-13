using TinyTumble.Core.Logging;
using UnityEngine;

namespace TinyTumble.Infrastructure.Logging
{
    /// <summary>
    /// Лог в консоль Unity и Player.log. Тег идёт отдельным аргументом logger'а, без склейки строк на нашей стороне.
    /// </summary>
    public sealed class UnityGameLog : IGameLog
    {
        public void Info(string tag, string message)
        {
            Debug.unityLogger.Log(LogType.Log, tag, message);
        }

        public void Warning(string tag, string message)
        {
            Debug.unityLogger.Log(LogType.Warning, tag, message);
        }

        public void Error(string tag, string message)
        {
            Debug.unityLogger.Log(LogType.Error, tag, message);
        }
    }
}
