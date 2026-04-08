using ChillAI.Service.Platform;
using UnityEngine;

namespace ChillAI.Controller
{
    public class DisplaySwitchController
    {
        readonly IWindowService _windowService;
        int _currentDisplayIndex;

        public DisplaySwitchController(IWindowService windowService)
        {
            _windowService = windowService;
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
            _windowService.MoveToDisplay(_currentDisplayIndex);
        }
    }
}
