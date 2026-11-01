using System;
using UnityEngine;

namespace OpenSkies.Platform
{
    /// <summary>The few moments that may vibrate. Nothing vibrates continuously.</summary>
    public enum HapticEvent
    {
        /// <summary>A cockpit switch or lever moved.</summary>
        Selection = 0,
        /// <summary>An important choice confirmed (start a mode, set the parking brake).</summary>
        Confirm = 1,
        /// <summary>Wheels on the runway, light.</summary>
        TouchdownSoft = 2,
        /// <summary>Wheels on the runway, firm.</summary>
        TouchdownFirm = 3,
        /// <summary>A red warning (stall, overspeed) appeared.</summary>
        Warning = 4,
    }

    /// <summary>
    /// Decides whether a haptic pulse may play: the player's switch, device support,
    /// and limits so that nothing ever vibrates continuously (a minimum interval per
    /// event and a short global gap). Plain C#, EditMode-tested.
    /// </summary>
    public sealed class HapticPolicy
    {
        public const int EventCount = 5;

        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Minimum time between two pulses of the same kind, s (index = HapticEvent).")]
            public float[] minimumInterval = { 0.08f, 0.25f, 0.6f, 0.6f, 4f };
            [Tooltip("Minimum time between any two pulses, s.")]
            public float globalGap = 0.06f;
        }

        readonly Settings _settings;
        readonly float[] _last = new float[EventCount];
        float _lastAny = float.NegativeInfinity;

        public bool Enabled { get; set; } = true;

        public HapticPolicy(Settings settings = null)
        {
            _settings = settings ?? new Settings();
            Reset();
        }

        public void Reset()
        {
            for (int i = 0; i < EventCount; i++) _last[i] = float.NegativeInfinity;
            _lastAny = float.NegativeInfinity;
        }

        /// <summary>True if the pulse should play now (and records it).</summary>
        public bool Allow(HapticEvent haptic, float time, bool deviceSupported)
        {
            if (!Enabled || !deviceSupported) return false;
            int i = (int)haptic;
            if (i < 0 || i >= EventCount) return false;
            float interval = _settings.minimumInterval != null && i < _settings.minimumInterval.Length ? _settings.minimumInterval[i] : 0.25f;
            if (time - _last[i] < interval || time - _lastAny < _settings.globalGap) return false;
            _last[i] = time;
            _lastAny = time;
            return true;
        }
    }

    /// <summary>A device that can play short haptic pulses.</summary>
    public interface IHapticDevice
    {
        bool Supported { get; }
        void Play(HapticEvent haptic);
    }

    /// <summary>No haptics (desktop, WebGL, tests).</summary>
    public sealed class NoHaptics : IHapticDevice
    {
        public static readonly NoHaptics Instance = new NoHaptics();
        public bool Supported => false;
        public void Play(HapticEvent haptic) { }
    }

    /// <summary>
    /// Android haptics through <c>View.performHapticFeedback</c> on the activity's
    /// window: the system's own short effects, which follow the player's system
    /// "touch feedback" setting and need no vibration permission. Calls run on the
    /// Android UI thread. Support is read once from the vibrator service.
    /// Not verified on a device yet (see docs/MOBILE_PERFORMANCE_REPORT.md).
    /// </summary>
    public sealed class AndroidHaptics : IHapticDevice
    {
        // android.view.HapticFeedbackConstants
        const int LongPress = 0;
        const int VirtualKey = 1;
        const int KeyboardTap = 3;
        const int ClockTick = 4;
        const int Confirm = 16;   // API 30
        const int Reject = 17;    // API 30

        readonly bool _supported;
        readonly int _sdk;
#if UNITY_ANDROID && !UNITY_EDITOR
        readonly AndroidJavaObject _activity;
        readonly AndroidJavaRunnable _runnable;
        int _pending;
#endif

        public bool Supported => _supported;

        public AndroidHaptics()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) _sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    _activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using (var vibrator = _activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                    _supported = vibrator != null && vibrator.Call<bool>("hasVibrator");
                _runnable = Perform; // one delegate for the session: no allocation per pulse
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpenSkies] Haptics unavailable: " + e.Message);
                _supported = false;
            }
#endif
        }

        public void Play(HapticEvent haptic)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_supported || _activity == null) return;
            _pending = Constant(haptic, _sdk);
            try
            {
                _activity.Call("runOnUiThread", _runnable);
            }
            catch (Exception)
            {
                // A failed pulse is not worth more than a missing pulse.
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void Perform()
        {
            try
            {
                using (var window = _activity.Call<AndroidJavaObject>("getWindow"))
                using (var view = window.Call<AndroidJavaObject>("getDecorView"))
                    view.Call<bool>("performHapticFeedback", _pending);
            }
            catch (Exception)
            {
            }
        }
#endif

        /// <summary>The system effect for an event, by Android version (CONFIRM and REJECT exist from API 30).</summary>
        public static int Constant(HapticEvent haptic, int sdk)
        {
            switch (haptic)
            {
                case HapticEvent.Selection: return ClockTick;
                case HapticEvent.Confirm: return sdk >= 30 ? Confirm : VirtualKey;
                case HapticEvent.TouchdownSoft: return KeyboardTap;
                case HapticEvent.TouchdownFirm: return LongPress;
                case HapticEvent.Warning: return sdk >= 30 ? Reject : LongPress;
                default: return VirtualKey;
            }
        }
    }

    /// <summary>The player's haptics: policy plus the device for this platform.</summary>
    public sealed class HapticFeedback
    {
        readonly IHapticDevice _device;
        readonly HapticPolicy _policy;

        public HapticFeedback(IHapticDevice device, HapticPolicy policy = null)
        {
            _device = device ?? NoHaptics.Instance;
            _policy = policy ?? new HapticPolicy();
        }

        /// <summary>Android players get system haptics; other platforms none.</summary>
        public static IHapticDevice CreateDevice()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidHaptics();
#else
            return NoHaptics.Instance;
#endif
        }

        public bool Supported => _device.Supported;
        public bool Enabled
        {
            get => _policy.Enabled;
            set => _policy.Enabled = value;
        }
        public HapticPolicy Policy => _policy;
        public int PlayedCount { get; private set; }

        /// <summary>Plays the pulse if the player wants haptics, the device has them and the limits allow it.</summary>
        public bool Play(HapticEvent haptic)
        {
            if (!_policy.Allow(haptic, Time.unscaledTime, _device.Supported)) return false;
            _device.Play(haptic);
            PlayedCount++;
            return true;
        }
    }
}
