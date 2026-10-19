using System;
using TinyTumble.Core.Scenes;
using TinyTumble.Core.Utils;
using UnityEngine;

namespace TinyTumble.UI.Loading
{
    /// <summary>
    /// Экран загрузки в корневом префабе. Виден, пока жив хотя бы один холд.
    /// </summary>
    public sealed class LoadingScreenView : MonoBehaviour, ILoadingScreen
    {
        private sealed class HoldHandle : IDisposable
        {
            private LoadingScreenView _owner;

            public HoldHandle(LoadingScreenView owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                _owner.ReleaseHold();
                _owner = null;
            }
        }

        [SerializeField] private CanvasGroup _canvasGroup;

        private readonly HoldCounter _holds = new HoldCounter();

        public bool IsVisible => _holds.IsHeld;

        public int HoldCount => _holds.Count;

        public IDisposable Hold()
        {
            if (_holds.Acquire())
            {
                SetVisible(true);
            }

            return new HoldHandle(this);
        }

        private void Awake()
        {
            SetVisible(_holds.IsHeld);
        }

        private void ReleaseHold()
        {
            if (_holds.Release())
            {
                SetVisible(false);
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
