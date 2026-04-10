# Task Panel 实现方案：自动生成开关 + 拖拽排序

> 版本：v1.0  
> 日期：2026-04-09

---

## 1. 需求概述

### 1.1 自动任务生成开关

在 System Menu 的 Settings 面板中增加一个 Toggle 控件，控制 task agent 是否自动从聊天内容中提取任务意图并创建 BigEvent / SubTask。**默认关闭**。

### 1.2 任务项拖拽

为 Task Panel 中的任务项增加拖拽能力：

| 拖拽操作 | 效果 |
|----------|------|
| 同一 BigEvent 下的 SubTask 上下拖拽 | 重新排序 |
| SubTask 拖到左栏 | 提升为 BigEvent |
| BigEvent 拖到右栏 | 降级为当前选中 BigEvent 的 SubTask |

**与现有交互不冲突**：单击（选中/编辑）和长按拖拽通过 300ms 长按阈值区分。

---

## 2. 架构概览

```
┌──────────────────────────────────────────────────────────┐
│                      View Layer                          │
│  SystemMenuView.cs ─── Toggle ──→ AppSettings            │
│  TaskPanelView.cs  ─── TaskItemDragManipulator           │
│       ├─ ResolveDropTarget()  判断放置目标                │
│       └─ HandleDragEnd()      执行操作                    │
├──────────────────────────────────────────────────────────┤
│                   Controller Layer                       │
│  EmojiChatController.cs ── autoGenerateTasks 守卫        │
│  TaskDecompositionController.cs                          │
│       ├─ ReorderSubTask()                                │
│       ├─ PromoteSubTaskToBigEvent()                      │
│       └─ DemoteBigEventToSubTask()                       │
├──────────────────────────────────────────────────────────┤
│                     Model Layer                          │
│  BigEvent.cs                                             │
│       ├─ InsertSubTask(index, subTask)                   │
│       ├─ MoveSubTask(subTaskId, newIndex)                │
│       └─ ReindexSubTasks()                               │
│  TaskDecompositionModel.cs                               │
│       ├─ MoveSubTaskToIndex()                            │
│       ├─ DetachSubTask()                                 │
│       └─ InsertBigEvent()                                │
├──────────────────────────────────────────────────────────┤
│                     Signal Layer                         │
│  BigEventChangedSignal ── +SubTaskReordered 枚举值       │
└──────────────────────────────────────────────────────────┘
```

---

## 3. Feature 1：自动任务生成开关

### 3.1 数据层

**文件**：`Assets/Scripts/Core/Settings/AppSettings.cs`

```csharp
[Header("Emoji Chat")]
// ... existing fields ...

[Tooltip("Allow task agent to auto-create tasks from chat intent")]
public bool autoGenerateTasks = false;
```

`AppSettings` 是 ScriptableObject，通过 `ProjectInstaller` 以 `BindInstance` 注入 Zenject 容器，所有需要读取的类均可通过构造函数注入获取。

### 3.2 控制层

**文件**：`Assets/Scripts/Controller/EmojiChatController.cs`

新增 `AppSettings` 构造注入：

```csharp
readonly AppSettings _appSettings;

public EmojiChatController(
    // ... existing params ...
    AppSettings appSettings)
{
    // ...
    _appSettings = appSettings;
}
```

在 `SendMessage()` 中，将自动任务路由调用加上守卫条件：

```csharp
// 原来
if (!string.IsNullOrWhiteSpace(parsed.task_intent))
    HandleTaskIntent(parsed.task_intent);

// 改为
if (_appSettings.autoGenerateTasks && !string.IsNullOrWhiteSpace(parsed.task_intent))
    HandleTaskIntent(parsed.task_intent);
```

### 3.3 视图层

**文件**：`Assets/UI/SystemMenu.uxml`

在 bubbles-slider 行后新增 Toggle 行：

```xml
<ui:VisualElement class="setting-row">
    <ui:Label text="自动任务" class="setting-label"/>
    <ui:Toggle name="auto-task-toggle" class="setting-toggle"/>
</ui:VisualElement>
```

**文件**：`Assets/UI/SystemMenu.uss`

新增 Toggle 样式，复用暗色主题风格：

