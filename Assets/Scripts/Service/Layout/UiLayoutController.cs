using System;
using System.Collections.Generic;
using System.IO;
using ChillAI.Core.Layout;
using ChillAI.Model.TaskDecomposition;
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
        VisualElement _profileRoot;
        VisualElement _chatHistoryRoot;
        VisualElement _chatPanel;
        VisualElement _taskPanel;
        VisualElement _profilePanel;
        VisualElement _chatHistoryPanel;

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

        public void RegisterProfileHudRoot(VisualElement root)
        {
            _profileRoot = root;
            EnsureLoaded();
            ApplyProfileRoot();
        }

        public void RegisterChatHistoryHudRoot(VisualElement root)
        {
            _chatHistoryRoot = root;
            EnsureLoaded();
            ApplyChatHistoryRoot();
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

        public void RegisterProfilePanel(VisualElement panel)
        {
            _profilePanel = panel;
            EnsureLoaded();
            panel.schedule.Execute(() => ApplyProfilePanel(panel));
        }

        public void RegisterChatHistoryPanel(VisualElement panel)
        {
            _chatHistoryPanel = panel;
            EnsureLoaded();
            panel.schedule.Execute(() => ApplyChatHistoryPanel(panel));
        }

        public void UnregisterMenuWrapper()   => _menuWrapper = null;
        public void UnregisterChatHudRoot()   => _chatRoot    = null;
        public void UnregisterTaskHudRoot()   => _taskRoot    = null;
        public void UnregisterProfileHudRoot() => _profileRoot = null;
        public void UnregisterChatHistoryHudRoot() => _chatHistoryRoot = null;
        public void UnregisterChatPanel()     => _chatPanel   = null;
        public void UnregisterTaskPanel()     => _taskPanel   = null;
        public void UnregisterProfilePanel()  => _profilePanel = null;
        public void UnregisterChatHistoryPanel() => _chatHistoryPanel = null;

        // ── Column ratio ──────────────────────────────────────────────────────────

        public void SetTaskColLeftRatio(float ratio)
        {
            EnsureLoaded();
            _snapshot.hasTaskColLeftRatio = true;
            _snapshot.taskColLeftRatio    = R(Mathf.Clamp01(ratio));
        }

        public bool TryGetTaskColLeftRatio(out float ratio)
        {
            EnsureLoaded();
            if (_snapshot.hasTaskColLeftRatio && _snapshot.taskColLeftRatio > 0f)
            {
                ratio = _snapshot.taskColLeftRatio;
                return true;
            }
            ratio = 0f;
            return false;
        }

        // ── Selected task category tab ────────────────────────────────────────────

        public void SetTaskSelectedCategory(TaskCategory category)
        {
            EnsureLoaded();
            _snapshot.hasTaskSelectedCategory = true;
            _snapshot.taskSelectedCategory = (int)category;
        }

        public bool TryGetTaskSelectedCategory(out TaskCategory category)
        {
            EnsureLoaded();
            if (_snapshot.hasTaskSelectedCategory)
            {
                category = (TaskCategory)_snapshot.taskSelectedCategory;
                return true;
            }
            category = TaskCategory.Doing;
            return false;
        }

        // ── Context switch ────────────────────────────────────────────────────────

        public void RebindToCurrentContext()
        {
            _loaded    = false;
            _contextId = null;
            EnsureLoaded();
            ApplyMenuWrapper();
            ApplyChatRoot();
            ApplyTaskRoot();
            ApplyProfileRoot();
            ApplyChatHistoryRoot();
            if (_chatPanel != null) _chatPanel.schedule.Execute(() => ApplyChatPanel(_chatPanel));
            if (_taskPanel != null) _taskPanel.schedule.Execute(() => ApplyTaskPanel(_taskPanel));
            if (_profilePanel != null) _profilePanel.schedule.Execute(() => ApplyProfilePanel(_profilePanel));
            if (_chatHistoryPanel != null) _chatHistoryPanel.schedule.Execute(() => ApplyChatHistoryPanel(_chatHistoryPanel));
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
            var scheduler = (_chatHistoryPanel ?? _profilePanel ?? _taskPanel ?? _chatPanel ?? _menuWrapper)?.schedule;
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
            NormalizeHudRootPositions(_snapshot);
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

        void ApplyProfileRoot()
        {
            if (_profileRoot == null || _snapshot == null || !_snapshot.hasProfileRoot) return;
#pragma warning disable CS0618
            _profileRoot.transform.position = new Vector3(
                _snapshot.profileRootX, _snapshot.profileRootY, 0f);
#pragma warning restore CS0618
        }

        void ApplyChatHistoryRoot()
        {
            if (_chatHistoryRoot == null || _snapshot == null || !_snapshot.hasChatHistoryRoot) return;
#pragma warning disable CS0618
            _chatHistoryRoot.transform.position = new Vector3(
                _snapshot.chatHistoryRootX, _snapshot.chatHistoryRootY, 0f);
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

        void ApplyProfilePanel(VisualElement panel)
        {
            if (panel == null || _snapshot == null || !_snapshot.hasProfilePanel) return;
            if (!IsValidSize(_snapshot.profilePanelW, _snapshot.profilePanelH)) return;
            PlacePanel(panel, _snapshot.profilePanelLeft, _snapshot.profilePanelTop,
                _snapshot.profilePanelW, _snapshot.profilePanelH);
        }

        void ApplyChatHistoryPanel(VisualElement panel)
        {
            if (panel == null || _snapshot == null || !_snapshot.hasChatHistoryPanel) return;
            if (!IsValidSize(_snapshot.chatHistoryPanelW, _snapshot.chatHistoryPanelH)) return;
            PlacePanel(panel, _snapshot.chatHistoryPanelLeft, _snapshot.chatHistoryPanelTop,
                _snapshot.chatHistoryPanelW, _snapshot.chatHistoryPanelH);
        }

        static bool IsValidSize(float w, float h)
            => !float.IsNaN(w) && !float.IsNaN(h) && w >= 80f && h >= 60f;

        static void PlacePanel(VisualElement panel, float left, float top, float w, float h)
        {
            var parent = panel.parent;
            if (parent == null) return;

            // Persisted panel coordinates are root-relative. Clear any author-time
            // translate offset (e.g. UXML "translate: 0 -25px") so it doesn't
            // get re-applied on every launch and cause cumulative drift.
            panel.style.translate = new StyleTranslate(new Translate(0f, 0f));
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
            CaptureTransform(_profileRoot, ref _snapshot.hasProfileRoot,
                ref _snapshot.profileRootX, ref _snapshot.profileRootY);
            CaptureTransform(_chatHistoryRoot, ref _snapshot.hasChatHistoryRoot,
                ref _snapshot.chatHistoryRootX, ref _snapshot.chatHistoryRootY);
            NormalizeHudRootPositions(_snapshot);

            CapturePanelLayout(_chatPanel,
                ref _snapshot.hasChatPanel,
                ref _snapshot.chatPanelLeft, ref _snapshot.chatPanelTop,
                ref _snapshot.chatPanelW,    ref _snapshot.chatPanelH);

            CapturePanelLayout(_taskPanel,
                ref _snapshot.hasTaskPanel,
                ref _snapshot.taskPanelLeft, ref _snapshot.taskPanelTop,
                ref _snapshot.taskPanelW,    ref _snapshot.taskPanelH);

            CapturePanelLayout(_profilePanel,
                ref _snapshot.hasProfilePanel,
                ref _snapshot.profilePanelLeft, ref _snapshot.profilePanelTop,
                ref _snapshot.profilePanelW,    ref _snapshot.profilePanelH);

            CapturePanelLayout(_chatHistoryPanel,
                ref _snapshot.hasChatHistoryPanel,
                ref _snapshot.chatHistoryPanelLeft, ref _snapshot.chatHistoryPanelTop,
                ref _snapshot.chatHistoryPanelW,    ref _snapshot.chatHistoryPanelH);
        }

        static void CaptureTransform(VisualElement el,
            ref bool hasFlag, ref float outX, ref float outY)
        {
            if (el == null) return;
#pragma warning disable CS0618
            var p = el.transform.position;
#pragma warning restore CS0618
            hasFlag = true;
            outX = R(p.x);
            outY = R(p.y);
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

            outLeft = R(tl.x);
            outTop  = R(tl.y);
            outW    = R(wb.width);
            outH    = R(wb.height);
            hasFlag = true;
        }

        static void NormalizeHudRootPositions(UiLayoutSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (!TryGetSharedHudRootPosition(snapshot, out var sharedX, out var sharedY)) return;

            sharedX = R(sharedX);
            sharedY = R(sharedY);

            snapshot.hasMenuWrapper = true;
            snapshot.menuWrapperX = sharedX;
            snapshot.menuWrapperY = sharedY;

            snapshot.hasChatRoot = true;
            snapshot.chatRootX = sharedX;
            snapshot.chatRootY = sharedY;

            snapshot.hasTaskRoot = true;
            snapshot.taskRootX = sharedX;
            snapshot.taskRootY = sharedY;

            if (snapshot.hasProfileRoot)
            {
                snapshot.profileRootX = sharedX;
                snapshot.profileRootY = sharedY;
            }

            if (snapshot.hasChatHistoryRoot)
            {
                snapshot.chatHistoryRootX = sharedX;
                snapshot.chatHistoryRootY = sharedY;
            }
        }

        static bool TryGetSharedHudRootPosition(UiLayoutSnapshot snapshot, out float x, out float y)
        {
            if (snapshot.hasChatRoot && snapshot.hasTaskRoot &&
                Mathf.Abs(snapshot.chatRootX - snapshot.taskRootX) <= 1f &&
                Mathf.Abs(snapshot.chatRootY - snapshot.taskRootY) <= 1f)
            {
                x = (snapshot.chatRootX + snapshot.taskRootX) * 0.5f;
                y = (snapshot.chatRootY + snapshot.taskRootY) * 0.5f;
                return true;
            }

            if (snapshot.hasMenuWrapper)
            {
                x = snapshot.menuWrapperX;
                y = snapshot.menuWrapperY;
                return true;
            }

            if (snapshot.hasChatRoot)
            {
                x = snapshot.chatRootX;
                y = snapshot.chatRootY;
                return true;
            }

            if (snapshot.hasTaskRoot)
            {
                x = snapshot.taskRootX;
                y = snapshot.taskRootY;
                return true;
            }

            if (snapshot.hasProfileRoot)
            {
                x = snapshot.profileRootX;
                y = snapshot.profileRootY;
                return true;
            }

            if (snapshot.hasChatHistoryRoot)
            {
                x = snapshot.chatHistoryRootX;
                y = snapshot.chatHistoryRootY;
                return true;
            }

            x = 0f;
            y = 0f;
            return false;
        }

        static float R(float v) => (float)Math.Round(v, 3);
    }
}
