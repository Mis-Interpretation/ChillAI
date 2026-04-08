namespace ChillAI.Service.Platform
{
    public interface IWindowService
    {
        void MakeTransparent(float alpha);
        void SetAlwaysOnTop(bool enabled);
        void SetClickThrough(bool enabled);
    }
}
