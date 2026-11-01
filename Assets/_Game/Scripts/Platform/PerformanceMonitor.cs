using System;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

namespace OpenSkies.Platform
{
    /// <summary>
    /// Frame times over a sliding window with average and percentiles, without
    /// allocating after construction. Plain C#, EditMode-tested.
    /// </summary>
    public sealed class FrameStatistics
    {
        readonly float[] _samples;
        readonly float[] _sorted;
        int _count, _next;
        bool _sortedValid;

        public FrameStatistics(int capacity = 600)
        {
            _samples = new float[Math.Max(1, capacity)];
            _sorted = new float[_samples.Length];
        }

        public int Count => _count;
        public int Capacity => _samples.Length;

        /// <summary>Adds one frame time, ms. Ignores bad values.</summary>
        public void Add(float milliseconds)
        {
            if (float.IsNaN(milliseconds) || float.IsInfinity(milliseconds) || milliseconds < 0f) return;
            _samples[_next] = milliseconds;
            _next = (_next + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
            _sortedValid = false;
        }

        public void Clear()
        {
            _count = 0;
            _next = 0;
            _sortedValid = false;
        }

        public float Average
        {
            get
            {
                if (_count == 0) return 0f;
                double sum = 0;
                for (int i = 0; i < _count; i++) sum += _samples[i];
                return (float)(sum / _count);
            }
        }

        public float Max
        {
            get
            {
                float max = 0f;
                for (int i = 0; i < _count; i++) max = Math.Max(max, _samples[i]);
                return max;
            }
        }

        /// <summary>Nearest-rank percentile (0-100) of the window, ms.</summary>
        public float Percentile(float percent)
        {
            if (_count == 0) return 0f;
            if (!_sortedValid)
            {
                Array.Copy(_samples, _sorted, _count);
                Array.Sort(_sorted, 0, _count);
                _sortedValid = true;
            }
            percent = Math.Max(0f, Math.Min(100f, percent));
            int rank = (int)Math.Ceiling(percent / 100f * _count) - 1;
            return _sorted[Math.Max(0, Math.Min(_count - 1, rank))];
        }

        /// <summary>Frames in the window slower than the threshold (hitches), ms.</summary>
        public int CountAbove(float milliseconds)
        {
            int n = 0;
            for (int i = 0; i < _count; i++)
                if (_samples[i] > milliseconds) n++;
            return n;
        }
    }

    /// <summary>
    /// Development-build performance sampling for device profiling: frame time
    /// average and percentiles, CPU and GPU frame times (FrameTimingManager, where
    /// the device supports it), garbage collections, memory, draw calls, SetPass
    /// calls and batches (ProfilerRecorder; some counters exist only in development
    /// players). Optional on-screen line and a CSV log to persistentDataPath for
    /// long (15-minute) flights. Disabled in release builds unless forced. The
    /// numbers are measurements only when taken on the device; the Editor's are not
    /// representative.
    /// </summary>
    public sealed class PerformanceMonitor : MonoBehaviour
    {
        [Tooltip("Overlay line (development builds). Optional.")]
        public Text overlay;
        [Tooltip("Run in release builds too (for a profiling build without the development overhead).")]
        public bool runInReleaseBuilds;
        [Tooltip("Window of the statistics, frames (600 = 10 s at 60 FPS, 20 s at 30 FPS).")]
        public int windowFrames = 600;
        public float overlayRefreshSeconds = 1f;
        [Tooltip("Write a CSV line at this interval while logging, s.")]
        public float logIntervalSeconds = 10f;
        [Tooltip("Start the CSV log at start-up (development builds).")]
        public bool logToFile;

        FrameStatistics _stats;
        ProfilerRecorder _systemMemory, _gcReserved, _gcAllocInFrame, _drawCalls, _setPass, _batches;
        readonly FrameTiming[] _timing = new FrameTiming[1];
        readonly StringBuilder _text = new StringBuilder(256);
        float _nextOverlay, _nextLog, _cpuMs, _gpuMs, _logStarted;
        int _gcAtStart, _hitches;
        long _peakSystemMemory;
        StreamWriter _log;
        bool _active;

        public FrameStatistics Statistics => _stats;
        public bool Active => _active;
        public string LogPath { get; private set; }
        public float CpuFrameMs => _cpuMs;
        public float GpuFrameMs => _gpuMs;
        public int GarbageCollections => GC.CollectionCount(0) - _gcAtStart;

        void OnEnable()
        {
            _active = Debug.isDebugBuild || runInReleaseBuilds;
            if (overlay != null) overlay.enabled = _active && Debug.isDebugBuild;
            if (!_active) return;
            _stats = new FrameStatistics(windowFrames);
            _gcAtStart = GC.CollectionCount(0);
            _systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory", 1);
            _gcReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory", 1);
            _gcAllocInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            if (logToFile) StartLog();
        }

