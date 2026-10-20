namespace TinyTumble.Core.Flow
{
    /// <summary>
    /// Состояние верхнего уровня. Каждому состоянию соответствует ровно одна сцена из Build Settings.
    /// </summary>
    public enum GameState
    {
        None = 0,
        Boot = 1,
        MainMenu = 2,
        Lobby = 3,
        Match = 4,
        Results = 5,
    }

    /// <summary>
    /// Имена сцен. Совпадают с именами файлов в Assets/Scenes и порядком в Build Settings.
    /// </summary>
    public static class SceneNames
    {
        public const string Boot = "Boot";
        public const string MainMenu = "MainMenu";
        public const string Lobby = "Lobby";
        public const string Match = "Match";
        public const string Results = "Results";
    }

    public static class GameStateExtensions
    {
        public static string ToSceneName(this GameState state)
        {
            switch (state)
            {
                case GameState.Boot:
                    return SceneNames.Boot;
                case GameState.MainMenu:
                    return SceneNames.MainMenu;
                case GameState.Lobby:
                    return SceneNames.Lobby;
                case GameState.Match:
                    return SceneNames.Match;
                case GameState.Results:
                    return SceneNames.Results;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Обратное преобразование: по имени активной сцены. Неизвестная сцена (например, сцена тест-раннера) даёт None.
        /// </summary>
        public static GameState FromSceneName(string sceneName)
        {
            switch (sceneName)
            {
                case SceneNames.Boot:
                    return GameState.Boot;
                case SceneNames.MainMenu:
                    return GameState.MainMenu;
                case SceneNames.Lobby:
                    return GameState.Lobby;
                case SceneNames.Match:
                    return GameState.Match;
                case SceneNames.Results:
                    return GameState.Results;
                default:
                    return GameState.None;
            }
        }
    }
}
