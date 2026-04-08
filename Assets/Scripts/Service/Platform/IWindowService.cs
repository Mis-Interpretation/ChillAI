namespace ChillAI.Service.Platform
{
    public interface IWindowService
    {
        void MakeTransparent(float alpha);
        void SetAlwaysOnTop(bool enabled);
        void SetClickThrough(bool enabled);
        (int x, int y) GetWindowPosition();
        void SetWindowPosition(int x, int y);
        (int x, int y) GetCursorScreenPosition();
    }
}
