using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.UserProfile;
using ChillAI.Service.Layout;
using ChillAI.View.Window;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace ChillAI.View.Profile
{
    [RequireComponent(typeof(UIDocument))]
    public class ProfileInsightPanelView : MonoBehaviour
    {
        const string HiddenClass = "hidden";
        const string EmptyAnswerText = "暂未了解";

        SignalBus _signalBus;
        IProfileReader _profileReader;
        UserSettingsService _userSettings;
        UiLayoutController _uiLayout;

        VisualElement _panel;
        Button _closeBtn;
        Label _summaryLabel;
        ScrollView _qaScroll;
        VisualElement _resizeHandle;

        bool _panelVisible;
        WindowDragManipulator _dragManipulator;
        PanelResizeManipulator _resizeManipulator;

        [Inject]
        public void Construct(
            SignalBus signalBus,
            IProfileReader profileReader,
            UserSettingsService userSettings,
            UiLayoutController uiLayout)
        {
            _signalBus = signalBus;
            _profileReader = profileReader;
            _userSettings = userSettings;
            _uiLayout = uiLayout;
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _panel = root.Q<VisualElement>("profile-panel");
            _closeBtn = root.Q<Button>("profile-close-btn");
            _summaryLabel = root.Q<Label>("profile-summary-label");
            _qaScroll = root.Q<ScrollView>("profile-qa-scroll");
            _resizeHandle = root.Q<VisualElement>("profile-resize-handle");

            _uiLayout.RegisterProfileHudRoot(root);
            _uiLayout.RegisterProfilePanel(_panel);

            _panelVisible = !_panel.ClassListContains(HiddenClass);
            _closeBtn.clicked += OnClose;

            var header = root.Q<VisualElement>(className: "profile-panel-header");
            _dragManipulator = new WindowDragManipulator(_panel);
            _dragManipulator.DragEnded += OnHudLayoutChanged;
            header.AddManipulator(_dragManipulator);

            if (_resizeHandle != null)
            {
                var configuredMinWidth = _userSettings.Data.profilePanelMinWidth > 0
                    ? _userSettings.Data.profilePanelMinWidth
                    : 320;
                var configuredMinHeight = _userSettings.Data.profilePanelMinHeight > 0
                    ? _userSettings.Data.profilePanelMinHeight
                    : 220;
                var minWidth = Mathf.Max(200, configuredMinWidth);
                var minHeight = Mathf.Max(140, configuredMinHeight);
                _resizeManipulator = new PanelResizeManipulator(_panel, minWidth, minHeight, OnHudLayoutChanged);
                _resizeHandle.AddManipulator(_resizeManipulator);
            }

            _signalBus?.Subscribe<ToggleProfileInsightPanelSignal>(OnTogglePanelSignal);
            _signalBus?.Subscribe<ProfileUpdatedSignal>(OnProfileUpdated);

            RefreshContent();
        }

        void OnDisable()
        {
            _closeBtn.clicked -= OnClose;

            var header = _panel?.Q<VisualElement>(className: "profile-panel-header");
            if (_dragManipulator != null)
            {
                header?.RemoveManipulator(_dragManipulator);
                _dragManipulator.DragEnded -= OnHudLayoutChanged;
            }

            if (_resizeManipulator != null && _resizeHandle != null)
                _resizeHandle.RemoveManipulator(_resizeManipulator);

            _uiLayout?.UnregisterProfileHudRoot();
            _uiLayout?.UnregisterProfilePanel();

            _signalBus?.TryUnsubscribe<ToggleProfileInsightPanelSignal>(OnTogglePanelSignal);
            _signalBus?.TryUnsubscribe<ProfileUpdatedSignal>(OnProfileUpdated);
        }

        void OnTogglePanelSignal()
        {
            SetPanelVisible(!_panelVisible);
        }

        void OnClose()
        {
            SetPanelVisible(false);
        }

        void SetPanelVisible(bool visible)
        {
            _panelVisible = visible;
            _panel.EnableInClassList(HiddenClass, !visible);
            if (visible)
                RefreshContent();
            _uiLayout?.RequestSave();
        }

        void OnProfileUpdated(ProfileUpdatedSignal _)
        {
            RefreshContent();
        }

        void OnHudLayoutChanged()
        {
            _uiLayout?.RequestSave();
        }

        void RefreshContent()
        {
            var answered = _profileReader.AnsweredCount;
            var total = ProfileQuestions.TotalCount;
            var runText = _profileReader.HasEverRun ? $"最近更新：{_profileReader.LastRunTime}" : "尚未运行";
            _summaryLabel.text = $"已了解 {answered}/{total} | {runText}";

            _qaScroll.Clear();

            var snapshots = _profileReader.GetSectionSnapshots();
            for (int i = 0; i < snapshots.Count; i++)
            {
                var sectionSnapshot = snapshots[i];
                var card = new VisualElement();
                card.AddToClassList("profile-section-card");

                var sectionTitle = new Label(sectionSnapshot.Section.title);
                sectionTitle.AddToClassList("profile-section-title");
                card.Add(sectionTitle);

                if (!string.IsNullOrWhiteSpace(sectionSnapshot.Section.description))
                {
                    var sectionDesc = new Label(sectionSnapshot.Section.description);
                    sectionDesc.AddToClassList("profile-section-desc");
                    card.Add(sectionDesc);
                }

                for (int j = 0; j < sectionSnapshot.Entries.Count; j++)
                {
                    var entry = sectionSnapshot.Entries[j];
                    card.Add(CreateQuestionRow(entry));
                }

                _qaScroll.Add(card);
            }
        }

        static VisualElement CreateQuestionRow(ProfileQuestionAnswer entry)
        {
            var row = new VisualElement();
            row.AddToClassList("profile-qa-row");

            var questionLabel = new Label(entry.Question.label);
            questionLabel.AddToClassList("profile-question-label");
            row.Add(questionLabel);

            var answer = entry.Answer;
            var hasAnswer = answer != null && !string.IsNullOrWhiteSpace(answer.answer);
            var answerText = new Label(hasAnswer ? answer.answer.Trim() : EmptyAnswerText);
            answerText.AddToClassList("profile-answer-text");
            if (!hasAnswer)
                answerText.AddToClassList("profile-answer-text--empty");
            row.Add(answerText);

            return row;
        }
    }
}
