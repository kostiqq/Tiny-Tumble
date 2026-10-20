using System;
using TinyTumble.Core.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace TinyTumble.Infrastructure.Input
{
    /// <summary>
    /// Ввод игрока с его устройств (геймпад или клавиатура + мышь).
    /// </summary>
    public sealed class DevicePlayerInput : IPlayerInput, IDisposable
    {
        private readonly InputActionMap _map;
        private readonly InputAction _move;
        private readonly InputAction[] _buttonActions;
        private readonly PlayerButtons[] _buttonFlags;
        private InputAccumulator _accumulator;

        public DevicePlayerInput(InputDevice[] devices)
        {
            _map = PlayerControls.CreateMap();
            _map.devices = new ReadOnlyArray<InputDevice>(devices);
            _move = _map.FindAction(PlayerControls.MoveAction, throwIfNotFound: true);

            int count = PlayerControls.Buttons.Length;
            _buttonActions = new InputAction[count];
            _buttonFlags = new PlayerButtons[count];
            for (int i = 0; i < count; i++)
            {
                _buttonActions[i] = _map.FindAction(PlayerControls.Buttons[i].ActionName, throwIfNotFound: true);
                _buttonFlags[i] = PlayerControls.Buttons[i].Button;
            }

            // Одна подписка на всю карту вместо замыкания на каждую кнопку.
            _map.actionTriggered += OnActionTriggered;
            _map.Enable();
        }

        public PlayerInputFrame Sample()
        {
            PlayerButtons held = PlayerButtons.None;
            for (int i = 0; i < _buttonActions.Length; i++)
            {
                if (_buttonActions[i].IsPressed())
                {
                    held |= _buttonFlags[i];
                }
            }

            return _accumulator.Consume(_move.ReadValue<Vector2>(), held);
        }

        public void Dispose()
        {
            _map.actionTriggered -= OnActionTriggered;
            _map.Disable();
            _map.Dispose();
        }

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            PlayerButtons flag = FindFlag(context.action);
            if (flag == PlayerButtons.None)
            {
                return;
            }

            if (context.phase == InputActionPhase.Performed)
            {
                _accumulator.RegisterPress(flag);
            }
            else if (context.phase == InputActionPhase.Canceled)
            {
                _accumulator.RegisterRelease(flag);
            }
        }

        private PlayerButtons FindFlag(InputAction action)
        {
            for (int i = 0; i < _buttonActions.Length; i++)
            {
                if (_buttonActions[i] == action)
                {
                    return _buttonFlags[i];
                }
            }

            return PlayerButtons.None;
        }
    }
}
