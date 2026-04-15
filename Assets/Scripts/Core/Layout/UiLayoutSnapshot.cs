using System;

namespace ChillAI.Core.Layout
{
    [Serializable]
    public class UiLayoutSnapshot
    {
        public bool hasWindow;
        public int windowX;
        public int windowY;
        public int windowW;
        public int windowH;

        public bool hasMenuWrapper;
        public float menuWrapperX;
        public float menuWrapperY;

        public bool hasChatRoot;
        public float chatRootX;
        public float chatRootY;

        public bool hasTaskRoot;
        public float taskRootX;
        public float taskRootY;

        public bool hasProfileRoot;
        public float profileRootX;
        public float profileRootY;

        public bool hasChatHistoryRoot;
        public float chatHistoryRootX;
        public float chatHistoryRootY;

        public bool hasChatPanel;
        public float chatPanelLeft;
        public float chatPanelTop;
        public float chatPanelW;
        public float chatPanelH;

        public bool hasTaskPanel;
        public float taskPanelLeft;
        public float taskPanelTop;
        public float taskPanelW;
        public float taskPanelH;

        public bool hasProfilePanel;
        public float profilePanelLeft;
        public float profilePanelTop;
        public float profilePanelW;
        public float profilePanelH;

        public bool hasChatHistoryPanel;
        public float chatHistoryPanelLeft;
        public float chatHistoryPanelTop;
        public float chatHistoryPanelW;
        public float chatHistoryPanelH;

        public bool hasTaskColLeftRatio;
        public float taskColLeftRatio;
    }

    [Serializable]
    public sealed class UiLayoutContextEntry
    {
        public string contextId;
        public UiLayoutSnapshot snapshot;
    }

    [Serializable]
    public class UiLayoutFilePayload
    {
        public int version = 1;
        public UiLayoutContextEntry[] entries = Array.Empty<UiLayoutContextEntry>();
    }
}