```css
.setting-toggle {
    margin-left: auto;
    flex-shrink: 0;
}

.setting-toggle > .unity-toggle__input > .unity-toggle__checkmark {
    background-color: rgba(40, 40, 50, 0.95);
    border-color: rgba(255, 255, 255, 0.2);
    border-radius: 9px;
    width: 18px;
    height: 18px;
    border-width: 1px;
}

.setting-toggle:checked > .unity-toggle__input > .unity-toggle__checkmark {
    background-color: rgba(80, 140, 255, 0.7);
    border-color: rgba(80, 140, 255, 0.9);
}
```

**文件**：`Assets/Scripts/View/SystemMenu/SystemMenuView.cs`

```csharp
Toggle _autoTaskToggle;

// OnEnable() 中初始化
_autoTaskToggle = root.Q<Toggle>("auto-task-toggle");
_autoTaskToggle.value = _appSettings.autoGenerateTasks;
_autoTaskToggle.RegisterValueChangedCallback(evt =>
    _appSettings.autoGenerateTasks = evt.newValue);
```

---

## 4. Feature 2：任务项拖拽

### 4.1 交互设计

#### 4.1.1 单击 vs 长按区分

```
PointerDown
    │
    ├── < 300ms 松手 ──→ 正常单击（选中/编辑/勾选）
    │
    ├── 300ms 内移动 > 5px ──→ 取消（用户在滚动）
    │
    └── 300ms 到期且未移动 ──→ 进入拖拽模式
         │
         ├── PointerMove ──→ ghost 跟随 + 显示 drop indicator
         │
         └── PointerUp ──→ 执行放置操作（排序/升级/降级）
```

#### 4.1.2 拖拽规则矩阵

| 拖拽源 | 放置到左栏 | 放置到右栏 |
|--------|-----------|-----------|
| **SubTask** | 提升为 BigEvent | 同父级内排序 |
| **BigEvent** | 无操作 | 降级为选中 BigEvent 的 SubTask |

#### 4.1.3 视觉反馈

- **Ghost 元素**：半透明蓝色背景 + 白色文字，跟随光标偏移 8px
- **Drop Indicator**：2px 蓝色水平线，显示在目标插入位置
- **源元素变暗**：拖拽中源元素 opacity 降至 0.3

### 4.2 数据层变更

#### 4.2.1 BigEvent.cs

**文件**：`Assets/Scripts/Model/TaskDecomposition/BigEvent.cs`

新增方法：

```csharp
/// 在指定位置插入子任务
public void InsertSubTask(int index, SubTask subTask)
{
    index = Math.Max(0, Math.Min(index, _subTasks.Count));
    _subTasks.Insert(index, subTask);
    ReindexSubTasks();
}

/// 将子任务移动到新位置
public void MoveSubTask(string subTaskId, int newIndex)
{
    var idx = _subTasks.FindIndex(s => s.Id == subTaskId);
    if (idx < 0) return;
    var task = _subTasks[idx];
    _subTasks.RemoveAt(idx);
    newIndex = Math.Max(0, Math.Min(newIndex, _subTasks.Count));
    _subTasks.Insert(newIndex, task);
    ReindexSubTasks();
}

/// 重新编号所有子任务的 Order
void ReindexSubTasks()
{
    for (int i = 0; i < _subTasks.Count; i++)
        _subTasks[i].Order = i + 1;
}
```

重构 `RemoveSubTask` 中的 re-index 循环为调用 `ReindexSubTasks()`。

#### 4.2.2 ITaskDecompositionWriter.cs

**文件**：`Assets/Scripts/Model/TaskDecomposition/ITaskDecompositionWriter.cs`

新增接口方法：

```csharp
void MoveSubTaskToIndex(string bigEventId, string subTaskId, int newIndex);
SubTask DetachSubTask(string bigEventId, string subTaskId);
void InsertBigEvent(int index, BigEvent bigEvent);
```

#### 4.2.3 TaskDecompositionModel.cs

**文件**：`Assets/Scripts/Model/TaskDecomposition/TaskDecompositionModel.cs`

实现接口：

