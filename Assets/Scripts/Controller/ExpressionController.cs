using System;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.Expression;
using UnityEngine;
using Zenject;

namespace ChillAI.Controller
{
    public class ExpressionController : IInitializable, IDisposable
    {
        readonly IExpressionStateWriter _expressionState;
        readonly ExpressionMappingData _mappingData;
        readonly SignalBus _signalBus;

        public ExpressionController(
            IExpressionStateWriter expressionState,
            ExpressionMappingData mappingData,
            SignalBus signalBus)
        {
            _expressionState = expressionState;
            _mappingData = mappingData;
            _signalBus = signalBus;
        }

        public void Initialize()
        {
            _signalBus.Subscribe<ActiveProcessChangedSignal>(OnActiveProcessChanged);
        }

        public void Dispose()
        {
            _signalBus.TryUnsubscribe<ActiveProcessChangedSignal>(OnActiveProcessChanged);
        }

        void OnActiveProcessChanged(ActiveProcessChangedSignal signal)
        {
            var expression = _mappingData.GetExpression(signal.Category);
            _expressionState.SetExpression(expression);
            _signalBus.Fire(new ExpressionChangedSignal(expression));

            Debug.Log($"[ChillAI] Expression: {signal.Category} -> {expression}");
        }
    }
}
