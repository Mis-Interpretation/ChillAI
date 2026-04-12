# Profile Agent (30问系统) 实现文档

## 概述

Profile Agent 是一个后台运行的 AI agent，通过分析用户的聊天记录、任务数据、App 使用时长等信息，自动回答和维护 18 个用户画像问题（分 3 层）。数据持久化到 `user_profile.json`，并提供结构化接口供 UI 读取展示。

---

## 问题结构（18 问，3 层）

### 第一层：身份底色（几乎不变）

| ID | 标签 | 问题 |
|----|------|------|
| t1_name | 名字/昵称 | 用户的名字、昵称、或对AI的称呼 |
| t1_language | 母语/常用语言 | 用户的母语或常用语言 |
| t1_occupation | 职业或身份 | 学生/程序员/设计师等 |
| t1_rhythm | 时区/生活节奏 | 早鸟还是夜猫 |
| t1_values | 价值观关键词 | 效率优先、关系导向等 |

### 第二层：偏好与风格（缓慢演变）

| ID | 标签 | 问题 |
|----|------|------|
| t2_comm_style | 沟通风格偏好 | 直接vs铺垫，简洁vs详尽 |
| t2_response_len | 回应长度偏好 | 偏好的回应长度 |
| t2_likes_challenge | 是否喜欢被反问 | 是否喜欢被反问或被挑战 |
| t2_decision_style | 决策风格 | 直觉型vs分析型 |
| t2_interests | 兴趣领域 | 游戏、书、音乐、运动等 |
| t2_dislikes | 讨厌什么 | 用户讨厌的事物 |
| t2_relationships | 亲近的人 | 家人/朋友/同事+关系标注 |
| t2_tools | 常用工具/平台 | Mac/Windows，常用软件等 |

### 第三层：当下状态（频繁变化）

| ID | 标签 | 问题 |
|----|------|------|
| t3_projects | 最近在忙什么 | 当前项目或事情 |
| t3_stress | 烦恼/压力源 | 当前烦恼+解决状态 |
| t3_mood | 最近情绪基调 | 最近的情绪状态 |
| t3_learning | 正在学习/探索 | 正在学习的新领域 |
| t3_goals | 近期目标 | 1-4周内的目标 |

---

## 触发机制

采用 **事件驱动 + 时间兜底** 的混合模式，所有阈值可在 `UserSettingsData` 中配置：

| 触发源 | 计数方式 | 默认阈值 |
|--------|---------|----------|
| 聊天消息 | `EmojiChatResponseSignal` 累计 | 10 条 |
| 任务事件 | `BigEventChangedSignal` / `SubTaskCompletionChangedSignal` 累计 | 3 个 |
| 时间兜底 | 距上次运行的真实时间（跨 session 持久化） | 60 分钟 |
| 首次运行 | `lastRunTime` 不存在 | 立即触发 |

**关键设计**：`lastRunTime` 持久化在 `user_profile.json` 中。启动时从文件读取上次运行时间，计算真实间隔，避免每次启动都触发。

**调试快捷键**：`AppSettings.debugMode` 开启时，按 `Left Alt + F + 4`（Alpha4）立即触发。

---

## Per-Tier 并行更新流程

每个 tier 拥有独立的 triage + update pipeline，3 个 tier **并行**运行。

### 数据配置（`ProfileTierConfig` in `ProfilePrompts.cs`）

每个 tier 通过 `ProfileDataSource` flags 声明需要的数据源：

| Tier | 数据源 |
|------|--------|
| T1 身份底色 | ChatMessages + SystemTime + AppActiveHours |
| T2 偏好与风格 | ChatMessages + AppUsageByCategory |
| T3 当下状态 | ChatMessages + CurrentTasks + ArchivedTasks + AppUsageDetail + SystemTime |

### Phase 1：Per-Tier Triage（分诊）

- **模型**：gpt-4o-mini（便宜快速）
- **参数**：maxTokens=512, temperature=0.2
- **3 个 triage 并行发出**，每个只接收对应 tier 的数据和 profile summary
- **输出**：需要更新的问题 ID 列表 + 原因
- **JSON Schema**：`{"updates": [{"id": "t3_mood", "reason": "..."}]}`

Per-Tier 分诊规则：
- **T1**：门槛极高，需要明确身份变更信号，每次最多标记 2 个
- **T2**：中等门槛，从行为模式推断，每次最多标记 3 个
- **T3**：低门槛，有线索就标记，每次最多标记 5 个
- 所有 tier：(unanswered) 有线索就标记，[low confidence] 降低门槛

### Phase 2：Per-Tier Focused Update（聚焦更新）

- **模型**：gpt-4o（高质量）
- **参数**：maxTokens=2048, temperature=0.5
- **只对有标记的 tier 发起 update**，并行执行
- **输出**：更新后的答案 + confidence 分数
- **JSON Schema**：`{"answers": [{"id": "t3_mood", "answer": "...", "confidence": 0.8}]}`

Per-Tier Update 侧重：
- **T1**：需要非常明确的证据，身份信息来自直接陈述
- **T2**：允许从行为模式推断，偏好类描述趋势而非单次行为
- **T3**：注重时效性，描述当前状态，旧信息应替换而非追加

**Token 成本估算**：3 个 Triage ~1,900 tokens + Update ~1,700 tokens（最坏） ≈ 3,600 tokens/次。典型成本约 ~2,800 tokens（T1 通常不触发 update）

---

## 数据源

ProfileController 的 `BuildTierDataSummary()` 根据 `ProfileDataSource` flags 按需组装数据：