```csharp
public void MoveSubTaskToIndex(string bigEventId, string subTaskId, int newIndex)
{
    var bigEvent = GetBigEvent(bigEventId);
    bigEvent?.MoveSubTask(subTaskId, newIndex);
}

public SubTask DetachSubTask(string bigEventId, string subTaskId)
{
    var bigEvent = GetBigEvent(bigEventId);
    if (bigEvent == null) return null;
    var subTask = bigEvent.SubTasks.FirstOrDefault(s => s.Id == subTaskId);
    if (subTask == null) return null;
    bigEvent.RemoveSubTask(subTaskId);
    return subTask;
}

public void InsertBigEvent(int index, BigEvent bigEvent)
{
    index = Math.Max(0, Math.Min(index, _bigEvents.Count));
    _bigEvents.Insert(index, bigEvent);
}
```

### 4.3 信号层变更

**文件**：`Assets/Scripts/Core/Signals/BigEventChangedSignal.cs`

```csharp
public enum BigEventChangeType
{
    Added,
    Removed,
    SubTaskAdded,
    SubTaskRemoved,
    SubTaskReordered    // 新增
}
```

无需新增信号类或修改 Installer 注册。

### 4.4 控制层变更

**文件**：`Assets/Scripts/Controller/TaskDecompositionController.cs`

新增三个公开方法：

```csharp
/// 重排序子任务
public void ReorderSubTask(string bigEventId, string subTaskId, int newIndex)
{
    _taskModel.MoveSubTaskToIndex(bigEventId, subTaskId, newIndex);
    _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.SubTaskReordered));
    _taskModel.Save();
}

/// 子任务提升为大任务，返回新 BigEvent ID
public string PromoteSubTaskToBigEvent(string bigEventId, string subTaskId)
{
    var subTask = _taskModel.DetachSubTask(bigEventId, subTaskId);
    if (subTask == null) return null;
    var newBigEvent = _taskModel.AddBigEvent(subTask.Title);
    _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.SubTaskRemoved));
    _signalBus.Fire(new BigEventChangedSignal(newBigEvent.Id, BigEventChangeType.Added));
    _taskModel.Save();
    return newBigEvent.Id;
}

/// 大任务降级为目标大任务的子任务
public void DemoteBigEventToSubTask(string bigEventId, string targetBigEventId)
{
    if (bigEventId == targetBigEventId) return;
    var bigEvent = _taskModel.GetBigEvent(bigEventId);
    if (bigEvent == null) return;
    var target = _taskModel.GetBigEvent(targetBigEventId);
    if (target == null) return;

    var title = bigEvent.Title;
    _taskModel.RemoveBigEvent(bigEventId);
    var subTask = new SubTask(title, target.TotalCount + 1);
    target.AddSubTask(subTask);
    _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.Removed));
    _signalBus.Fire(new BigEventChangedSignal(targetBigEventId, BigEventChangeType.SubTaskAdded));
    _taskModel.Save();
}
```

### 4.5 视图层变更

#### 4.5.1 TaskItemDragManipulator.cs（新建文件）

**文件**：`Assets/Scripts/View/TaskUI/TaskItemDragManipulator.cs`

**核心类型定义**：

```csharp
public enum DragItemType { BigEvent, SubTask }

public struct DragItemInfo
{
    public DragItemType Type;
    public string ItemId;
    public string ParentBigEventId;
}

public enum DropTargetType { None, ReorderSubTask, PromoteToList, DemoteToSubTask }

public struct DropTarget
{
    public DropTargetType Type;
    public int InsertIndex;
    public string TargetBigEventId;
    // 视觉反馈定位（panel 坐标系）
    public float IndicatorY;
    public float IndicatorLeft;
    public float IndicatorWidth;
}
```

**状态机**：

```
Idle ──PointerDown──→ Waiting ──300ms──→ Dragging ──PointerUp──→ Idle
                         │                    │
                    PointerUp/Move>5px    PointerCaptureOut
                         │                    │
                         └──→ Idle ←──────────┘
```

**构造参数**：

```csharp
TaskItemDragManipulator(
    Func<DragItemInfo> getItemInfo,
    Func<Vector2, DragItemInfo, DropTarget> resolveDropTarget,
    Action<DragItemInfo, DropTarget> onDragEnd,
    Action onDragStarted,
    VisualElement overlayRoot
)
```

**关键行为**：

