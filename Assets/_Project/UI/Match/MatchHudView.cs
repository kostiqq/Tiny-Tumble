using System;
using System.Collections.Generic;
using System.Text;
using R3;
using TinyTumble.Gameplay.Match;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Match
{
    /// <summary>
    /// Временный HUD матча: встряск каждого участника (чем выше, тем дальше отлетает от удара) и подсказка
    /// по управлению. Полноценный HUD — этап 9; пока текст, чтобы на плейтесте была видна модель «встряска».
    /// </summary>
    public sealed class MatchHudView : MonoBehaviour
    {
        [SerializeField] private Text _label;
        [SerializeField] private string _botName = "Бот";
        [SerializeField, TextArea] private string _controlsHint =
            "Толчок: X / ЛКМ / J (с ношей — держать и отпустить: бросок) · Захват: B / ПКМ / K · Парирование: Y / L · Рывок: RT / Shift · Эмоции: крестовина / 1–4";

        private readonly StringBuilder _builder = new StringBuilder();
        private readonly Dictionary<ICharacterInfo, IDisposable> _subscriptions = new Dictionary<ICharacterInfo, IDisposable>();
        private readonly List<ICharacterInfo> _ordered = new List<ICharacterInfo>();
        private readonly Comparison<ICharacterInfo> _bySlot = CompareBySlot;
        private IMatchCharactersView _characters;
        private bool _dirty = true;

        [Inject]
        public void Construct(IMatchCharactersView characters)
        {
            _characters = characters;
            _characters.CharacterAdded += OnAdded;
            _characters.CharacterRemoved += OnRemoved;
            for (int i = 0; i < _characters.Count; i++)
            {
                OnAdded(_characters[i]);
            }
        }

        private void OnDestroy()
        {
            if (_characters != null)
            {
                _characters.CharacterAdded -= OnAdded;
                _characters.CharacterRemoved -= OnRemoved;
            }

            foreach (IDisposable subscription in _subscriptions.Values)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }

        private void LateUpdate()
        {
            if (_dirty)
            {
                _dirty = false;
                Refresh();
            }
        }

        private void OnAdded(ICharacterInfo character)
        {
            if (character == null || _subscriptions.ContainsKey(character))
            {
                return;
            }

            _subscriptions.Add(character, character.Shake.Subscribe(_ => _dirty = true));
            _dirty = true;
        }

        private void OnRemoved(ICharacterInfo character)
        {
            if (character != null && _subscriptions.TryGetValue(character, out IDisposable subscription))
            {
                subscription.Dispose();
                _subscriptions.Remove(character);
            }

            _dirty = true;
        }

        private void Refresh()
        {
            if (_label == null)
            {
                return;
            }

            _ordered.Clear();
            _ordered.AddRange(_subscriptions.Keys);
            _ordered.Sort(_bySlot);

            _builder.Clear();
            for (int i = 0; i < _ordered.Count; i++)
            {
                ICharacterInfo character = _ordered[i];
                if (character.IsBot)
                {
                    _builder.Append(_botName);
                }
                else
                {
                    _builder.Append('P').Append(character.Slot + 1);
                }

                _builder.Append("  ").Append(Mathf.RoundToInt(character.Shake.CurrentValue)).Append('%').AppendLine();
            }

            _builder.AppendLine().Append(_controlsHint);
            _label.text = _builder.ToString();
        }

        /// <summary>Игроки по номеру слота, боты в конце.</summary>
        private static int CompareBySlot(ICharacterInfo a, ICharacterInfo b)
        {
            int left = a.IsBot ? int.MaxValue : a.Slot;
            int right = b.IsBot ? int.MaxValue : b.Slot;
            return left != right ? left.CompareTo(right) : a.Id.CompareTo(b.Id);
        }
    }
}
