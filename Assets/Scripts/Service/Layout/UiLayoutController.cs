using System;
using System.Collections.Generic;
using System.IO;
using ChillAI.Core.Layout;
using ChillAI.Service.Platform;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace ChillAI.Service.Layout
{
    /// <summary>
    /// Persists and restores the screen-bound UI layout (panel positions/sizes/visibility,
    /// game window bounds). Plain class — no ITickable, no per-frame cost.
    /// Saves are debounced: multiple RequestSave() calls within MinSaveInterval seconds
    /// collapse into a single write triggered by the last caller via UIElements scheduler.
    /// </summary>
    public sealed class UiLayoutController
    {
        const string FileName = "ui_layout.json";
        const float MinSaveIntervalSeconds = 0.4f;

        readonly IWindowService _windowService;

        UiLayoutFilePayload _file;
        UiLayoutSnapshot _snapshot;
        string _contextId;
        bool _loaded;

        VisualElement _menuWrapper;
        VisualElement _chatRoot;
        VisualElement _taskRoot;
        VisualElement _chatPanel;
        VisualElement _taskPanel;

        float _lastSaveTime = float.MinValue;
        bool _savePending;

        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        [Inject]
        public UiLayoutController(IWindowService windowService)
        {
            _windowService = windowService;
        }

        // ── Registration ──────────────────────────────────────────────────────────

        public void RegisterMenuWrapper(VisualElement wrapper)
        {
            _menuWrapper = wrapper;
            EnsureLoaded();
            ApplyMenuWrapper();
        }

        public void RegisterChatHudRoot(VisualElement root)
        {
            _chatRoot = root;
            EnsureLoaded();
            ApplyChatRoot();
        }

        public void RegisterTaskHudRoot(VisualElement root)
        {
            _taskRoot = root;
            EnsureLoaded();
            ApplyTaskRoot();
        }

        /// <summary>
        /// Register the chat panel. Layout is applied on the next scheduler tick
        /// so that UIElements has already computed geometry before we read it.
        /// </summary>
        public void RegisterChatPanel(VisualElement panel)
        {
            _chatPanel = panel;
            EnsureLoaded();
            // Defer to next frame so element bounds are computed.
            panel.schedule.Execute(() => ApplyChatPanel(panel));
        }

        /// <summary>
        /// Register the task panel. Layout is applied on the next scheduler tick.
        /// </summary>
        public void RegisterTaskPanel(VisualElement panel)
        {
            _taskPanel = panel;
            EnsureLoaded();
            panel.schedule.Execute(() => ApplyTaskPanel(panel));
        }

        public void UnregisterMenuWrapper()   => _menuWrapper = null;
        public void UnregisterChatHudRoot()   => _chatRoot    = null;
        public void UnregisterTaskHudRoot()   => _taskRoot    = null;
        public void UnregisterChatPanel()     => _chatPanel   = null;
        public void UnregisterTaskPanel()     => _taskPanel   = null;

        // ── Context switch ────────────────────────────────────────────────────────

        public void RebindToCurrentContext()
        {
            _loaded    = false;
            _contextId = null;
            EnsureLoaded();
            ApplyMenuWrapper();
            ApplyChatRoot();
            ApplyTaskRoot();
            if (_chatPanel != null) _chatPanel.schedule.Execute(() => ApplyChatPanel(_chatPanel));
            if (_taskPanel != null) _taskPanel.schedule.Execute(() => ApplyTaskPanel(_taskPanel));
        }

        // ── Window bounds ─────────────────────────────────────────────────────────

        public void ApplyGameWindowIfSaved()
        {
            EnsureLoaded();
#if !UNITY_EDITOR
            if (_snapshot.hasWindow && _snapshot.windowW >= 320 && _snapshot.windowH >= 240)
                _windowService.SetWindowBounds(
                    _snapshot.windowX, _snapshot.windowY, _snapshot.windowW, _snapshot.windowH);
#endif
        }

        // ── Save ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Request a debounced save. A scheduler item is posted on any available
        /// registered panel; the actual write happens ≥ MinSaveIntervalSeconds later.
        /// </summary>
        public void RequestSave()
        {
            if (_savePending) return;
            _savePending = true;

            // Use any live panel to post the deferred write via UIElements scheduler.
            var scheduler = (_taskPanel ?? _chatPanel ?? _menuWrapper)?.schedule;
            if (scheduler == null)
            {
                // No panel yet — just write immediately.
                DoSave();
                return;
            }

            long delayMs = (long)(Mathf.Max(0f,
                _lastSaveTime + MinSaveIntervalSeconds - Time.realtimeSinceStartup) * 1000f);

            scheduler.Execute(() =>
            {
                _savePending = false;
                DoSave();
            }).StartingIn(delayMs);
        }

        /// <summary>Flush immediately (e.g. on application quit).</summary>
        public void SaveNow()
        {
            _savePending = false;
            DoSave();
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        void DoSave()
        {
            EnsureLoaded();
            CaptureAll();
            Persist();
            _lastSaveTime = Time.realtimeSinceStartup;
        }

        void EnsureLoaded()
        {
            var id = _windowService.GetUiLayoutContextId();
            if (_loaded && id == _contextId) return;

            _contextId = id;
            _file      = ReadFile();
            _snapshot  = FindEntry(_file, id) ?? new UiLayoutSnapshot();
            _loaded    = true;
        }

        static UiLayoutFilePayload ReadFile()
        {
            try
            {
                if (!File.Exists(FilePath)) return new UiLayoutFilePayload();
                var json = File.ReadAllText(FilePath);
                return JsonUtility.FromJson<UiLayoutFilePayload>(json) ?? new UiLayoutFilePayload();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] ui_layout read failed: {e.Message}");
                return new UiLayoutFilePayload();
            }
        }

        static UiLayoutSnapshot FindEntry(UiLayoutFilePayload file, string contextId)
        {
            if (file.entries == null) return null;
            foreach (var e in file.entries)
                if (e?.contextId == contextId && e.snapshot != null) return e.snapshot;
            return null;
        }

        void Persist()
        {
            UpsertEntry(_file, _contextId, _snapshot);
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(_file, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] ui_layout write failed: {e.Message}");
            }
        }

        static void UpsertEntry(UiLayoutFilePayload file, string contextId, UiLayoutSnapshot snap)
        {
            var list = file.entries != null
                ? new List<UiLayoutContextEntry>(file.entries)
                : new List<UiLayoutContextEntry>();

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i]?.contextId == contextId)
                {
                    list[i].snapshot = snap;
                    file.entries     = list.ToArray();
                    return;
                }
            }
            list.Add(new UiLayoutContextEntry { contextId = contextId, snapshot = snap });
            file.entries = list.ToArray();
        }

        // ── Apply ─────────────────────────────────────────────────────────────────

        void ApplyMenuWrapper()
        {
            if (_menuWrapper == null || _snapshot == null || !_snapshot.hasMenuWrapper) return;
#pragma warning disable CS0618
            _menuWrapper.transform.position = new Vector3(
                _snapshot.menuWrapperX, _snapshot.menuWrapperY, 0f);
#pragma warning restore CS0618
        }

        void ApplyChatRoot()
        {
            if (_chatRoot == null || _snapshot == null || !_snapshot.hasChatRoot) return;
#pragma warning disable CS0618
            _chatRoot.transform.position = new Vector3(
                _snapshot.chatRootX, _snapshot.chatRootY, 0f);
#pragma warning restore CS0618
        }

        void ApplyTaskRoot()
        {
            if (_taskRoot == null || _snapshot == null || !_snapshot.hasTaskRoot) return;
#pragma warning disable CS0618
            _taskRoot.transform.position = new Vector3(
                _snapshot.taskRootX, _snapshot.taskRootY, 0f);
#pragma warning restore CS0618
        }

        void ApplyChatPanel(VisualElement panel)
        {
            if (panel == null || _snapshot == null || !_snapshot.hasChatPanel) return;
            if (!IsValidSize(_snapshot.chatPanelW, _snapshot.chatPanelH)) return;
            PlacePanel(panel, _snapshot.chatPanelLeft, _snapshot.chatPanelTop,
                _snapshot.chatPanelW, _snapshot.chatPanelH);
        }

        void ApplyTaskPanel(VisualElement panel)
        {
            if (panel == null || _snapshot == null || !_snapshot.hasTaskPanel) return;
            if (!IsValidSize(_snapshot.taskPanelW, _snapshot.taskPanelH)) return;
            PlacePanel(panel, _snapshot.taskPanelLeft, _snapshot.taskPanelTop,
                _snapshot.taskPanelW, _snapshot.taskPanelH);
        }

        static bool IsValidSize(float w, float h)
            => !float.IsNaN(w) && !float.IsNaN(h) && w >= 80f && h >= 60f;

        static void PlacePanel(VisualElement panel, float left, float top, float w, float h)
        {
            var parent = panel.parent;
            if (parent == null) return;

            panel.style.position = Position.Absolute;
            panel.style.bottom   = StyleKeyword.Auto;
            panel.style.right    = StyleKeyword.Auto;
            panel.style.left     = left;
            panel.style.top      = top;
            panel.style.width    = w;
            panel.style.height   = h;
        }

        // ── Capture ───────────────────────────────────────────────────────────────

        void CaptureAll()
        {
            if (_snapshot == null) return;

#if !UNITY_EDITOR
            var (wx, wy, ww, wh) = _windowService.GetWindowBounds();
            if (ww >= 320 && wh >= 240)
            {
                _snapshot.hasWindow = true;
                _snapshot.windowX   = wx;
                _snapshot.windowY   = wy;
                _snapshot.windowW   = ww;
                _snapshot.windowH   = wh;
            }
#endif

            CaptureTransform(_menuWrapper, ref _snapshot.hasMenuWrapper,
                ref _snapshot.menuWrapperX, ref _snapshot.menuWrapperY);
            CaptureTransform(_chatRoot, ref _snapshot.hasChatRoot,
                ref _snapshot.chatRootX, ref _snapshot.chatRootY);
            CaptureTransform(_taskRoot, ref _snapshot.hasTaskRoot,
                ref _snapshot.taskRootX, ref _snapshot.taskRootY);

            CapturePanelLayout(_chatPanel,
                ref _snapshot.hasChatPanel,
                ref _snapshot.chatPanelLeft, ref _snapshot.chatPanelTop,
                ref _snapshot.chatPanelW,    ref _snapshot.chatPanelH);

            CapturePanelLayout(_taskPanel,
                ref _snapshot.hasTaskPanel,
                ref _snapshot.taskPanelLeft, ref _snapshot.taskPanelTop,
                ref _snapshot.taskPanelW,    ref _snapshot.taskPanelH);
        }

        static void CaptureTransform(VisualElement el,
            ref bool hasFlag, ref float outX, ref float outY)
        {
            if (el == null) return;
#pragma warning disable CS0618
            var p = el.transform.position;
#pragma warning restore CS0618
            hasFlag = true;
            outX = p.x;
            outY = p.y;
        }

        static void CapturePanelLayout(VisualElement panel,
            ref bool hasFlag,
            ref float outLeft, ref float outTop,
            ref float outW,    ref float outH)
        {
            if (panel == null) return;

            var parent = panel.parent;
            if (parent == null) return;

            // worldBound is zero when element is hidden (display:none). Skip in that case.
            var wb = panel.worldBound;
            if (wb.width < 1f || wb.height < 1f) return;

            // Convert world top-left to parent-local space.
            var tl = parent.WorldToLocal(new Vector2(wb.xMin, wb.yMin));

            outLeft = tl.x;
            outTop  = tl.y;
            outW    = wb.width;
            outH    = wb.height;
            hasFlag = true;
        }
    }
}