1. `OnPointerDown` — 记录位置，启动 300ms schedule，**不捕获指针不阻止冒泡**
2. `OnLongPress` — 进入 Dragging：回调 `onDragStarted`（提交编辑），捕获指针，创建 ghost + indicator
3. `OnPointerMove (Dragging)` — ghost 跟随光标，调用 `resolveDropTarget` 定位 indicator
4. `OnPointerUp (Dragging)` — 解析最终 DropTarget，调用 `onDragEnd`，清理视觉元素
5. `OnPointerUp (Waiting)` — 取消计时器，回到 Idle，正常 click 事件继续冒泡

**坐标转换**：
- `ToPanelPos()` — target 本地坐标 → panel.visualTree 坐标
- `ToOverlayLocal()` — panel 坐标 → overlayRoot 本地坐标（用于定位 ghost/indicator）

#### 4.5.2 TaskPanel.uss 拖拽样式

**文件**：`Assets/UI/TaskPanel.uss`

```css
/* Ghost 跟随元素 */
.drag-ghost {
    position: absolute;
    background-color: rgba(60, 100, 180, 0.85);
    border-radius: 5px;
    padding: 4px 10px;
    max-width: 200px;
}

.drag-ghost-label {
    color: #ffffff;
    font-size: 12px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

/* 插入位置指示线 */
.drop-indicator {
    position: absolute;
    height: 2px;
    background-color: rgba(100, 170, 255, 0.9);
    border-radius: 1px;
}

/* 被拖元素变暗 */
.drag-source-dimmed {
    opacity: 0.3;
}
```

#### 4.5.3 TaskPanelView.cs 集成

**文件**：`Assets/Scripts/View/TaskUI/TaskPanelView.cs`

**a) 为 BigEvent 行附加拖拽**（`CreateListItem()` 末尾）：

```csharp
var dragManip = new TaskItemDragManipulator(
    () => new DragItemInfo { Type = DragItemType.BigEvent, ItemId = bigEventId },
    ResolveDropTarget,
    HandleDragEnd,
    () => { CommitEditMode(); RemoveInlineInput(); },
    _panel);
row.AddManipulator(dragManip);
```

**b) 为 SubTask 行附加拖拽**（`CreateSubTaskElement()` 末尾）：

```csharp
var dragManip = new TaskItemDragManipulator(
    () => new DragItemInfo {
        Type = DragItemType.SubTask,
        ItemId = subTaskId,
        ParentBigEventId = bigEventId
    },
    ResolveDropTarget,
    HandleDragEnd,
    () => { CommitEditMode(); RemoveInlineInput(); },
    _panel);
row.AddManipulator(dragManip);
```

**c) 放置目标解析** — `ResolveDropTarget(Vector2 panelPos, DragItemInfo info)`：

```
panelPos 在 _listScroll.worldBound 内？
  ├── info.Type == SubTask → PromoteToList
  └── 其他 → None

panelPos 在 _taskScroll.worldBound 内？
  ├── info.Type == BigEvent
  │     ├── _selectedBigEventId != null 且 != info.ItemId → DemoteToSubTask
  │     └── 否则 → None
  └── info.Type == SubTask → ReorderSubTask（计算 insertIndex）
```

插入位置计算：遍历 ScrollView.contentContainer 的子元素，比较鼠标 Y 与每个子元素的 worldBound 中点，确定插入索引。

**d) 拖拽完成处理** — `HandleDragEnd(DragItemInfo info, DropTarget dropTarget)`：

```csharp
switch (dropTarget.Type)
{
    case ReorderSubTask:
        _controller.ReorderSubTask(info.ParentBigEventId, info.ItemId, dropTarget.InsertIndex);
        break;
    case PromoteToList:
        var newId = _controller.PromoteSubTaskToBigEvent(info.ParentBigEventId, info.ItemId);
        // 自动选中新建的 BigEvent
        break;
    case DemoteToSubTask:
        _controller.DemoteBigEventToSubTask(info.ItemId, dropTarget.TargetBigEventId);
        // 如果降级的是当前选中项，切换选中到目标
        break;
}
```

**e) 信号处理更新** — `OnBigEventChanged` 增加 case：

```csharp
case BigEventChangeType.SubTaskReordered:
    if (signal.BigEventId == _selectedBigEventId)
        RebuildRightColumn();
    break;
```

