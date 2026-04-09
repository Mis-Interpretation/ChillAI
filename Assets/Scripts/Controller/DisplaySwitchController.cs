using ChillAI.Core.Signals;
using ChillAI.Service.Platform;
using UnityEngine;
using Zenject;

namespace ChillAI.Controller
{
    public class DisplaySwitchController
    {
        readonly IWindowService _windowService;
        readonly SignalBus _signalBus;
        int _currentDisplayIndex;

        public DisplaySwitchController(IWindowService windowService, SignalBus signalBus)
        {
            _windowService = windowService;
            _signalBus = signalBus;
        }

        public void CycleToNextDisplay()
        {
            int count = _windowService.GetDisplayCount();
            if (count <= 1)
            {
                Debug.Log("[ChillAI] Only one display detected, cannot switch.");
                return;
            }

            _currentDisplayIndex = (_currentDisplayIndex + 1) % count;
            int prevW = Screen.width;
            int prevH = Screen.height;
            _windowService.MoveToDisplay(_currentDisplayIndex);
            _signalBus.Fire(new DisplaySwitchedSignal(prevW, prevH));
        }
    }
}
