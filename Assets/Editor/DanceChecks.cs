using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using YARG.Core.Input;
using YARG.Core;
using YARG.Core.Dance;
using YARG.Core.Game;
using YARG.Helpers.Extensions;
using YARG.Input;
using YARG.Menu.ProfileList;

namespace Editor
{
    public static class DanceChecks
    {
        public static void Run()
        {
            if (DanceNative.yarg_dance_abi() != 1) throw new Exception("Dance native ABI mismatch");
            using var bindings = new ProfileBindings(new YargProfile());
            foreach (var mode in new[] { GameMode.Dance, GameMode.FourKey })
            {
                var controls = BindingCollection.CreateGameplayBindings(mode).ToArray();
                if (controls.Length != 4) throw new Exception("Expected four independent dance buttons");
                var instrument = mode.PossibleInstruments().Single();
                if (instrument.ToNativeGameMode() != mode) throw new Exception("Profile instrument mismatch");
                if (string.IsNullOrEmpty(instrument.ToResourceName()) || string.IsNullOrEmpty(mode.ToResourceName()))
                    throw new Exception("Missing profile/library icon mapping");
                if (instrument.ToSortAttribute().ToInstrument() != instrument) throw new Exception("Instrument sort mismatch");
                CheckKeyboard(mode);
            }
            // Check the actual profile picker, rather than just enum membership.
            var picker = (GameMode[])typeof(ProfileSidebar).GetField("_gameModes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null);
            if (!picker.Contains(GameMode.Dance) || !picker.Contains(GameMode.FourKey)) throw new Exception("Dance profiles are missing from the picker");
            Debug.Log("DANCE_CHECKS_PASSED: native ABI, profile picker, four independent bindings, instrument and library mappings");
        }

        private static void CheckKeyboard(GameMode mode)
        {
            var keyboard = InputSystem.AddDevice<Keyboard>("Dance regression keyboard");
            try
            {
                using var binds = new ProfileBindings(new YargProfile { GameMode = mode });
                binds.AddDevice(keyboard);
                binds.SetDefaultBinds(keyboard);
                var pressed = new System.Collections.Generic.HashSet<int>();
                void Capture(ref GameInput input) { if (input.Button) pressed.Add(input.Action); }
                binds.SubscribeToGameplayInputs(mode, Capture);
                binds.EnableInputs();
                var keys = mode == GameMode.Dance
                    ? new[] { Key.LeftArrow, Key.DownArrow, Key.UpArrow, Key.RightArrow }
                    : new[] { Key.D, Key.F, Key.J, Key.K };
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                InputSystem.Update();
                if (pressed.Count != 4) throw new Exception($"{mode}: expected four simultaneous independent keyboard inputs, got {pressed.Count}");
                binds.DisableInputs();
            }
            finally { InputSystem.RemoveDevice(keyboard); }
        }
    }
}
