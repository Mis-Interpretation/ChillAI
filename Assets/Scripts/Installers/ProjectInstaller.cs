using ChillAI.Controller;
using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.BehaviorMapping;
using ChillAI.Model.ChatHistory;
using ChillAI.Model.Expression;
using ChillAI.Model.ProcessMonitor;
using ChillAI.Model.TaskDecomposition;
using ChillAI.Model.UsageTracking;
using ChillAI.Service.AI;
using ChillAI.Service.Platform;
using UnityEngine;
using Zenject;

namespace ChillAI.Installers
{
    public class ProjectInstaller : MonoInstaller
    {
        [Header("Settings")]
        [SerializeField] AppSettings appSettings;
        [SerializeField] ExpressionMappingData expressionMappingData;
        [SerializeField] BehaviorMappingData behaviorMappingData;

        [Header("Agent System")]
        [SerializeField] AgentRegistry agentRegistry;

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
            Container.DeclareSignal<DisplaySwitchedSignal>().OptionalSubscriber();

            // ScriptableObject settings (injected as instances)
            Container.BindInstance(appSettings);
            Container.BindInstance(expressionMappingData);
            Container.BindInstance(behaviorMappingData);
            Container.BindInstance(agentRegistry);

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
            Container.Bind(typeof(IUsageTrackingReader), typeof(IUsageTrackingWriter))
                .To<UsageTrackingModel>().AsSingle();
            Container.Bind(typeof(IChatHistoryReader), typeof(IChatHistoryWriter))
                .To<ChatHistoryModel>().AsSingle();

            // Services
            Container.Bind<IWindowService>()
                .To<Win32WindowService>().AsSingle();
            Container.Bind<IAIService>()
                .To<OpenAIService>().AsSingle();

            // Controllers
            Container.BindInterfacesAndSelfTo<ProcessMonitorController>().AsSingle();
            Container.BindInterfacesAndSelfTo<ExpressionController>().AsSingle();
            Container.Bind<TaskDecompositionController>().AsSingle();
            Container.Bind<EmojiChatController>().AsSingle();
            Container.BindInterfacesAndSelfTo<UsageTrackingController>().AsSingle();
            Container.Bind<DisplaySwitchController>().AsSingle();
        }
    }
}
