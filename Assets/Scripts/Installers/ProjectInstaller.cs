using ChillAI.Controller;
using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.BehaviorMapping;
using ChillAI.Model.ChatHistory;
using ChillAI.Model.Expression;
using ChillAI.Model.ProcessMonitor;
using ChillAI.Model.Quest;
using ChillAI.Model.TaskArchive;
using ChillAI.Model.TaskDecomposition;
using ChillAI.Model.UsageTracking;
using ChillAI.Model.UserProfile;
using ChillAI.Service.AI;
using ChillAI.Service.EmojiFilter;
using ChillAI.Service.Layout;
using ChillAI.Service.Platform;
using ChillAI.Service.Quest;
using UnityEngine;
using Zenject;

namespace ChillAI.Installers
{
    public class ProjectInstaller : MonoInstaller
    {
        [Header("Settings")]
        [SerializeField] AppSettings appSettings;
        [SerializeField] UserSettingsDefaults userSettingsDefaults;
        [SerializeField] ExpressionMappingData expressionMappingData;
        [SerializeField] BehaviorMappingData behaviorMappingData;

        [Header("Agent System")]
        [SerializeField] AgentRegistry agentRegistry;

        [Header("Emoji Filter")]
        [SerializeField] EmojiPaletteData emojiPaletteData;

        [Header("Quest")]
        [SerializeField] QuestDatabase questDatabase;

        public override void InstallBindings()
        {
            // Signal system
            SignalBusInstaller.Install(Container);
            Container.DeclareSignal<ActiveProcessChangedSignal>().OptionalSubscriber();
            Container.DeclareSignal<ExpressionChangedSignal>().OptionalSubscriber();
            Container.DeclareSignal<TaskDecompositionResultSignal>().OptionalSubscriber();
            Container.DeclareSignal<EmojiChatResponseSignal>().OptionalSubscriber();
            Container.DeclareSignal<BigEventChangedSignal>().OptionalSubscriber();
            Container.DeclareSignal<SubTaskCompletionChangedSignal>().OptionalSubscriber();
            Container.DeclareSignal<TaskAddedViaChatSignal>().OptionalSubscriber();
            Container.DeclareSignal<QuestCheckRequestedSignal>().OptionalSubscriber();
            Container.DeclareSignal<QuestProgressChangedSignal>().OptionalSubscriber();
            Container.DeclareSignal<UnlockEmojiListRequestedSignal>().OptionalSubscriber();
            Container.DeclareSignal<TriggerProfileAgentRequestedSignal>().OptionalSubscriber();
            Container.DeclareSignal<DisplaySwitchedSignal>().OptionalSubscriber();
            Container.DeclareSignal<ProfileUpdatedSignal>().OptionalSubscriber();
            Container.DeclareSignal<ToggleProfileInsightPanelSignal>().OptionalSubscriber();
            Container.DeclareSignal<ToggleChatHistoryPanelSignal>().OptionalSubscriber();
            Container.DeclareSignal<ChatInputFocusSignal>().OptionalSubscriber();

            // ScriptableObject settings (injected as instances)
            Container.BindInstance(appSettings);
            Container.BindInstance(userSettingsDefaults);
            Container.BindInstance(expressionMappingData);
            Container.BindInstance(behaviorMappingData);
            Container.BindInstance(agentRegistry);
            Container.BindInstance(emojiPaletteData);
            Container.BindInstance(questDatabase);

            // Persistent user settings (JSON-backed)
            Container.Bind<UserSettingsService>().AsSingle().NonLazy();

            // Config
            Container.Bind(typeof(IConfigReader), typeof(IConfigWriter))
                .To<ConfigService>().AsSingle().NonLazy();

            // Models
            Container.Bind(typeof(IProcessMonitorReader), typeof(IProcessMonitorWriter))
                .To<ProcessMonitorModel>().AsSingle();
            Container.Bind<IBehaviorMappingReader>()
                .To<BehaviorMappingModel>().AsSingle();
            Container.Bind(typeof(IExpressionStateReader), typeof(IExpressionStateWriter))
                .To<ExpressionStateModel>().AsSingle();
            Container.Bind(typeof(ITaskDecompositionReader), typeof(ITaskDecompositionWriter))
                .To<TaskDecompositionModel>().AsSingle();
            Container.Bind<ITaskArchiveStore>()
                .To<TaskArchiveModel>().AsSingle();
            Container.Bind<IQuestRuntimeStore>()
                .To<QuestRuntimeModel>().AsSingle();
            Container.Bind(typeof(IUsageTrackingReader), typeof(IUsageTrackingWriter))
                .To<UsageTrackingModel>().AsSingle();
            Container.Bind(typeof(IChatHistoryReader), typeof(IChatHistoryWriter))
                .To<ChatHistoryModel>().AsSingle();
            Container.Bind(typeof(IProfileReader), typeof(IProfileWriter))
                .To<ProfileModel>().AsSingle();

            // Services
            Container.Bind<IWindowService>()
                .To<Win32WindowService>().AsSingle();
            Container.Bind<UiLayoutController>().AsSingle();
            Container.Bind<IAIService>()
                .To<OpenAIService>().AsSingle();
            Container.Bind<IEmojiFilterService>()
                .To<EmojiFilterService>().AsSingle();
            Container.Bind<QuestRuleConditionEvaluator>().AsSingle();
            Container.Bind<QuestAgentEvaluator>().AsSingle();
            Container.Bind<QuestConditionEvaluator>().AsSingle();
            Container.Bind<QuestActionExecutor>().AsSingle();

            // Controllers
            Container.BindInterfacesAndSelfTo<ProcessMonitorController>().AsSingle();
            Container.BindInterfacesAndSelfTo<ExpressionController>().AsSingle();
            Container.Bind<TaskDecompositionController>().AsSingle();
            Container.Bind<EmojiChatController>().AsSingle();
            Container.BindInterfacesAndSelfTo<UsageTrackingController>().AsSingle();
            Container.BindInterfacesAndSelfTo<ProfileController>().AsSingle();
            Container.Bind<DisplaySwitchController>().AsSingle();
            Container.Bind<QuestController>().AsSingle().NonLazy();
        }
    }
}
