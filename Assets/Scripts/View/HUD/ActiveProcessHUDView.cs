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
        [Tooltip("If set, this RectTransform moves with the UI Toolkit HUD when dragging the system menu (e.g. a parent group). Otherwise uses processText.rectTransform.")]
        [SerializeField] RectTransform menuDragCompanionOverride;

        SignalBus _signalBus;

        public RectTransform MenuDragCompanionRect =>
            menuDragCompanionOverride != null
                ? menuDragCompanionOverride
                : (processText != null ? processText.rectTransform : null);

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
