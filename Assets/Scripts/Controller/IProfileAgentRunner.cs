using System.Collections.Generic;
using ChillAI.Model.UserProfile;

namespace ChillAI.Controller
{
    public interface IProfileAgentRunner
    {
        void RunProfileUpdateForTiers(IReadOnlyList<ProfileTier> tiers);
    }
}
