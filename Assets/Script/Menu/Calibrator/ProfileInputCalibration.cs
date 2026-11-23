using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Logging;
using YARG.Input;
using YARG.Localization;
using YARG.Menu.Dialogs;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Playback;
using YARG.Player;

namespace YARG.Menu.Calibrator
{
    internal static class ProfileInputCalibration
    {
        private const string Localization = "Menu.ProfileCalibration";
        private const int MaxInputs = 256;

        [DllImport("yarg_dance", CallingConvention = CallingConvention.Cdecl)]
        private static extern UIntPtr yarg_input_calibration_clicks([Out] byte[] output, UIntPtr capacity);

        [DllImport("yarg_dance", CallingConvention = CallingConvention.Cdecl)]
        private static extern int yarg_input_calibration_offset([In] double[] hits, UIntPtr count);

        public static void Show(YargProfile profile, Action onSaved)
        {
            var dialogs = DialogManager.Instance;
            if (dialogs.IsDialogShowing) return;

            var title = Localize.Key(Localization, "Title");
            if (profile == null || profile.IsBot || profile.GameMode == GameMode.Vocals)
            {
                dialogs.ShowMessage(title, Localize.Key(Localization, "SelectInstrument"));
                return;
            }

            var player = PlayerContainer.GetPlayerFromProfile(profile);
            var mode = profile.GameMode;
            if (player == null || !player.InputsEnabled || !player.Bindings.HasDeviceAssigned)
            {
                dialogs.ShowMessage(title, Localize.Key(Localization, "ConnectInstrument"));
                return;
            }

            var bindings = player.Bindings[mode];
            bool useStrum = mode is GameMode.FiveFretGuitar or GameMode.SixFretGuitar &&
                bindings.Any(binding => IsStrum(binding.Action) && player.Bindings.InputDevices.Any(binding.ContainsBindingsForDevice));
            var instruction = Localize.Key(Localization, mode switch
            {
                GameMode.FiveFretGuitar or GameMode.SixFretGuitar when useStrum => "Strum",
                GameMode.ProGuitar => "PickString",
                GameMode.EliteDrums or GameMode.FourLaneDrums or GameMode.FiveLaneDrums => "HitPad",
                _ => "TapNote"
            });

            var dialog = dialogs.ShowMessage(title, Localize.KeyFormat(Localization + ".Instructions",
                profile.Name, instruction, profile.InputCalibrationMilliseconds));
            bool capturing = false;
            void Start()
            {
                if (capturing) return;
                capturing = true;
                Capture(dialog, player, mode, useStrum, instruction, Start, onSaved).Forget();
            }

            dialog.ClearButtons();
            dialog.AddDialogButton("Menu.Common.Start", Start);
            dialog.AddDialogButton("Menu.Common.Cancel", dialogs.ClearDialog);

            async UniTaskVoid Capture(MessageDialog view, YargPlayer selected, GameMode gameMode,
                bool strum, string actionText, Action retry, Action saved)
            {
                try
                {
                    await Run(view, selected, gameMode, strum, actionText, retry, saved);
                }
                finally
                {
                    capturing = false;
                }
            }
        }

