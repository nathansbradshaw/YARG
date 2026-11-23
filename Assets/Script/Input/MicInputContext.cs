using System.Collections.Generic;
using YARG.Audio.BASS;
using YARG.Core.Audio;
using YARG.Core.Input;

namespace YARG.Input
{
    public class MicInputContext
    {
        public readonly MicDevice Device;

        private readonly bool _dryMonitoring;

        public MicInputContext(MicDevice device, bool dryMonitoring)
        {
            Device = device;
            _dryMonitoring = dryMonitoring;
        }

        /// <summary>
        /// Starts recording the output.
        /// </summary>
        public void Start()
        {
            if (_dryMonitoring && Device is BassMicDevice mic)
            {
                mic.SetMonitoringReverb(false);
            }
            Device.ClearOutputQueue();
            Device.IsRecordingOutput = true;
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
            if (_dryMonitoring && Device is BassMicDevice mic)
            {
                mic.SetMonitoringReverb(true);
            }
        }
    }
}
