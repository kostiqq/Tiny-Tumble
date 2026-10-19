namespace TinyTumble.Core.Logging
{
    /// <summary>
    /// Лог с тегом подсистемы. Тег — константа вызывающего класса, чтобы фильтровать консоль без лишних строк.
    /// </summary>
    public interface IGameLog
    {
        void Info(string tag, string message);

        void Warning(string tag, string message);

        void Error(string tag, string message);
    }
}
