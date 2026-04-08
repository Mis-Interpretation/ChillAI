using System;
using ChillAI.Core.Signals;
using TMPro;
using UnityEngine;
using Zenject;

namespace ChillAI.View.HUD
{
    public class ActiveProcessHUDView : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI processText;

        SignalBus _signalBus;

        [Inject]
        public void Construct(SignalBus signalBus)
        {
            _signalBus = signalBus;
        }

        void OnEnable()
        {
            _signalBus?.Subscribe<ActiveProcessChangedSignal>(OnProcessChanged);
        }

        void OnDisable()
        {
            _signalBus?.TryUnsubscribe<ActiveProcessChangedSignal>(OnProcessChanged);
        }

        void OnProcessChanged(ActiveProcessChangedSignal signal)
        {
            if (processText != null)
                processText.text = $"{signal.ProcessName} - {signal.Category}";
        }
    }
}
