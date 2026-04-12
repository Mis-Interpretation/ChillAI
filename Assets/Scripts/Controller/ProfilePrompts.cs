namespace ChillAI.Controller
{
    public static class ProfilePrompts
    {
        public const string TriageSystemPrompt =
            "你是一个用户画像分析助手。你的任务是根据新数据判断哪些用户画像问题需要更新。\n\n" +
            "用户画像分为三层，每层有固定的问题ID：\n\n" +
            "第一层 - 身份底色（几乎不变）：\n" +
            "  t1_name（名字/昵称）、t1_language（母语/常用语言）、t1_occupation（职业或身份）、\n" +
            "  t1_rhythm（时区/生活节奏）、t1_values（价值观关键词）\n\n" +
            "第二层 - 偏好与风格（缓慢演变）：\n" +
            "  t2_comm_style（沟通风格偏好）、t2_response_len（回应长度偏好）、\n" +
            "  t2_likes_challenge（是否喜欢被反问）、t2_decision_style（决策风格）、\n" +
            "  t2_interests（兴趣领域）、t2_dislikes（讨厌什么）、\n" +
            "  t2_relationships（亲近的人）、t2_tools（常用工具/平台）\n\n" +
            "第三层 - 当下状态（频繁变化）：\n" +
            "  t3_projects（最近在忙什么）、t3_stress（烦恼/压力源）、\n" +
            "  t3_mood（最近情绪基调）、t3_learning（正在学习/探索）、t3_goals（近期目标）\n\n" +
            "判断规则：\n" +
            "1. (unanswered) 的问题：新数据中有任何相关线索就标记更新。\n" +
            "2. 已有答案的问题：只在新数据明确表明答案已过时或不准确时才标记。\n" +
            "3. [low confidence] 的问题：适当降低更新门槛。\n" +
            "4. 第一层问题几乎不需要更新，除非有明确的身份变更信息。\n" +
            "5. 第三层问题应积极更新，只要新数据中有相关线索。\n" +
            "6. 没有相关证据的问题一律跳过，不要猜测。\n" +
            "7. 每次标记通常不超过5个问题。\n\n" +
            "Return ONLY a JSON object.";

        public const string TriageSchemaName = "profile_triage";

        public const string TriageJsonSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"updates\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"id\":{\"type\":\"string\"}," +
            "\"reason\":{\"type\":\"string\"}" +
            "},\"required\":[\"id\",\"reason\"],\"additionalProperties\":false}}" +
            "},\"required\":[\"updates\"],\"additionalProperties\":false}";

        public const string UpdateSystemPrompt =
            "你是一个用户画像撰写助手。根据提供的证据（聊天记录、任务信息、App使用记录等），回答指定的用户画像问题。\n\n" +
            "回答规则：\n" +
            "1. 只基于证据回答，不要编造或过度推断。\n" +
            "2. 答案应简洁但有信息量，通常1-3句话。\n" +
            "3. 使用中文回答。\n" +
            "4. id 字段必须与问题ID完全一致（如 t1_name、t3_mood）。\n" +
            "5. confidence 评分标准：\n" +
            "   - 0.9-1.0：证据非常明确直接（如用户自己说了相关信息）\n" +
            "   - 0.7-0.8：从多条间接证据中合理推断\n" +
            "   - 0.5-0.6：有一定线索但不够确定\n" +
            "   - 0.3-0.4：只是微弱的暗示\n" +
            "6. 如果现有答案仍然有效只是需要补充，在原答案基础上增补而不是完全替换。\n" +
            "7. 第三层（t3_开头）的问题注重时效性，描述当前的状态。\n" +
            "8. 如果证据不足以可靠回答，给出低 confidence 而不是硬编答案。\n\n" +
            "Return ONLY a JSON object.";

        public const string UpdateSchemaName = "profile_answers";

        public const string UpdateJsonSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"answers\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"id\":{\"type\":\"string\"}," +
            "\"answer\":{\"type\":\"string\"}," +
            "\"confidence\":{\"type\":\"number\"}" +
            "},\"required\":[\"id\",\"answer\",\"confidence\"],\"additionalProperties\":false}}" +
            "},\"required\":[\"answers\"],\"additionalProperties\":false}";
    }
}
