using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace TinyTumble.Infrastructure.Debugging
{
    /// <summary>
    /// IMGUI-окно debug-консоли. Открывается клавишей ` (тильда) или F1. Живёт в корневом префабе.
    /// </summary>
    public sealed class DebugConsoleView : MonoBehaviour
    {
        private const string InputControlName = "DebugConsoleInput";

        [SerializeField, Range(0.2f, 1f)] private float _heightFraction = 0.45f;
        [SerializeField, Min(8)] private int _fontSize = 16;

        private readonly StringBuilder _builder = new StringBuilder();
        private DebugConsole _console;
        private bool _isOpen;
        private bool _focusInput;
        private string _input = string.Empty;
        private string _historyText = string.Empty;
        private bool _historyDirty = true;
        private Vector2 _scroll;
        private GUIStyle _labelStyle;
        private GUIStyle _inputStyle;

        public bool IsOpen => _isOpen;

        [Inject]
        public void Construct(DebugConsole console)
        {
            _console = console;
            _console.HistoryChanged += OnHistoryChanged;
        }

        private void OnDestroy()
        {
            if (_console != null)
            {
                _console.HistoryChanged -= OnHistoryChanged;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.backquoteKey.wasPressedThisFrame || keyboard.f1Key.wasPressedThisFrame)
            {
                _isOpen = !_isOpen;
                _focusInput = _isOpen;
                _input = string.Empty;
            }
        }

        private void OnGUI()
        {
            if (!_isOpen || _console == null)
            {
                return;
            }

            EnsureStyles();

            Event current = Event.current;
            if (current.type == EventType.KeyDown)
            {
                if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
                {
                    Submit();
                    current.Use();
                }
                else if (current.keyCode == KeyCode.BackQuote || current.character == '`' || current.character == '~')
                {
                    // Сама клавиша открытия не должна попадать в поле ввода.
                    current.Use();
                }
            }

            if (_historyDirty)
            {
                RebuildHistoryText();
            }

            float height = Screen.height * _heightFraction;
            float inputHeight = _fontSize * 2f;
            GUI.Box(new Rect(0f, 0f, Screen.width, height), GUIContent.none);

            var viewRect = new Rect(8f, 8f, Screen.width - 16f, height - inputHeight - 24f);
            float contentHeight = _labelStyle.CalcHeight(new GUIContent(_historyText), viewRect.width - 20f);
            _scroll = GUI.BeginScrollView(viewRect, _scroll, new Rect(0f, 0f, viewRect.width - 20f, contentHeight));
            GUI.Label(new Rect(0f, 0f, viewRect.width - 20f, contentHeight), _historyText, _labelStyle);
            GUI.EndScrollView();

            GUI.SetNextControlName(InputControlName);
            _input = GUI.TextField(new Rect(8f, height - inputHeight - 8f, Screen.width - 16f, inputHeight), _input, _inputStyle);
            if (_focusInput)
            {
                GUI.FocusControl(InputControlName);
                _focusInput = false;
            }
        }

        private void Submit()
        {
            string line = _input;
            _input = string.Empty;
            _console.Execute(line);
            _focusInput = true;
        }

        private void OnHistoryChanged()
        {
            _historyDirty = true;
        }

        private void RebuildHistoryText()
        {
            _builder.Clear();
            foreach (string line in _console.History)
            {
                _builder.AppendLine(line);
            }

            _historyText = _builder.ToString();
            _historyDirty = false;
            _scroll.y = float.MaxValue;
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = _fontSize, wordWrap = true, richText = false };
            _inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = _fontSize };
        }
    }
}
