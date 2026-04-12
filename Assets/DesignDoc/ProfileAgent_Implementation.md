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

## 两阶段更新流程

### Phase 1：Triage（分诊）

- **模型**：gpt-4o-mini（便宜快速）
- **参数**：maxTokens=512, temperature=0.2
- **输入**：新数据摘要 + 当前 profile 按 section 分组的概要
- **输出**：需要更新的问题 ID 列表 + 原因
- **JSON Schema**：`{"updates": [{"id": "t3_mood", "reason": "..."}]}`

分诊规则（写在 system prompt 中）：
- 第一层问题几乎不更新，除非有明确身份变更
- 第三层问题积极更新
- (unanswered) 的问题有线索就标记
- [low confidence] 的问题降低门槛
- 每次最多标记 5 个问题

### Phase 2：Focused Update（聚焦更新）

- **模型**：gpt-4o（高质量）
- **参数**：maxTokens=2048, temperature=0.5
- **输入**：证据 + 仅 Phase 1 标记的问题（含当前答案和更新原因）
- **输出**：更新后的答案 + confidence 分数
- **JSON Schema**：`{"answers": [{"id": "t3_mood", "answer": "...", "confidence": 0.8}]}`

**Token 成本估算**：Phase 1 ~1,100 tokens + Phase 2 ~1,000 tokens = ~2,100 tokens/次

---

## 数据源

ProfileController 在 `BuildNewDataSummary()` 中收集以下数据发送给 AI：

1. **系统时间** - 当前日期时间
2. **聊天记录** - 上次运行以来的新消息（最多 20 条），从 `IChatHistoryReader` 读取
3. **当前任务** - 所有 BigEvent 及完成进度，从 `ITaskDecompositionReader` 读取
4. **已完成任务** - 最近 10 条归档记录，从 `ITaskArchiveStore` 读取
5. **App 使用时长** - 当天各进程使用时间（超过 1 分钟的），从 `IUsageTrackingReader` 读取

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
| `Core/Settings/AgentRegistry.cs` | Ids 中添加 ProfileTriage、ProfileUpdate |
| `Core/Settings/AppSettings.cs` | 添加 debugMode 开关 |
| `Core/Settings/UserSettingsData.cs` | 添加 profileChatThreshold、profileTaskThreshold、profileTimeThresholdMinutes |
| `Core/Settings/UserSettingsDefaults.cs` | 添加默认值（10/3/60）+ CreateData 映射 |
| `Installers/ProjectInstaller.cs` | 注册 ProfileUpdatedSignal、ProfileModel 绑定、ProfileController 绑定 |

### Unity 资产

| 文件 | 说明 |
|------|------|
| `Data/ProfileTriageAgent.asset` | AgentProfile SO（prompt 未使用，硬编码在 ProfilePrompts.cs） |
| `Data/ProfileUpdateAgent.asset` | AgentProfile SO（prompt 未使用，硬编码在 ProfilePrompts.cs） |

---

## 数据流图

```
用户聊天 / 完成任务 / 时间流逝
         ↓ Signal + Tick
ProfileController 累计计数 + 计时
         ↓ 任一阈值达到
Phase 1: Triage (gpt-4o-mini)
    输入: 新数据摘要 + 当前 profile 概要
    输出: ["t3_mood", "t3_projects"] 需要更新
         ↓
Phase 2: Focused Update (gpt-4o)
    输入: 仅标记的问题 + 完整证据
    输出: 更新后的答案 + confidence
         ↓
ProfileModel.SetAnswer() → RecordRunTime() → Save()
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
