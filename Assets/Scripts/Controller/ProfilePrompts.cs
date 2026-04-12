using System;
using ChillAI.Model.UserProfile;

namespace ChillAI.Controller
{
    // ── Data source flags ──────────────────────────────────────────

    [Flags]
    public enum ProfileDataSource
    {
        None              = 0,
        ChatMessages      = 1 << 0,   // 聊天记录（用户消息）
        CurrentTasks      = 1 << 1,   // 当前任务 + 完成进度
        ArchivedTasks     = 1 << 2,   // 最近已完成任务
        AppUsageDetail    = 1 << 3,   // App 使用明细（进程名 + 时长）
        AppUsageByCategory = 1 << 4,  // App 使用分类汇总（Working: X min）
        AppActiveHours    = 1 << 5,   // App 活跃时段（用于推断作息）
        SystemTime        = 1 << 6,   // 当前系统时间
    }

    // ── Tier config ────────────────────────────────────────────────

    public readonly struct ProfileTierConfig
    {
        public readonly ProfileTier Tier;
        public readonly ProfileDataSource DataSources;
        public readonly string TriageSystemPrompt;
        public readonly string UpdateSystemPrompt;
        public readonly string AgentIdTriage;
        public readonly string AgentIdUpdate;

        public ProfileTierConfig(
            ProfileTier tier,
            ProfileDataSource dataSources,
            string triageSystemPrompt,
            string updateSystemPrompt,
            string agentIdTriage,
            string agentIdUpdate)
        {
            Tier = tier;
            DataSources = dataSources;
            TriageSystemPrompt = triageSystemPrompt;
            UpdateSystemPrompt = updateSystemPrompt;
            AgentIdTriage = agentIdTriage;
            AgentIdUpdate = agentIdUpdate;
        }
    }

    // ── Prompts & configs ──────────────────────────────────────────

    public static class ProfilePrompts
    {
        // ════════════════════════════════════════════════════════════
        //  Tier Configs — 每个 tier 声明自己需要的数据源和 prompt
        // ════════════════════════════════════════════════════════════

        public static readonly ProfileTierConfig[] TierConfigs =
        {
            // ── T1 身份底色 ──
            new ProfileTierConfig(
                tier:                ProfileTier.Identity,
                dataSources:         ProfileDataSource.ChatMessages
                                   | ProfileDataSource.SystemTime
                                   | ProfileDataSource.AppActiveHours,
                triageSystemPrompt:  TriageT1SystemPrompt,
                updateSystemPrompt:  UpdateT1SystemPrompt,
                agentIdTriage:       "profile-triage-t1",
                agentIdUpdate:       "profile-update-t1"
            ),

            // ── T2 偏好与风格 ──
            new ProfileTierConfig(
                tier:                ProfileTier.Preferences,
                dataSources:         ProfileDataSource.ChatMessages
                                   | ProfileDataSource.AppUsageByCategory,
                triageSystemPrompt:  TriageT2SystemPrompt,
                updateSystemPrompt:  UpdateT2SystemPrompt,
                agentIdTriage:       "profile-triage-t2",
                agentIdUpdate:       "profile-update-t2"
            ),

            // ── T3 当下状态 ──
            new ProfileTierConfig(
                tier:                ProfileTier.CurrentState,
                dataSources:         ProfileDataSource.ChatMessages
                                   | ProfileDataSource.CurrentTasks
                                   | ProfileDataSource.ArchivedTasks
                                   | ProfileDataSource.AppUsageDetail
                                   | ProfileDataSource.SystemTime,
                triageSystemPrompt:  TriageT3SystemPrompt,
                updateSystemPrompt:  UpdateT3SystemPrompt,
                agentIdTriage:       "profile-triage-t3",
                agentIdUpdate:       "profile-update-t3"
            ),
        };

        // ════════════════════════════════════════════════════════════
        //  JSON Schemas（所有 tier 共用）
        // ════════════════════════════════════════════════════════════

        public const string TriageSchemaName = "profile_triage";

        public const string TriageJsonSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"updates\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"id\":{\"type\":\"string\"}," +
            "\"reason\":{\"type\":\"string\"}" +
            "},\"required\":[\"id\",\"reason\"],\"additionalProperties\":false}}" +
            "},\"required\":[\"updates\"],\"additionalProperties\":false}";

        public const string UpdateSchemaName = "profile_answers";

        public const string UpdateJsonSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"answers\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"id\":{\"type\":\"string\"}," +
            "\"answer\":{\"type\":\"string\"}," +
            "\"confidence\":{\"type\":\"number\"}" +
            "},\"required\":[\"id\",\"answer\",\"confidence\"],\"additionalProperties\":false}}" +
            "},\"required\":[\"answers\"],\"additionalProperties\":false}";

        // ════════════════════════════════════════════════════════════
        //  Triage System Prompts（每 tier 独立）
        // ════════════════════════════════════════════════════════════

        public const string TriageT1SystemPrompt =
            "你是一个用户画像分析助手，专门负责判断「身份底色」层的问题是否需要更新。\n\n" +
            "你负责的问题：\n" +
            "  t1_name（名字/昵称）\n" +
            "  t1_language（母语/常用语言）\n" +
            "  t1_occupation（职业或身份）\n" +
            "  t1_rhythm（时区/生活节奏）\n" +
            "  t1_values（价值观关键词）\n\n" +
            "这些是用户最基础的身份信息，极少变化。\n\n" +
            "判断规则：\n" +
            "1. (unanswered) 的问题：新数据中有任何相关线索就标记。\n" +
            "2. 已有答案的问题：只在有明确的身份变更信号时才标记（如「我换工作了」「叫我XX」「我搬到XX了」）。\n" +
            "3. [low confidence] 的问题：如果新数据有补充线索就标记。\n" +
            "4. 门槛极高——模糊暗示不算，需要用户明确表达。\n" +
            "5. 每次最多标记 2 个问题。\n" +
            "6. 没有明确证据就返回空列表，不要猜测。\n\n" +
            "Return ONLY a JSON object.";