        private static async UniTask Run(MessageDialog dialog, YargPlayer player, GameMode mode, bool useStrum,
            string instruction, Action retry, Action onSaved)
        {
            var dialogs = DialogManager.Instance;
            var navigation = Navigator.Instance;
            var music = HelpBar.Instance.MusicPlayer;
            bool restoreMusic = music.gameObject.activeSelf;
            bool menuInputWasDisabled = navigation.DisableMenuInputs;
            var hits = new List<double>(32);
            var bindings = player.Bindings;
            string result = "Inconsistent";
            int offset = int.MinValue;

            dialog.ClearButtons();
            dialog.AddDialogButton("Menu.Common.Cancel", dialogs.ClearDialog);
            dialog.Message.text = Localize.KeyFormat(Localization + ".CountIn", instruction);

            try
            {
                navigation.DisableMenuInputs = true;
                if (restoreMusic) music.gameObject.SetActive(false);

                var length = yarg_input_calibration_clicks(null, UIntPtr.Zero);
                var wave = new byte[checked((int) length.ToUInt64())];
                yarg_input_calibration_clicks(wave, length);
                using var audio = new MemoryStream(wave, false);
                using var mixer = GlobalAudioHandler.LoadCustomFile("Instrument calibration.wav", audio, 1f, 0.8);
                if (mixer == null)
                {
                    result = "AudioError";
                }
                else
                {
                    using var runner = new SongRunner(mixer, 0, 0, 1f, 0);
                    void OnInput(ref GameInput input)
                    {
                        if (runner.Started && hits.Count < MaxInputs && IsPlayingInput(mode, useStrum, input))
                            hits.Add(runner.GetRelativeInputTime(input.Time));
                    }

                    bindings.SubscribeToGameplayInputs(mode, OnInput);
                    try
                    {
                        double timeout = Time.realtimeSinceStartupAsDouble + mixer.Length + 5;
                        bool measuring = false;
                        while (dialog != null && dialog.isActiveAndEnabled)
                        {
                            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                            {
                                dialogs.ClearDialog();
                                return;
                            }
                            if (player.Profile.GameMode != mode || player.Bindings != bindings ||
                                !player.InputsEnabled || !bindings.HasDeviceAssigned)
                            {
                                result = "ConnectInstrument";
                                break;
                            }
                            runner.Update();
                            if (!measuring && runner.AudioPlaybackTime >= 2.8)
                            {
                                measuring = true;
                                dialog.Message.text = Localize.KeyFormat(Localization + ".Playing", instruction);
                            }
                            if (runner.SongTime >= mixer.Length)
                            {
                                offset = yarg_input_calibration_offset(hits.ToArray(), (UIntPtr) hits.Count);
                                break;
                            }
                            if (Time.realtimeSinceStartupAsDouble >= timeout)
                            {
                                result = "AudioError";
                                break;
                            }
                            await UniTask.Yield(PlayerLoopTiming.Update);
                        }
                    }
                    finally
                    {
                        bindings.UnsubscribeFromGameplayInputs(mode, OnInput);
                    }
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Could not calibrate instrument input");
                result = "AudioError";
            }
            finally
            {
                if (navigation != null) navigation.DisableMenuInputs = menuInputWasDisabled;
                if (restoreMusic && music != null && GlobalVariables.Instance?.CurrentScene == SceneIndex.Menu)
                    music.gameObject.SetActive(true);
            }

            if (dialog == null || !dialog.isActiveAndEnabled) return;
            dialog.ClearButtons();
            if (offset != int.MinValue)
            {
                dialog.Message.text = Localize.KeyFormat(Localization + ".Result", offset,
                    player.Profile.InputCalibrationMilliseconds);
                dialog.AddDialogButton("Menu.Common.Save", () =>
                {
                    if (SaveOffset(player.Profile, offset))
                    {
                        onSaved();
                        dialogs.ClearDialog();
                    }
                    else
                    {
                        dialog.Message.text = Localize.Key(Localization, "SaveError");
                    }
                });
            }
            else
            {
                dialog.Message.text = Localize.Key(Localization, result);
            }
            dialog.AddDialogButton(Localization + ".Retry", () => retry());
            dialog.AddDialogButton("Menu.Common.Cancel", dialogs.ClearDialog);
        }

        private static bool SaveOffset(YargProfile profile, int offset)
        {
            long previous = profile.InputCalibrationMilliseconds;
            profile.InputCalibrationMilliseconds = offset;
            if (PlayerContainer.SaveProfiles(false) > 0) return true;
            profile.InputCalibrationMilliseconds = previous;
            return false;
        }

        private static bool IsStrum(int action) => action is (int) GuitarAction.StrumUp or (int) GuitarAction.StrumDown;

        private static bool IsPlayingInput(GameMode mode, bool useStrum, GameInput input) => mode switch
        {
            GameMode.FiveFretGuitar or GameMode.SixFretGuitar => input.Button && (useStrum ? IsStrum(input.Action) :
                input.Action is >= (int) GuitarAction.Fret1 and <= (int) GuitarAction.Fret6 or
                    >= (int) GuitarAction.Fret11 and <= (int) GuitarAction.Fret15),
            GameMode.ProGuitar => input.Button && input.Action is >= (int) ProGuitarAction.String1_Strum and <= (int) ProGuitarAction.String6_Strum,
            GameMode.EliteDrums or GameMode.FourLaneDrums or GameMode.FiveLaneDrums => input.Axis > 0,
            GameMode.ProKeys => input.Button && input.Action is >= (int) ProKeysAction.Key1 and <= (int) ProKeysAction.Key25 or
                >= (int) ProKeysAction.OpenNote and <= (int) ProKeysAction.OrangeKey,
            GameMode.Dance or GameMode.FourKey => input.Button && input.Action is >= 0 and < 4,
            _ => false
        };
    }
}
