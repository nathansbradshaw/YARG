using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Fx;
using UnityEngine;
using YARG.Audio.PitchDetection;
using YARG.Core.Logging;
using YARG.Core.Audio;
using YARG.Core.IO;
using YARG.Input;
using YARG.Settings;

namespace YARG.Audio.BASS
{
    internal static class NativeMicQueue
    {
        private const string NativeLibrary = "yarg_dance";
        [DllImport(NativeLibrary, EntryPoint = "yarg_mic_monitor_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr CreateMonitor(uint sampleRate);
        [DllImport(NativeLibrary, EntryPoint = "yarg_mic_pitch_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr CreatePitch(uint sampleRate);
        [DllImport(NativeLibrary, EntryPoint = "yarg_mic_monitor_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(IntPtr queue);
        [DllImport(NativeLibrary, EntryPoint = "yarg_mic_monitor_push", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Push(IntPtr queue, IntPtr pcm, UIntPtr samples);
        [DllImport(NativeLibrary, EntryPoint = "yarg_mic_monitor_render", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Render(IntPtr queue, IntPtr pcm, UIntPtr samples);
        [DllImport(NativeLibrary, EntryPoint = "yarg_mic_monitor_clear", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Clear(IntPtr queue);
        [DllImport(NativeLibrary, EntryPoint = "yarg_mic_pitch_read", CallingConvention = CallingConvention.Cdecl)]
        internal static extern UIntPtr ReadPitch(IntPtr queue, IntPtr pcm, UIntPtr samples);
    }

    internal sealed class MonitorPlaybackHandle : IDisposable
    {
        private static readonly StreamProcedure RenderCallback = Render;

#nullable enable
        public static MonitorPlaybackHandle? Create(int sampleRate)
#nullable disable
        {
            IntPtr queue;
            try
            {
                queue = NativeMicQueue.CreateMonitor((uint) sampleRate);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                YargLogger.LogException(e, "Could not load the native microphone monitor");
                return null;
            }
            if (queue == IntPtr.Zero)
            {
                YargLogger.LogError("Could not create the native microphone monitor");
                return null;
            }

            // A pull stream can run on the output mix schedule. BASS push streams always prebuffer.
            int monitorPlaybackHandle = Bass.CreateStream(sampleRate, 1, BassFlags.Default, RenderCallback, queue);
            if (monitorPlaybackHandle == 0)
            {
                YargLogger.LogFormatError("Failed to create monitor stream: {0}!", Bass.LastError);
                NativeMicQueue.Destroy(queue);
                return null;
            }

            if (!Bass.ChannelSetAttribute(monitorPlaybackHandle, ChannelAttribute.Buffer, 0) ||
                !Bass.ChannelSetAttribute(monitorPlaybackHandle, ChannelAttribute.Volume, 0) ||
                !Bass.ChannelPlay(monitorPlaybackHandle))
            {
                YargLogger.LogFormatError("Failed to start unbuffered microphone monitoring: {0}!", Bass.LastError);
                Bass.StreamFree(monitorPlaybackHandle);
                NativeMicQueue.Destroy(queue);
                return null;
            }

            return new MonitorPlaybackHandle(monitorPlaybackHandle, queue);
        }

        public readonly int Handle;
        private readonly IntPtr _queue;
        private bool _disposed;

        private MonitorPlaybackHandle(int handle, IntPtr queue)
        {
            Handle = handle;
            _queue = queue;
        }

        public void Push(IntPtr buffer, int length)
        {
            NativeMicQueue.Push(_queue, buffer, (UIntPtr) (length / sizeof(short)));
        }

        public void Clear()
        {
            NativeMicQueue.Clear(_queue);
        }

        private static int Render(int handle, IntPtr buffer, int length, IntPtr queue)
        {
            NativeMicQueue.Render(queue, buffer, (UIntPtr) (length / sizeof(short)));
            return length;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Bass.StreamFree(Handle);
                NativeMicQueue.Destroy(_queue);
                _disposed = true;
            }
        }
    }

    internal class RecordingHandle : IDisposable
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private const int CallbackPeriodMilliseconds = 2;
#else
        private const int CallbackPeriodMilliseconds = 3;
#endif
        private static readonly int[] _sampleRates = { 48000, 44100, 96000, 16000 };

#nullable enable
        public static RecordingHandle? CreateRecordingHandle(RecordProcedure procedure)
#nullable disable
        {
            foreach (int sampleRate in _sampleRates)
            {
                int handle = Bass.RecordStart(sampleRate, 1, BassFlags.RecordPause, CallbackPeriodMilliseconds, procedure, IntPtr.Zero);
                if (handle == 0)
                {
                    YargLogger.LogFormatTrace("Failed to start clean recording at {0} Hz: {1}!", sampleRate, Bass.LastError);
                    continue;
                }

                int processedHandle = Bass.CreateStream(sampleRate, 1, BassFlags.Decode, StreamProcedureType.Push);
                if (processedHandle == 0)
                {
                    YargLogger.LogFormatError("Failed to create processed recording stream at {0} Hz: {1}!", sampleRate, Bass.LastError);
                    Bass.ChannelStop(handle);
                    continue;
                }

                return new RecordingHandle(handle, processedHandle, sampleRate);
            }

            YargLogger.LogError("Failed to start recording at any supported sample rate!");
            return null;
        }

        public readonly int Handle;
        public readonly int ProcessedHandle;

        public readonly int SampleRate;

        private bool _disposed;

        private RecordingHandle(int handle, int processedHandle, int sampleRate)
        {
            Handle = handle;
            ProcessedHandle = processedHandle;
            SampleRate = sampleRate;
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                Bass.ChannelStop(Handle);
                Bass.StreamFree(ProcessedHandle);
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~RecordingHandle()
        {
            Dispose(false);
        }
    }

    public sealed class BassMicDevice : MicDevice
    {
        private const float MIC_HIT_INPUT_THRESHOLD = 25f;

#nullable enable
        internal static BassMicDevice? Create(int deviceId, string name)
#nullable disable
        {
            // Must initialise device before recording
            if (!Bass.RecordInit(deviceId))
            {
                YargLogger.LogFormatError("Failed to initialize recording device: {0}!", Bass.LastError);
                return null;
            }

            var device = new BassMicDevice(deviceId, name);
            device._recordHandle = RecordingHandle.CreateRecordingHandle(device.ProcessRecordData);
            if (device._recordHandle == null)
            {
                FreeDevice(deviceId);
                return null;
            }

            device._pitchDetector = new PitchTracker(sampleRate: device._recordHandle.SampleRate);
            try
            {
                device._pitchQueue = NativeMicQueue.CreatePitch((uint) device._recordHandle.SampleRate);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                YargLogger.LogException(e, "Could not load microphone pitch capture");
            }
            if (device._pitchQueue == IntPtr.Zero)
            {
                YargLogger.LogError("Could not create the microphone pitch queue");
                device.Dispose();
                return null;
            }

            device._monitorHandle = MonitorPlaybackHandle.Create(device._recordHandle.SampleRate);

            int lowEqHandle = BassHelpers.AddEqToChannel(device._recordHandle.ProcessedHandle, _lowEqParameters);
            int highEqHandle = BassHelpers.AddEqToChannel(device._recordHandle.ProcessedHandle, _highEqParameters);
            if (lowEqHandle == 0 || highEqHandle == 0)
            {
                YargLogger.LogFormatError("Failed to add EQ to processed recording stream: {0}!", Bass.LastError);
                device.Dispose();
                return null;
            }
            if (!Bass.ChannelPlay(device._recordHandle.Handle))
            {
                YargLogger.LogFormatError("Failed to start microphone capture: {0}!", Bass.LastError);
                device.Dispose();
                return null;
            }
            return device;
        }

        private static void FreeDevice(int deviceId)
        {
            Bass.CurrentRecordingDevice = deviceId;
            if (!Bass.RecordFree())
            {
                YargLogger.LogFormatWarning("Failed to free recording device after initialization failure: {0}!", Bass.LastError);
            }
        }

        private static readonly PeakEQParameters _lowEqParameters = new()
        {
            fBandwidth = 2.5f, fCenter = 20f, fGain = -10f
        };

        private static readonly PeakEQParameters _highEqParameters = new()
        {
            fBandwidth = 2.5f, fCenter = 10_000f, fGain = -10f
        };

        private float? _lastAmplitude;
        private bool _hasPitchSignal;

        private readonly ConcurrentQueue<MicOutputFrame> _frameQueue = new();

        private PitchTracker _pitchDetector;
        private IntPtr _pitchQueue;

        private MonitorPlaybackHandle _monitorHandle;

        private readonly int _deviceId;

        private RecordingHandle _recordHandle;

        public override int Reset()
        {
            ClearOutputQueue();

            // Query number of bytes in the recording buffer
            int available = Bass.ChannelGetData(_recordHandle.Handle, IntPtr.Zero, (int) DataFlags.Available);

            // Getting channel data removes it from the buffer (clearing it)
            if (Bass.ChannelGetData(_recordHandle.Handle, IntPtr.Zero, available) == -1)
            {
                return (int) Bass.LastError;
            }

            _monitorHandle?.Clear();
            return 0;
        }

        public override bool DequeueOutputFrame(out MicOutputFrame frame)
        {
            if (_frameQueue.TryDequeue(out frame)) return true;
            if (!IsRecordingOutput) return false;

            ProcessPitchBlock();
            return _frameQueue.TryDequeue(out frame);
        }

        public override void ClearOutputQueue()
        {
            _frameQueue.Clear();
            NativeMicQueue.Clear(_pitchQueue);
            _pitchDetector.Reset();
            _hasPitchSignal = false;
            _lastAmplitude = null;
        }

        public override void SetMonitoringLevel(float volume)
        {
            if (_monitorHandle != null && !Bass.ChannelSetAttribute(_monitorHandle.Handle, ChannelAttribute.Volume, volume))
            {
                YargLogger.LogFormatError("Failed to set volume attribute: {0}", Bass.LastError);
            }
        }

        public override SerializedMic Serialize()
        {
            return new SerializedMic(DisplayName);
        }

        private BassMicDevice(int deviceId, string name)
            : base(name)
        {
            _deviceId = deviceId;
        }

        private bool ProcessRecordData(int handle, IntPtr buffer, int length, IntPtr user)
        {
            _monitorHandle?.Push(buffer, length);
            if (IsRecordingOutput)
            {
                NativeMicQueue.Push(_pitchQueue, buffer, (UIntPtr) (length / sizeof(short)));
            }
            return true;
        }

        private unsafe void ProcessPitchBlock()
        {
            // The game pulls analysis data separately, so the recording callback never waits for pitch detection.
            int blockSamples = _recordHandle.SampleRate * RECORD_PERIOD_MS / 1000;
            short* block = stackalloc short[blockSamples];
            int samplesRead = (int) NativeMicQueue.ReadPitch(_pitchQueue, (IntPtr) block, (UIntPtr) blockSamples);
            if (samplesRead == 0) return;

            int blockBytes = samplesRead * sizeof(short);
            if (Bass.StreamPutData(_recordHandle.ProcessedHandle, (IntPtr) block, blockBytes) < 0) return;

            int bytesRead = Bass.ChannelGetData(_recordHandle.ProcessedHandle, (IntPtr) block, blockBytes);
            if (bytesRead >= sizeof(short))
            {
                CalculatePitchAndAmplitude(new ReadOnlySpan<short>(block, bytesRead / sizeof(short)));
            }
        }

        private void CalculatePitchAndAmplitude(ReadOnlySpan<short> buffer)
        {
            int sampleCount = buffer.Length;
            Span<float> floatBuffer = stackalloc float[sampleCount];

            // Convert 16 bit buffer to floats
            // If this isn't 16 bit god knows what device they're using.
            for (int i = 0; i < sampleCount; i++)
            {
                floatBuffer[i] = buffer[i] / 32768f;
            }

            // Calculate the root mean square
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < sampleCount; i += 4, count++)
            {
                sum += floatBuffer[i] * floatBuffer[i];
            }

            sum = Mathf.Sqrt(sum / count);

            // Convert to decibels to get the amplitude
            float amplitude = 20f * Mathf.Log10(sum * 180f);
            if (amplitude < -160f)
            {
                amplitude = -160f;
            }

            // Detect peaks for hit inputs
            if (amplitude > _lastAmplitude && Mathf.Abs(amplitude - _lastAmplitude.Value) >= MIC_HIT_INPUT_THRESHOLD)
            {
                var hitFrame = new MicOutputFrame(InputManager.CurrentInputTime, true, -1f, -1f);
                _frameQueue.Enqueue(hitFrame);
            }

            _lastAmplitude = amplitude;

            // Skip pitch detection if not speaking
            if (amplitude < SettingsManager.Settings.MicrophoneSensitivity.Value)
            {
                if (_hasPitchSignal) _pitchDetector.Reset();
                _hasPitchSignal = false;
                return;
            }
            _hasPitchSignal = true;

            if (_pitchDetector.ProcessBuffer(floatBuffer) is not float pitch)
            {
                return;
            }

            // Queue a MicOutput frame
            var frame = new MicOutputFrame(InputManager.CurrentInputTime, false,
                pitch, amplitude);
            _frameQueue.Enqueue(frame);
        }

        protected override void DisposeUnmanagedResources()
        {
            _recordHandle?.Dispose();
            if (_recordHandle != null)
            {
                FreeDevice(_deviceId);
            }
            _monitorHandle?.Dispose();
            if (_pitchQueue != IntPtr.Zero)
            {
                NativeMicQueue.Destroy(_pitchQueue);
            }
        }
    }
}
