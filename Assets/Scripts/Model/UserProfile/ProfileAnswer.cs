using System;

namespace ChillAI.Model.UserProfile
{
    [Serializable]
    public class ProfileAnswer
    {
        public string questionId;
        public string answer;
        public float confidence;
        public string updatedAt;

        public ProfileAnswer() { }

        public ProfileAnswer(string questionId, string answer, float confidence, string updatedAt)
        {
            this.questionId = questionId;
            this.answer = answer;
            this.confidence = confidence;
            this.updatedAt = updatedAt;
        }
    }
}
