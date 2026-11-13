using System;
using MessagePipe;
using TinyTumble.Core.Debugging;
using TinyTumble.Core.Messages;
using VContainer.Unity;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Команды консоли, доступные только во время матча.
    /// </summary>
    public sealed class MatchDebugCommands : IStartable, IDisposable
    {
        public const string EndCommand = "match.end";

        private readonly IDebugConsole _console;
        private readonly IPublisher<MatchEnded> _matchEnded;
        private IDisposable _endRegistration;

        public MatchDebugCommands(IDebugConsole console, IPublisher<MatchEnded> matchEnded)
        {
            _console = console;
            _matchEnded = matchEnded;
        }

        public void Start()
        {
            _endRegistration = _console.Register(EndCommand, "завершить матч без победителя", OnEnd);
        }

        public void Dispose()
        {
            if (_endRegistration != null)
            {
                _endRegistration.Dispose();
                _endRegistration = null;
            }
        }

        private void OnEnd(string arguments)
        {
            _matchEnded.Publish(new MatchEnded(MatchEnded.NoWinner));
        }
    }
}
