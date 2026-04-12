namespace ChillAI.Service.Platform
{
    public interface IWindowService
    {
        void MakeTransparent(float alpha);
        void SetAlwaysOnTop(bool enabled);
        void SetClickThrough(bool enabled);
        (int x, int y) GetWindowPosition();
        void SetWindowPosition(int x, int y);
        (int x, int y, int w, int h) GetWindowBounds();
        void SetWindowBounds(int x, int y, int w, int h);
        (int x, int y) GetCursorScreenPosition();
        int GetDisplayCount();
        void MoveToDisplay(int displayIndex);

        /// <summary>
        /// Identifies the current display + usable resolution + Unity client size + orientation for layout persistence.
        /// </summary>
        string GetUiLayoutContextId();
    }
}
