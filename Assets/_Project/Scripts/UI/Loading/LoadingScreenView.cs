using System;
using System.Collections.Generic;
using TinyTumble.Core.Scenes;
using TinyTumble.Core.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace TinyTumble.UI.Loading
{
    /// <summary>
    /// Экран загрузки в корневом префабе. Виден, пока жив хотя бы один холд.
    /// Текст — сообщение самого свежего живого холда с сообщением, иначе текст по умолчанию.
    /// </summary>
    public sealed class LoadingScreenView : MonoBehaviour, ILoadingScreen
    {
        private sealed class HoldHandle : IDisposable
        {
            private LoadingScreenView _owner;

            public HoldHandle(LoadingScreenView owner, string message)
            {
                _owner = owner;
                Message = message;
            }

            public string Message { get; }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                _owner.ReleaseHold(this);
                _owner = null;
            }
        }

        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _label;
        [SerializeField] private string _defaultText = "Загрузка…";

        private readonly HoldCounter _holds = new HoldCounter();
        private readonly List<HoldHandle> _messageHolds = new List<HoldHandle>();

        public bool IsVisible => _holds.IsHeld;

        public int HoldCount => _holds.Count;

        public string CurrentText => _messageHolds.Count > 0 ? _messageHolds[_messageHolds.Count - 1].Message : _defaultText;

        public IDisposable Hold()
        {
            return Hold(null);
        }

        public IDisposable Hold(string message)
        {
            var handle = new HoldHandle(this, message);
            if (!string.IsNullOrEmpty(message))
            {
                _messageHolds.Add(handle);
                RefreshText();
            }

            if (_holds.Acquire())
            {
                SetVisible(true);
            }

            return handle;
        }

        private void Awake()
        {
            SetVisible(_holds.IsHeld);
            RefreshText();
        }

        private void ReleaseHold(HoldHandle handle)
        {
            if (_messageHolds.Remove(handle))
            {
                RefreshText();
            }

            if (_holds.Release())
            {
                SetVisible(false);
            }
        }

        private void RefreshText()
        {
            if (_label != null)
            {
                _label.text = CurrentText;
            }
        }

        private void SetVisible(bool visible)
        {
            if (_canvasGroup == null)
            {
                return;
            }

            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible;
            _canvasGroup.interactable = visible;
        }
    }
}