| 数据源标记 | 内容 | 来源 |
|-----------|------|------|
| `SystemTime` | 当前日期时间 | `DateTime.Now` |
| `ChatMessages` | 上次运行以来的用户消息（最多 20 条） | `IChatHistoryReader` |
| `CurrentTasks` | 所有 BigEvent 及完成进度 | `ITaskDecompositionReader` |
| `ArchivedTasks` | 最近 10 条归档记录 | `ITaskArchiveStore` |
| `AppUsageDetail` | 当天各进程使用时间明细 | `IUsageTrackingReader` |
| `AppUsageByCategory` | 按 SoftwareCategory 分组汇总 | `IUsageTrackingReader` + `IBehaviorMappingReader` |
| `AppActiveHours` | 今日+近 3 天使用时长（推断作息） | `IUsageTrackingReader` |

---

## 数据结构

### 单个答案 (`ProfileAnswer`)

```
questionId: string   // 问题ID，如 "t3_mood"
answer: string       // 答案文本
confidence: float    // 置信度 0.0-1.0
updatedAt: string    // ISO 8601 时间戳
```

### 持久化文件 (`user_profile.json`)

```json
{
  "lastRunTime": "2026-04-12T03:04:45+08:00",
  "answers": [
    {
      "questionId": "t3_mood",
      "answer": "最近因为项目进展顺利而心情不错",
      "confidence": 0.8,
      "updatedAt": "2026-04-12T03:04:45+08:00"
    }
  ]
}
```

### UI 数据接口

`IProfileReader.GetSectionSnapshots()` 返回按 section 分组的数据，可直接用于 UI 渲染：

```
List<ProfileSectionSnapshot>
  ├── Section: "身份底色" (tier=Identity)
  │   ├── t1_name: Question + Answer (or null)
  │   ├── t1_language: Question + Answer (or null)
  │   └── ...
  ├── Section: "偏好与风格" (tier=Preferences)
  │   ├── t2_comm_style: Question + Answer (or null)
  │   └── ...
  └── Section: "当下状态" (tier=CurrentState)
      ├── t3_projects: Question + Answer (or null)
      └── ...
```

---

## 文件清单

### 新建文件

| 文件 | 作用 |
|------|------|
| `Model/UserProfile/ProfileQuestionDef.cs` | 问题定义、ProfileTier 枚举、ProfileSection、ProfileQuestions 静态注册表 |
| `Model/UserProfile/ProfileAnswer.cs` | 单个答案 DTO |
| `Model/UserProfile/IProfileReader.cs` | 读接口 + ProfileSectionSnapshot / ProfileQuestionAnswer |
| `Model/UserProfile/IProfileWriter.cs` | 写接口（继承 IProfileReader） |
| `Model/UserProfile/ProfileModel.cs` | 实现，持久化到 user_profile.json |
| `Core/Signals/ProfileUpdatedSignal.cs` | 更新完成信号（携带更新的问题 ID 列表） |
| `Controller/ProfileController.cs` | 核心控制器（IInitializable, IDisposable, ITickable） |
| `Controller/ProfilePrompts.cs` | 硬编码的 system prompt 和 JSON schema |

### 修改文件

| 文件 | 改动 |
|------|------|
| `Core/Settings/AgentRegistry.cs` | Ids 中添加 ProfileTriageT1/T2/T3、ProfileUpdateT1/T2/T3 |
| `Core/Settings/AppSettings.cs` | 添加 debugMode 开关 |
| `Core/Settings/UserSettingsData.cs` | 添加 profileChatThreshold、profileTaskThreshold、profileTimeThresholdMinutes |
| `Core/Settings/UserSettingsDefaults.cs` | 添加默认值（10/3/60）+ CreateData 映射 |
| `Installers/ProjectInstaller.cs` | 注册 ProfileUpdatedSignal、ProfileModel 绑定、ProfileController 绑定 |

### Unity 资产

不再需要 AgentProfile SO 文件，AgentProfile 在 `ProfileController` 中根据 `ProfileTierConfig` 动态创建。

---

## 数据流图

```
用户聊天 / 完成任务 / 时间流逝
         ↓ Signal + Tick
ProfileController 累计计数 + 计时
         ↓ 任一阈值达到
并行 Phase 1: Per-Tier Triage (gpt-4o-mini x3)
    ├─ T1 Triage: 聊天 + 时间 + 活跃时段 → T1 profile
    ├─ T2 Triage: 聊天 + App分类汇总 → T2 profile
    └─ T3 Triage: 聊天 + 任务 + 归档 + App明细 + 时间 → T3 profile
         ↓ 各自返回需更新的 ID 列表
并行 Phase 2: Per-Tier Update (gpt-4o, 仅对有标记的 tier)
    ├─ T1 Update (如有): 同上数据 + 标记的问题
    ├─ T2 Update (如有): 同上数据 + 标记的问题
    └─ T3 Update (如有): 同上数据 + 标记的问题
         ↓
合并写入 ProfileModel.SetAnswer() → RecordRunTime() → Save()
         ↓
ProfileUpdatedSignal → UI 刷新
```

---

## 配置项

在 `UserSettingsData` / `UserSettingsDefaults` 中：

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| profileChatThreshold | int | 10 | 多少条聊天消息触发一次 |
| profileTaskThreshold | int | 3 | 多少个任务事件触发一次 |
| profileTimeThresholdMinutes | int | 60 | 最长间隔（分钟） |

在 `AppSettings` 中：

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| debugMode | bool | false | 开启后可用快捷键手动触发 |
