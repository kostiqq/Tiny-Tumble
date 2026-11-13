using System;
using System.Collections.Generic;
using System.Text;
using TinyTumble.Core.Debugging;
using TinyTumble.Core.Logging;
using UnityEngine;

namespace TinyTumble.Infrastructure.Debugging
{
    /// <summary>
    /// Реестр команд и история сообщений для debug-консоли. Отрисовка — в <see cref="DebugConsoleView"/>.
    /// </summary>
    public sealed class DebugConsole : IDebugConsole, IDisposable
    {
        private const string LogTag = "Console";
        private const int HistoryCapacity = 200;

        private sealed class Command
        {
            public string Description;
            public Action<string> Handler;
        }

        private sealed class Registration : IDisposable
        {
            private DebugConsole _owner;
            private readonly string _name;
            private readonly Command _command;

            public Registration(DebugConsole owner, string name, Command command)
            {
                _owner = owner;
                _name = name;
                _command = command;
            }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                // Снимаем только свою регистрацию: команду могли перерегистрировать в новой сцене.
                if (_owner._commands.TryGetValue(_name, out Command current) && current == _command)
                {
                    _owner._commands.Remove(_name);
                }

                _owner = null;
            }
        }

        private readonly IGameLog _log;
        private readonly Dictionary<string, Command> _commands = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _history = new Queue<string>(HistoryCapacity);
        private readonly StringBuilder _builder = new StringBuilder();

        public DebugConsole(IGameLog log)
        {
            _log = log;
            Application.logMessageReceived += OnLogMessageReceived;
            Register("help", "список команд", OnHelp);
            Register("clear", "очистить историю", OnClear);
        }

        public event Action HistoryChanged;

        public IReadOnlyCollection<string> History => _history;

        public IDisposable Register(string command, string description, Action<string> handler)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Command name is empty.", nameof(command));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var entry = new Command { Description = description, Handler = handler };
            _commands[command] = entry;
            return new Registration(this, command, entry);
        }

        public bool Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            line = line.Trim();
            int space = line.IndexOf(' ');
            string name = space < 0 ? line : line.Substring(0, space);
            string arguments = space < 0 ? string.Empty : line.Substring(space + 1).Trim();

            AddHistory("> " + line);
            if (!_commands.TryGetValue(name, out Command command))
            {
                _log.Warning(LogTag, $"Unknown command '{name}'. Type 'help'.");
                return false;
            }

            try
            {
                command.Handler(arguments);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            return true;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            _commands.Clear();
        }

        private void OnHelp(string arguments)
        {
            _builder.Clear();
            _builder.Append("Commands:");
            foreach (KeyValuePair<string, Command> pair in _commands)
            {
                _builder.AppendLine().Append("  ").Append(pair.Key).Append(" — ").Append(pair.Value.Description);
            }

            AddHistory(_builder.ToString());
        }

        private void OnClear(string arguments)
        {
            _history.Clear();
            HistoryChanged?.Invoke();
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            AddHistory(type == LogType.Log ? condition : type + ": " + condition);
        }

        private void AddHistory(string line)
        {
            while (_history.Count >= HistoryCapacity)
            {
                _history.Dequeue();
            }

            _history.Enqueue(line);
            HistoryChanged?.Invoke();
        }
    }
}