        public const string TriageT2SystemPrompt =
            "你是一个用户画像分析助手，专门负责判断「偏好与风格」层的问题是否需要更新。\n\n" +
            "你负责的问题：\n" +
            "  t2_comm_style（沟通风格偏好）\n" +
            "  t2_response_len（回应长度偏好）\n" +
            "  t2_likes_challenge（是否喜欢被反问）\n" +
            "  t2_decision_style（决策风格）\n" +
            "  t2_interests（兴趣领域）\n" +
            "  t2_dislikes（讨厌什么）\n" +
            "  t2_relationships（亲近的人）\n" +
            "  t2_tools（常用工具/平台）\n\n" +
            "这些反映用户的做事方式和偏好，缓慢演变。\n\n" +
            "判断规则：\n" +
            "1. (unanswered) 的问题：新数据中有相关线索就标记。\n" +
            "2. 已有答案的问题：新数据表明偏好已发生变化时才标记。\n" +
            "3. [low confidence] 的问题：有补充证据就标记。\n" +
            "4. comm_style / response_len / likes_challenge：从用户的表达模式（怎么说话）推断，而非话题内容。\n" +
            "5. interests / dislikes：从聊天内容 + App 使用类别综合判断。\n" +
            "6. relationships：注意聊天中提及的人名和关系线索。\n" +
            "7. tools：关注 App 使用数据中出现的软件/平台。\n" +
            "8. 每次最多标记 3 个问题。\n" +
            "9. 没有相关证据就跳过，不要猜测。\n\n" +
            "Return ONLY a JSON object.";

        public const string TriageT3SystemPrompt =
            "你是一个用户画像分析助手，专门负责判断「当下状态」层的问题是否需要更新。\n\n" +
            "你负责的问题：\n" +
            "  t3_projects（最近在忙什么）\n" +
            "  t3_stress（烦恼/压力源）\n" +
            "  t3_mood（最近情绪基调）\n" +
            "  t3_learning（正在学习/探索）\n" +
            "  t3_goals（近期目标）\n\n" +
            "这些反映用户此刻的状态，频繁变化。\n\n" +
            "判断规则：\n" +
            "1. (unanswered) 的问题：有任何相关线索就标记。\n" +
            "2. 已有答案的问题：只要新数据暗示状态可能已变化就标记——门槛要低。\n" +
            "3. [low confidence] 的问题：积极标记以补充信息。\n" +
            "4. 强调时效性——如果现有答案已过时（即使内容本身没错，但时间过去了），也应标记。\n" +
            "5. 每次最多标记 5 个问题。\n" +
            "6. 没有任何相关线索才跳过。\n\n" +
            "Return ONLY a JSON object.";

        // ════════════════════════════════════════════════════════════
        //  Update System Prompts（每 tier 独立）
        // ════════════════════════════════════════════════════════════

        const string UpdateCommonRules =
            "回答通用规则：\n" +
            "1. 只基于证据回答，不要编造或过度推断。\n" +
            "2. 答案应简洁但有信息量，通常1-3句话。\n" +
            "3. 使用中文回答。\n" +
            "4. id 字段必须与问题ID完全一致。\n" +
            "5. confidence 评分标准：\n" +
            "   - 0.9-1.0：证据非常明确直接（如用户自己说了相关信息）\n" +
            "   - 0.7-0.8：从多条间接证据中合理推断\n" +
            "   - 0.5-0.6：有一定线索但不够确定\n" +
            "   - 0.3-0.4：只是微弱的暗示\n" +
            "6. 如果现有答案仍然有效只是需要补充，在原答案基础上增补而不是完全替换。\n" +
            "7. 如果证据不足以可靠回答，给出低 confidence 而不是硬编答案。\n\n";

        public const string UpdateT1SystemPrompt =
            "你是一个用户画像撰写助手，负责回答「身份底色」层的问题。\n\n" +
            UpdateCommonRules +
            "身份层特殊规则：\n" +
            "- 需要非常明确的证据才能给出高 confidence。\n" +
            "- 身份信息通常来自用户的直接陈述，而非间接推断。\n" +
            "- 如果只是模糊暗示，给出 0.3-0.4 的 confidence。\n\n" +
            "Return ONLY a JSON object.";

        public const string UpdateT2SystemPrompt =
            "你是一个用户画像撰写助手，负责回答「偏好与风格」层的问题。\n\n" +
            UpdateCommonRules +
            "偏好层特殊规则：\n" +
            "- 可以从用户的行为模式中合理推断（如从聊天风格推断 comm_style）。\n" +
            "- interests 和 tools 可以结合 App 使用数据推断。\n" +
            "- relationships 需要明确提及才能记录。\n" +
            "- 偏好类答案应描述趋势而非单次行为。\n\n" +
            "Return ONLY a JSON object.";

        public const string UpdateT3SystemPrompt =
            "你是一个用户画像撰写助手，负责回答「当下状态」层的问题。\n\n" +
            UpdateCommonRules +
            "当下状态层特殊规则：\n" +
            "- 注重时效性，描述当前的状态而非历史。\n" +
            "- 如果现有答案是旧信息，应该用新信息替换而非追加。\n" +
            "- projects 和 goals 应结合任务数据综合判断。\n" +
            "- mood 和 stress 应从聊天语气和内容综合推断。\n\n" +
            "Return ONLY a JSON object.";
    }
}
