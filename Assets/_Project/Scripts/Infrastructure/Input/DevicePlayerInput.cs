using System;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace TinyTumble.Infrastructure.Input
{
    /// <summary>
    /// Ввод игрока с его устройств (геймпад или клавиатура + мышь): персонаж и камера.
    /// </summary>
    public sealed class DevicePlayerInput : IPlayerInput, IPlayerLookInput, IDisposable
    {
        private readonly InputActionMap _map;
        private readonly InputAction _move;
        private readonly InputAction _lookMouse;
        private readonly InputAction _lookStick;
        private readonly InputAction _spectateNext;
        private readonly InputAction _spectatePrevious;
        private readonly InputAction _cursorToggle;
        private readonly InputAction[] _buttonActions;
        private readonly PlayerButtons[] _buttonFlags;
        private readonly InputConfig _config;
        private InputAccumulator _accumulator;
        private int _spectateSteps;
        private bool _cursorTogglePressed;

        /// <param name="config">Чувствительность стика; null — сырое значение (тесты).</param>
        public DevicePlayerInput(InputDevice[] devices, InputConfig config = null)
        {
            _config = config;
            _map = PlayerControls.CreateMap();
            _map.devices = new ReadOnlyArray<InputDevice>(devices);
            _move = _map.FindAction(PlayerControls.MoveAction, throwIfNotFound: true);
            _lookMouse = _map.FindAction(PlayerControls.LookMouseAction, throwIfNotFound: true);
            _lookStick = _map.FindAction(PlayerControls.LookStickAction, throwIfNotFound: true);
            _spectateNext = _map.FindAction(PlayerControls.SpectateNextAction, throwIfNotFound: true);
            _spectatePrevious = _map.FindAction(PlayerControls.SpectatePreviousAction, throwIfNotFound: true);
            _cursorToggle = _map.FindAction(PlayerControls.CursorToggleAction, throwIfNotFound: true);

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

            Vector2 move = _move.ReadValue<Vector2>();
            if (_config != null)
            {
                move = StickResponse.Apply(move, _config.StickDeadZone, _config.StickOuterZone, _config.StickResponseExponent);
            }

            return _accumulator.Consume(move, held);
        }

        public Vector2 ReadMouseDelta()
        {
            return _lookMouse.ReadValue<Vector2>();
        }

        public Vector2 ReadStick()
        {
            return _lookStick.ReadValue<Vector2>();
        }

        public int ConsumeSpectateSteps()
        {
            int steps = _spectateSteps;
            _spectateSteps = 0;
            return steps;
        }

        public bool ConsumeCursorToggle()
        {
            bool pressed = _cursorTogglePressed;
            _cursorTogglePressed = false;
            return pressed;
        }

        public void Dispose()
        {
            _map.actionTriggered -= OnActionTriggered;
            _map.Disable();
            _map.Dispose();
        }

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            // Нажатия камеры копим до чтения: так они не теряются при любом режиме обновления Input System.
            if (context.phase == InputActionPhase.Performed)
            {
                if (context.action == _spectateNext)
                {
                    _spectateSteps++;
                    return;
                }

                if (context.action == _spectatePrevious)
                {
                    _spectateSteps--;
                    return;
                }

                if (context.action == _cursorToggle)
                {
                    _cursorTogglePressed = true;
                    return;
                }
            }

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
