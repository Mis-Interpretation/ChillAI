using System;
using ChillAI.Core;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.ProcessMonitor;
using ChillAI.Model.UsageTracking;
using UnityEngine;
using Zenject;

namespace ChillAI.Controller
{
    public class UsageTrackingController : ITickable, IInitializable, IDisposable
    {
        readonly IUsageTrackingWriter _usageTracking;
        readonly IProcessMonitorReader _processMonitor;
        readonly AppSettings _appSettings;
        readonly SignalBus _signalBus;

        string _currentProcessName;
        SoftwareCategory _currentCategory;
        float _autoSaveTimer;

        public UsageTrackingController(
            IUsageTrackingWriter usageTracking,
            IProcessMonitorReader processMonitor,
            AppSettings appSettings,
            SignalBus signalBus)
        {
            _usageTracking = usageTracking;
            _processMonitor = processMonitor;
            _appSettings = appSettings;
            _signalBus = signalBus;
        }

        public void Initialize()
        {
            _signalBus.Subscribe<ActiveProcessChangedSignal>(OnActiveProcessChanged);
            Application.quitting += OnQuitting;

            // Seed from current process if already detected before this controller initialized
            var current = _processMonitor.CurrentProcess;
            if (current != null)
            {
                _currentProcessName = current.ProcessName;
                _currentCategory = current.Category;
            }
        }

        public void Dispose()
        {
            _signalBus.TryUnsubscribe<ActiveProcessChangedSignal>(OnActiveProcessChanged);
            Application.quitting -= OnQuitting;
        }

        public void Tick()
        {
            if (!string.IsNullOrEmpty(_currentProcessName))
                _usageTracking.AddUsage(_currentProcessName, _currentCategory, Time.unscaledDeltaTime);

            _autoSaveTimer += Time.unscaledDeltaTime;
            if (_autoSaveTimer >= _appSettings.usageAutoSaveIntervalMinutes * 60f)
            {
                _autoSaveTimer = 0f;
                _usageTracking.Save();
            }
        }

        void OnActiveProcessChanged(ActiveProcessChangedSignal signal)
        {
            _currentProcessName = signal.ProcessName;
            _currentCategory = signal.Category;
        }

        void OnQuitting()
        {
            _usageTracking.Save();
            Debug.Log("[ChillAI] Usage data saved on application quit.");
        }
    }
}