---

## 5. 边界情况处理

| 场景 | 处理策略 |
|------|----------|
| BigEvent 拖到右栏但自己就是选中的 | `ResolveDropTarget` 返回 `None`，禁止自降级 |
| 无选中 BigEvent 时拖 BigEvent 到右栏 | `_selectedBigEventId` 为 null，返回 `None` |
| 拖拽时有编辑中的文本 | `onDragStarted` 回调 `CommitEditMode()` + `RemoveInlineInput()` |
| 指针在拖拽中离开面板 | `PointerCaptureOutEvent` 触发清理 ghost/indicator |
| 快速点击（< 300ms） | 计时器在 `PointerUp` 时取消，正常 click/select/edit 事件不受影响 |
| SubTask 拖到原位 | `BigEvent.MoveSubTask` 执行 remove + 同位 insert，无副作用 |
| 唯一的 BigEvent 无法降级 | 无其他 BigEvent 可作为目标，返回 `None` |
| 拖拽前移动超 5px | 视为滚动意图，取消长按计时器 |

---

## 6. 文件变更清单

### 6.1 修改的文件

| 文件 | 变更内容 |
|------|----------|
| `Assets/Scripts/Core/Settings/AppSettings.cs` | +1 字段 `autoGenerateTasks` |
| `Assets/Scripts/Controller/EmojiChatController.cs` | +注入 AppSettings, +守卫条件 |
| `Assets/UI/SystemMenu.uxml` | +1 Toggle 行 |
| `Assets/UI/SystemMenu.uss` | +`.setting-toggle` 样式 |
| `Assets/Scripts/View/SystemMenu/SystemMenuView.cs` | +Toggle 绑定逻辑 |
| `Assets/Scripts/Model/TaskDecomposition/BigEvent.cs` | +3 方法, 重构 RemoveSubTask |
| `Assets/Scripts/Model/TaskDecomposition/ITaskDecompositionWriter.cs` | +3 接口方法 |
| `Assets/Scripts/Model/TaskDecomposition/TaskDecompositionModel.cs` | +3 方法实现 |
| `Assets/Scripts/Core/Signals/BigEventChangedSignal.cs` | +1 枚举值 |
| `Assets/Scripts/Controller/TaskDecompositionController.cs` | +3 公开方法 |
| `Assets/UI/TaskPanel.uss` | +拖拽相关样式 |
| `Assets/Scripts/View/TaskUI/TaskPanelView.cs` | +拖拽集成, +ResolveDropTarget, +HandleDragEnd |

### 6.2 新建的文件

| 文件 | 说明 |
|------|------|
| `Assets/Scripts/View/TaskUI/TaskItemDragManipulator.cs` | 长按拖拽 Manipulator（~260 行） |

---

## 7. 实施顺序

```
Phase A（自动生成开关，独立完成）
  1. AppSettings.cs
  2. EmojiChatController.cs
  3. SystemMenu.uxml
  4. SystemMenu.uss
  5. SystemMenuView.cs

Phase B（拖拽 Model/Controller 层）
  6. BigEvent.cs
  7. ITaskDecompositionWriter.cs
  8. TaskDecompositionModel.cs
  9. BigEventChangedSignal.cs
  10. TaskDecompositionController.cs

Phase C（拖拽 View 层，依赖 Phase B）
  11. TaskItemDragManipulator.cs（新建）
  12. TaskPanel.uss
  13. TaskPanelView.cs
```

---

## 8. 验证要点

1. **自动任务开关**：Settings 中确认"自动任务" Toggle 默认关闭；开启后聊天输入任务意图可自动创建任务；关闭后不再自动创建
2. **SubTask 排序**：长按 SubTask 300ms 出现 ghost 和蓝色指示线，上下拖动松手后顺序改变
3. **SubTask 提升**：SubTask 拖到左栏松手，左栏出现新的 BigEvent，右栏移除该 SubTask
4. **BigEvent 降级**：BigEvent 拖到右栏松手，左栏移除该 BigEvent，右栏出现新的 SubTask
5. **交互无冲突**：快速单击不触发拖拽，编辑和选中功能正常工作
6. **持久化**：所有拖拽操作后 tasks.json 正确保存
