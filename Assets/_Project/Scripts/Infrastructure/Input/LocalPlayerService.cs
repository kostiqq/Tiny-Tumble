using System;
using System.Collections.Generic;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Core.Logging;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace TinyTumble.Infrastructure.Input
{
    /// <summary>
    /// Присоединение локальных игроков: «нажал кнопку на геймпаде — вошёл» (раздел 11 GDD).
    /// Геймпад входит по A или Start, клавиатура — по Enter или пробелу (вместе с мышью).
    /// </summary>
    public sealed class LocalPlayerService : ILocalPlayers, IDisposable
    {
        private const string LogTag = "Players";

        private readonly MatchConfig _matchConfig;
        private readonly InputConfig _inputConfig;
        private readonly IGameLog _log;
        private readonly List<LocalPlayer> _players = new List<LocalPlayer>();
        private readonly List<InputDevice> _joinedDevices = new List<InputDevice>();
        private readonly List<DevicePlayerInput> _inputs = new List<DevicePlayerInput>();
        private IDisposable _anyButtonSubscription;

        public LocalPlayerService(MatchConfig matchConfig, InputConfig inputConfig, IGameLog log)
        {
            _matchConfig = matchConfig;
            _inputConfig = inputConfig;
            _log = log;
        }

        public event Action<LocalPlayer> PlayerJoined;

        public IReadOnlyList<LocalPlayer> Players => _players;

        public int MaxPlayers => _matchConfig.MaxLocalPlayers;

        public bool JoiningEnabled
        {
            get => _anyButtonSubscription != null;
            set
            {
                if (value == JoiningEnabled)
                {
                    return;
                }

                if (value)
                {
                    _anyButtonSubscription = InputSystem.onAnyButtonPress.Call(OnAnyButtonPress);
                }
                else
                {
                    _anyButtonSubscription.Dispose();
                    _anyButtonSubscription = null;
                }
            }
        }

        public bool TryJoinDefault()
        {
            if (_players.Count > 0 || Keyboard.current == null)
            {
                return false;
            }

            return TryJoin(Keyboard.current);
        }

        public void Dispose()
        {
            JoiningEnabled = false;
            for (int i = 0; i < _inputs.Count; i++)
            {
                _inputs[i].Dispose();
            }

            _inputs.Clear();
            _players.Clear();
            _joinedDevices.Clear();
        }

        private void OnAnyButtonPress(InputControl control)
        {
            if (IsJoinControl(control))
            {
                TryJoin(control.device);
            }
        }

        private static bool IsJoinControl(InputControl control)
        {
            if (control.device is Gamepad gamepad)
            {
                return control == gamepad.buttonSouth || control == gamepad.startButton;
            }

            if (control.device is Keyboard keyboard)
            {
                return control == keyboard.enterKey || control == keyboard.spaceKey;
            }

            return false;
        }

        private bool TryJoin(InputDevice device)
        {
            if (_players.Count >= MaxPlayers || _joinedDevices.Contains(device))
            {
                return false;
            }

            InputDevice[] devices;
            if (device is Keyboard && Mouse.current != null)
            {
                devices = new InputDevice[] { device, Mouse.current };
            }
            else
            {
                devices = new[] { device };
            }

            var input = new DevicePlayerInput(devices, _inputConfig);
            var player = new LocalPlayer(_players.Count, device.displayName, input);
            _inputs.Add(input);
            _joinedDevices.Add(device);
            _players.Add(player);

            _log.Info(LogTag, $"P{player.Index + 1} joined with {player.DeviceName}");
            PlayerJoined?.Invoke(player);
            return true;
        }
    }
}
