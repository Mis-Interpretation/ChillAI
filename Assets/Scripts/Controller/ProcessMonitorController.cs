using System;
using System.Text;
using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.BehaviorMapping;
using ChillAI.Model.ProcessMonitor;
using ChillAI.Service.Platform;
using UnityEngine;
using Zenject;

namespace ChillAI.Controller
{
    public class ProcessMonitorController : ITickable
    {
        readonly IProcessMonitorWriter _processMonitor;
        readonly IBehaviorMappingReader _behaviorMapping;
        readonly AppSettings _appSettings;
        readonly IConfigReader _configReader;
        readonly SignalBus _signalBus;

        float _timeSinceLastScan;
        string _lastProcessName;

        public ProcessMonitorController(
            IProcessMonitorWriter processMonitor,
            IBehaviorMappingReader behaviorMapping,
            AppSettings appSettings,
            IConfigReader configReader,
            SignalBus signalBus)
        {
            _processMonitor = processMonitor;
            _behaviorMapping = behaviorMapping;
            _appSettings = appSettings;
            _configReader = configReader;
            _signalBus = signalBus;

            // Trigger first scan immediately
            _timeSinceLastScan = float.MaxValue;
        }

        float ScanInterval =>
            _configReader.GetEffectiveFloat(
                _configReader.Config.processScanIntervalOverride,
                _appSettings.processScanInterval);

        public void Tick()
        {
            _timeSinceLastScan += Time.unscaledDeltaTime;

            if (_timeSinceLastScan < ScanInterval)
                return;

            _timeSinceLastScan = 0f;
            ScanForegroundProcess();
        }

        void ScanForegroundProcess()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                var foregroundHwnd = Win32Interop.GetForegroundWindow();
                if (foregroundHwnd == IntPtr.Zero) return;

                Win32Interop.GetWindowThreadProcessId(foregroundHwnd, out uint pid);
                if (pid == 0) return;

                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                var processName = process.ProcessName;

                // Skip if same process as last scan
                if (processName == _lastProcessName) return;
                _lastProcessName = processName;

                // Get window title
                var titleLength = Win32Interop.GetWindowTextLength(foregroundHwnd);
                var titleBuilder = new StringBuilder(titleLength + 1);
                Win32Interop.GetWindowText(foregroundHwnd, titleBuilder, titleBuilder.Capacity);
                var windowTitle = titleBuilder.ToString();

                // Whitelist check (privacy filter)
                if (!_behaviorMapping.IsWhitelisted(processName))
                    return;

                var category = _behaviorMapping.GetCategory(processName);
                var info = new ProcessInfo(processName, windowTitle, category);

                _processMonitor.UpdateCurrentProcess(info);
                _signalBus.Fire(new ActiveProcessChangedSignal(processName, windowTitle, category));

                Debug.Log($"[ChillAI] Detected: {info}");
            }
            catch (ArgumentException)
            {
                // Process exited between GetForegroundWindow and GetProcessById, safe to ignore
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Process scan error: {e.Message}");
            }
#endif
        }
    }
}
