using System.Collections.Generic;
using YARG.Core.Audio;
using YARG.Core.Input;
using YARG.Settings;

namespace YARG.Input
{
    public class MicInputContext
    {
        public readonly MicDevice Device;
        private readonly bool _karaoke;

        public MicInputContext(MicDevice device, bool karaoke)
        {
            Device = device;
            _karaoke = karaoke;
        }

        /// <summary>
        /// Starts recording the output.
        /// </summary>
        public void Start()
        {
            Device.ClearOutputQueue();
            Device.IsRecordingOutput = true;
            Device.SetMonitoringLevel(GetMonitoringLevel(_karaoke));
        }

        /// <summary>
        /// Gets the mic's input, converts it to an engine compatible format,
        /// then pushes the inputs to the <paramref name="engine"/>.
        /// </summary>
        public IEnumerable<GameInput> GetInputsFromMic()
        {
            while (Device.DequeueOutputFrame(out var frame))
            {
                // frame.VoiceDetected will ALWAYS be true here, as it wouldn't be queued otherwise

                // Queue it up!
                GameInput gameInput;
                if (!frame.IsHit)
                {
                    gameInput = GameInput.Create(frame.Time, VocalsAction.Pitch, frame.PitchAsMidiNote);
                }
                else
                {
                    gameInput = GameInput.Create(frame.Time, VocalsAction.Hit, true);
                }

                yield return gameInput;
            }
        }

        /// <summary>
        /// Stops recording the output.
        /// </summary>
        public void Stop()
        {
            Device.IsRecordingOutput = false;
            Device.SetMonitoringLevel(GetMonitoringLevel(false));
        }

        internal static float GetMonitoringLevel(bool karaoke) => karaoke
            ? SettingsManager.Settings.KaraokeMicVolume.Value
            : SettingsManager.Settings.VocalMonitoring.Value;
    }
}