        void OnDisable()
        {
            StopLog();
            if (!_active) return; // nothing was started
            _systemMemory.Dispose();
            _gcReserved.Dispose();
            _gcAllocInFrame.Dispose();
            _drawCalls.Dispose();
            _setPass.Dispose();
            _batches.Dispose();
        }

        void Update()
        {
            if (!_active) return;
            float ms = Time.unscaledDeltaTime * 1000f;
            _stats.Add(ms);
            if (ms > 50f) _hitches++;
            if (_systemMemory.Valid) _peakSystemMemory = Math.Max(_peakSystemMemory, _systemMemory.LastValue);

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
            {
                _cpuMs = (float)_timing[0].cpuFrameTime;
                _gpuMs = (float)_timing[0].gpuFrameTime;
            }

            float now = Time.unscaledTime;
            if (overlay != null && overlay.enabled && now >= _nextOverlay)
            {
                _nextOverlay = now + overlayRefreshSeconds;
                overlay.text = Summary();
            }
            if (_log != null && now >= _nextLog)
            {
                _nextLog = now + logIntervalSeconds;
                WriteLogLine(now);
            }
        }

        /// <summary>One line for the overlay (development only: it builds a string once a second).</summary>
        public string Summary()
        {
            _text.Clear();
            float avg = _stats.Average;
            _text.Append("PERF ").Append(avg > 0f ? (1000f / avg).ToString("0") : "-").Append(" fps  avg ").Append(avg.ToString("0.0"))
                .Append(" p95 ").Append(_stats.Percentile(95f).ToString("0.0")).Append(" p99 ").Append(_stats.Percentile(99f).ToString("0.0"))
                .Append(" ms  CPU ").Append(_cpuMs > 0f ? _cpuMs.ToString("0.0") : "n/a")
                .Append(" GPU ").Append(_gpuMs > 0f ? _gpuMs.ToString("0.0") : "n/a")
                .Append("  GC ").Append(GarbageCollections)
                .Append("  mem ").Append(Megabytes(_systemMemory)).Append(" MB")
                .Append("  draws ").Append(Count(_drawCalls)).Append(" setpass ").Append(Count(_setPass)).Append(" batches ").Append(Count(_batches));
            return _text.ToString();
        }

        /// <summary>Starts (or restarts) the CSV log in persistentDataPath. Development use.</summary>
        public void StartLog()
        {
            StopLog();
            try
            {
                LogPath = Path.Combine(Application.persistentDataPath, "openskies-perf-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                _log = new StreamWriter(LogPath, false);
                _log.WriteLine("seconds,avg_ms,p50_ms,p95_ms,p99_ms,max_ms,hitches_over_50ms,cpu_ms,gpu_ms,gc_collections,gc_alloc_frame_bytes," +
                               "system_mb,peak_system_mb,gc_reserved_mb,draw_calls,setpass_calls,batches,target_fps");
                _logStarted = Time.unscaledTime;
                _nextLog = Time.unscaledTime + logIntervalSeconds;
                _hitches = 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpenSkies] Performance log unavailable: " + e.Message);
                _log = null;
            }
        }

        public void StopLog()
        {
            if (_log == null) return;
            _log.Flush();
            _log.Dispose();
            _log = null;
        }

        void WriteLogLine(float now)
        {
            _text.Clear();
            _text.Append((now - _logStarted).ToString("0")).Append(',')
                .Append(_stats.Average.ToString("0.00")).Append(',').Append(_stats.Percentile(50f).ToString("0.00")).Append(',')
                .Append(_stats.Percentile(95f).ToString("0.00")).Append(',').Append(_stats.Percentile(99f).ToString("0.00")).Append(',')
                .Append(_stats.Max.ToString("0.00")).Append(',').Append(_hitches).Append(',')
                .Append(_cpuMs.ToString("0.00")).Append(',').Append(_gpuMs.ToString("0.00")).Append(',')
                .Append(GarbageCollections).Append(',').Append(Count(_gcAllocInFrame)).Append(',')
                .Append(Megabytes(_systemMemory)).Append(',').Append(_peakSystemMemory / (1024 * 1024)).Append(',').Append(Megabytes(_gcReserved)).Append(',')
                .Append(Count(_drawCalls)).Append(',').Append(Count(_setPass)).Append(',').Append(Count(_batches)).Append(',')
                .Append(Application.targetFrameRate);
            _log.WriteLine(_text.ToString());
            _log.Flush(); // the app can be killed at any time
        }

        static string Megabytes(ProfilerRecorder recorder) => recorder.Valid ? (recorder.LastValue / (1024 * 1024)).ToString() : "n/a";
        static string Count(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue.ToString() : "n/a";
    }
}
