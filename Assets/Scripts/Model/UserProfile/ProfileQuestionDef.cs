using System;
using System.Collections.Generic;

namespace ChillAI.Model.UserProfile
{
    public enum ProfileTier
    {
        Identity = 1,
        Preferences = 2,
        CurrentState = 3
    }

    [Serializable]
    public class ProfileQuestionDef
    {
        public string id;
        public ProfileTier tier;
        public string label;
        public string question;

        public ProfileQuestionDef(string id, ProfileTier tier, string label, string question)
        {
            this.id = id;
            this.tier = tier;
            this.label = label;
            this.question = question;
        }
    }

    [Serializable]
    public class ProfileSection
    {
        public ProfileTier tier;
        public string title;
        public string description;
        public IReadOnlyList<ProfileQuestionDef> questions;

        public ProfileSection(ProfileTier tier, string title, string description,
            IReadOnlyList<ProfileQuestionDef> questions)
        {
            this.tier = tier;
            this.title = title;
            this.description = description;
            this.questions = questions;
        }
    }

    public static class ProfileQuestions
    {
        // ── Tier 1: 身份底色（几乎不变） ──

        static readonly ProfileQuestionDef[] Tier1Questions =
        {
            new ProfileQuestionDef("t1_name",       ProfileTier.Identity, "名字/昵称",    "用户的名字、昵称、或对AI的称呼是什么？"),
            new ProfileQuestionDef("t1_language",   ProfileTier.Identity, "母语/常用语言", "用户的母语或常用语言是什么？"),
            new ProfileQuestionDef("t1_occupation", ProfileTier.Identity, "职业或身份",    "用户的职业或身份是什么？（学生/程序员/设计师……）"),
            new ProfileQuestionDef("t1_rhythm",    ProfileTier.Identity, "时区/生活节奏",  "用户的所在时区和生活节奏是怎样的？（早鸟还是夜猫）"),
            new ProfileQuestionDef("t1_values",    ProfileTier.Identity, "价值观关键词",   "用户的核心价值观关键词是什么？（比如「效率优先」或「关系导向」）"),
        };

        // ── Tier 2: 偏好与风格（缓慢演变） ──

        static readonly ProfileQuestionDef[] Tier2Questions =
        {
            new ProfileQuestionDef("t2_comm_style",     ProfileTier.Preferences, "沟通风格偏好",   "用户偏好的沟通风格是什么？（直接vs铺垫，简洁vs详尽）"),
            new ProfileQuestionDef("t2_response_len",   ProfileTier.Preferences, "回应长度偏好",   "用户偏好的回应长度是怎样的？"),
            new ProfileQuestionDef("t2_likes_challenge",ProfileTier.Preferences, "是否喜欢被反问", "用户是否喜欢被反问或被挑战？"),
            new ProfileQuestionDef("t2_decision_style", ProfileTier.Preferences, "决策风格",       "用户的决策风格是什么？（直觉型vs分析型）"),
            new ProfileQuestionDef("t2_interests",      ProfileTier.Preferences, "兴趣领域",       "用户的兴趣领域有哪些？（游戏、书、音乐、运动……可扩展列表）"),
            new ProfileQuestionDef("t2_dislikes",       ProfileTier.Preferences, "讨厌什么",       "用户讨厌什么？"),
            new ProfileQuestionDef("t2_relationships",  ProfileTier.Preferences, "亲近的人",       "用户亲近的人有哪些？（家人/朋友/同事+关系标注）"),
            new ProfileQuestionDef("t2_tools",          ProfileTier.Preferences, "常用工具/平台",   "用户常用的工具和平台有哪些？（用Mac还是Windows，用什么软件……）"),
        };

        // ── Tier 3: 当下状态（频繁变化） ──

        static readonly ProfileQuestionDef[] Tier3Questions =
        {
            new ProfileQuestionDef("t3_projects", ProfileTier.CurrentState, "最近在忙什么",   "用户最近在忙什么项目或事情？"),
            new ProfileQuestionDef("t3_stress",   ProfileTier.CurrentState, "烦恼/压力源",    "用户当前的烦恼或压力源是什么？（+解决状态）"),
            new ProfileQuestionDef("t3_mood",     ProfileTier.CurrentState, "最近情绪基调",   "用户最近的情绪基调是怎样的？"),
            new ProfileQuestionDef("t3_learning", ProfileTier.CurrentState, "正在学习/探索",  "用户正在学习或探索的新领域是什么？"),
            new ProfileQuestionDef("t3_goals",    ProfileTier.CurrentState, "近期目标",       "用户的近期目标是什么？（1-4周）"),
        };

        // ── Sections (for UI iteration) ──

        public static readonly IReadOnlyList<ProfileSection> Sections = new ProfileSection[]
        {
            new ProfileSection(ProfileTier.Identity,     "身份底色", "这些定义\"这个人是谁\"",          Tier1Questions),
            new ProfileSection(ProfileTier.Preferences,  "偏好与风格", "这些定义\"这个人怎么运作\"",    Tier2Questions),
            new ProfileSection(ProfileTier.CurrentState,  "当下状态", "这些定义\"这个人现在在哪里\"",    Tier3Questions),
        };

        // ── Flat list (for lookup convenience) ──

        public static readonly IReadOnlyList<ProfileQuestionDef> All;
        static readonly Dictionary<string, ProfileQuestionDef> ById = new Dictionary<string, ProfileQuestionDef>();

        static ProfileQuestions()
        {
            var all = new List<ProfileQuestionDef>();
            foreach (var section in Sections)
            {
                foreach (var q in section.questions)
                {
                    all.Add(q);
                    ById[q.id] = q;
                }
            }
            All = all;
        }

        public static ProfileQuestionDef Get(string id)
        {
            return ById.TryGetValue(id, out var q) ? q : null;
        }

        public static int TotalCount => All.Count;
    }
}
