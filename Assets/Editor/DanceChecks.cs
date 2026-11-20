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
                if (controls.Length != 5) throw new Exception("Expected four independent dance buttons and star power");
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
                    ? new[] { Key.LeftArrow, Key.DownArrow, Key.UpArrow, Key.RightArrow, Key.RightShift }
                    : new[] { Key.D, Key.F, Key.J, Key.K, Key.Space };
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                InputSystem.Update();
                if (pressed.Count != 5 || !pressed.Contains(4)) throw new Exception($"{mode}: expected independent notes and star-power input, got {pressed.Count}");
                binds.DisableInputs();
                var legacy = binds.Serialize();
                legacy.ModeMappings[mode].Bindings.Remove("Guitar.StarPower");
                using var migrated = new ProfileBindings(new YargProfile { GameMode = mode }, legacy);
                migrated.AddDevice(keyboard);
                var power = migrated[mode].TryGetBindingByAction(4);
                var powerKey = mode == GameMode.Dance ? keyboard.rightShiftKey : keyboard.spaceKey;
                if (power == null || !power.ContainsControl(powerKey)) throw new Exception("Legacy Dance profile did not receive its new SP binding");
                if (!migrated[mode].TryGetBindingByAction(0).ContainsControl(mode == GameMode.Dance ? keyboard.leftArrowKey : keyboard.dKey))
                    throw new Exception("SP migration changed an existing note binding");
                power.ClearAllBindings();
                var savedUnbound = migrated.Serialize();
                using var intentional = new ProfileBindings(new YargProfile { GameMode = mode }, savedUnbound);
                intentional.AddDevice(keyboard);
                if (intentional[mode].TryGetBindingByAction(4).ContainsControl(powerKey)) throw new Exception("Intentional SP unbind was overwritten");
            }
            finally { InputSystem.RemoveDevice(keyboard); }
        }
    }
}
