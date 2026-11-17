using TinyTumble.Core.Input;
using UnityEngine.InputSystem;

namespace TinyTumble.Infrastructure.Input
{
    /// <summary>
    /// Раскладка из раздела 4 GDD. Карта создаётся на каждого игрока отдельно и ограничивается его устройствами,
    /// поэтому два геймпада не управляют одним персонажем.
    /// </summary>
    public static class PlayerControls
    {
        public const string MapName = "Player";
        public const string MoveAction = "Move";

        // Камера: локальна, в кадр ввода персонажа не попадает.
        public const string LookMouseAction = "LookMouse";
        public const string LookStickAction = "LookStick";
        public const string SpectateNextAction = "SpectateNext";
        public const string SpectatePreviousAction = "SpectatePrevious";
        public const string CursorToggleAction = "CursorToggle";

        public struct ButtonBinding
        {
            public PlayerButtons Button;
            public string ActionName;
            public string[] Paths;
        }

        public static readonly ButtonBinding[] Buttons =
        {
            new ButtonBinding { Button = PlayerButtons.Jump, ActionName = "Jump", Paths = new[] { "<Gamepad>/buttonSouth", "<Keyboard>/space" } },
            new ButtonBinding { Button = PlayerButtons.Push, ActionName = "Push", Paths = new[] { "<Gamepad>/buttonWest", "<Mouse>/leftButton", "<Keyboard>/j" } },
            new ButtonBinding { Button = PlayerButtons.Grab, ActionName = "Grab", Paths = new[] { "<Gamepad>/buttonEast", "<Mouse>/rightButton", "<Keyboard>/k" } },
            new ButtonBinding { Button = PlayerButtons.Parry, ActionName = "Parry", Paths = new[] { "<Gamepad>/buttonNorth", "<Keyboard>/l" } },
            new ButtonBinding { Button = PlayerButtons.Dash, ActionName = "Dash", Paths = new[] { "<Gamepad>/rightTrigger", "<Keyboard>/leftShift" } },
            new ButtonBinding { Button = PlayerButtons.Emote1, ActionName = "Emote1", Paths = new[] { "<Gamepad>/dpad/up", "<Keyboard>/1" } },
            new ButtonBinding { Button = PlayerButtons.Emote2, ActionName = "Emote2", Paths = new[] { "<Gamepad>/dpad/right", "<Keyboard>/2" } },
            new ButtonBinding { Button = PlayerButtons.Emote3, ActionName = "Emote3", Paths = new[] { "<Gamepad>/dpad/down", "<Keyboard>/3" } },
            new ButtonBinding { Button = PlayerButtons.Emote4, ActionName = "Emote4", Paths = new[] { "<Gamepad>/dpad/left", "<Keyboard>/4" } },
        };

        public static InputActionMap CreateMap()
        {
            var map = new InputActionMap(MapName);

            InputAction move = map.AddAction(MoveAction, InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddBinding("<Gamepad>/leftStick");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            for (int i = 0; i < Buttons.Length; i++)
            {
                InputAction action = map.AddAction(Buttons[i].ActionName, InputActionType.Button);
                string[] paths = Buttons[i].Paths;
                for (int p = 0; p < paths.Length; p++)
                {
                    action.AddBinding(paths[p]);
                }
            }

            InputAction lookMouse = map.AddAction(LookMouseAction, InputActionType.Value, expectedControlLayout: "Vector2");
            lookMouse.AddBinding("<Mouse>/delta");

            InputAction lookStick = map.AddAction(LookStickAction, InputActionType.Value, expectedControlLayout: "Vector2");
            lookStick.AddBinding("<Gamepad>/rightStick");

            InputAction next = map.AddAction(SpectateNextAction, InputActionType.Button);
            next.AddBinding("<Gamepad>/rightShoulder");
            next.AddBinding("<Keyboard>/e");

            InputAction previous = map.AddAction(SpectatePreviousAction, InputActionType.Button);
            previous.AddBinding("<Gamepad>/leftShoulder");
            previous.AddBinding("<Keyboard>/q");

            InputAction cursorToggle = map.AddAction(CursorToggleAction, InputActionType.Button);
            cursorToggle.AddBinding("<Keyboard>/escape");

            return map;
        }
    }
}
