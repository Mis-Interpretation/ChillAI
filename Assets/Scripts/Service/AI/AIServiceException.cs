using System;

namespace ChillAI.Service.AI
{
    /// <summary>
    /// User-friendly AI service errors that can be displayed directly in the UI.
    /// </summary>
    public class AIServiceException : Exception
    {
        public AIServiceException(string message) : base(message) { }
        public AIServiceException(string message, Exception inner) : base(message, inner) { }
    }
}
