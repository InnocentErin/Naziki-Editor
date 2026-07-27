# 📊 故事板数据逻辑系统分析报告

**版本**: 2.0.3  
**分析日期**: 2026-07-24  
**最终状态**: ✅ 全部 P0/P1 问题已修复，134 项测试全部通过  
**分析范围**: 故事板 JSON 数据格式规范、层级结构定义、数据读取（导入）和写入（保存/导出）全流程

---

## 一、参考依据

| 参考文件 | 路径 | 用途 |
|---------|------|------|
| 官方说明书 | `tutorial/Storyboard说明.md` | 格式规范权威来源 |
| 官方示例文件 | `tutorial/storyboard_example.json` | 真实数据格式验证 |
| 官方数据模型 | `tutorial/Cytoid_StoryboardModel.cs` | 仅作参考，不可直接调用 |
| 项目数据模型 | `Models/StoryboardModels.cs` | 项目实际使用的优化模型 |

---

## 二、已修复问题汇总

### 2.1 ✅ P0-1: NoteControllerState 缺失关键属性（已修复）

**文件**: `Models/StoryboardModels.cs`

官方规范中 NoteController 支持以下属性，项目模型中原本缺失，已全部补全：

| 官方属性 | JSON 键名 | 修复内容 |
|---------|----------|---------|
| `x_multiplier` | `x_multiplier` | ✅ 新增 `float? XMultiplier` |
| `dx` | `dx` | ✅ 新增 `float? Dx`，官方标注为已知BUG |
| `y_multiplier` | `y_multiplier` | ✅ 新增 `float? YMultiplier` |
| `dy` | `dy` | ✅ 新增 `float? Dy` |
| `override_ring_color` | `override_ring_color` | ✅ 新增 `bool? OverrideRingColor` |
| `ring_color` | `ring_color` | ✅ 新增 `string RingColor` |
| `override_fill_color` | `override_fill_color` | ✅ 新增 `bool? OverrideFillColor` |
| `fill_color` | `fill_color` | ✅ 新增 `string FillColor` |
| `hitbox_multiplier` | `hitbox_multiplier` | ✅ 新增 `float? HitboxMultiplier` |
| `hold_direction` | `hold_direction` | ✅ 新增 `int? HoldDirection` |
| `style` | `style` | ✅ 新增 `int? Style` |

**属性名修正**: NoteController 使用 `opacity_multiplier` / `size_multiplier`（无 `note_` 前缀），与 SceneController 的全局 `note_opacity_multiplier` 区分。

**影响**: 修复前导入包含这些属性的官方谱面时，属性会**静默丢失**，重新保存后行为不一致。修复后属性完整保留。

---

### 2.2 ✅ P0-2: float.MaxValue 的 time 不应序列化（已修复）

**文件**: `Core/Serialization/Converters/StoryboardEntityConverter.cs`

**问题**: BaseState 的 `time` 无条件写入根节点。若 time 值为 `float.MaxValue`（表示"未设置"），它会被序列化，导致导入后对象被意外激活。

**修复**: 在 `WriteJson` 中添加检查，跳过值为 `float.MaxValue` 的 time 属性：

```csharp
if (prop.Name == "time")
{
    if (prop.Value.Type == JTokenType.Float && Math.Abs((float)prop.Value - float.MaxValue) < 0.01f)
        continue;
    if (prop.Value.Type == JTokenType.String && prop.Value.ToString() == float.MaxValue.ToString())
        continue;
}
```

**验证**: `FloatMaxValue_Time_ShouldNotBeSerialized` / `FloatMaxValue_StringTime_ShouldNotBeSerialized` 通过。

---

### 2.3 ✅ P0-3: `$note` 占位符未被处理（已修复）

**文件**: `Core/Storyboard/StoryboardParser.cs`

**问题**: 官方规范中 `id`, `parent_id`, `target_id`, `time` 都支持 `$note` 占位符。项目未处理占位符替换，导入包含 `$note` 的谱面时占位符被当作普通字符串保留。

**修复**: 添加 `ResolveNotePlaceholders` 和 `ReplaceNotePlaceholder` 方法，在 NoteController 的 note 为具体数字时替换 `$note` 占位符：

```csharp
private static void ResolveNotePlaceholders(StoryboardRoot root)
{
    // 当 note_controller 的 note 字段为具体数字时，替换 $note 占位符
    // 对于 note 选择器 {}，保留 $note 占位符不变（游戏运行时会展开）
}

private static void ReplaceNotePlaceholder(IStoryboardEntity entity, string noteId)
{
    // 替换 Id, ParentId, TargetId, Time 中的 $note 占位符
}
```

**验证**: `StoryboardParserTests` 全部通过，集成测试验证 NoteController 选择器正确保留。

---

### 2.4 ✅ P1-1: TextState.Align 类型不匹配（已修复）

**文件**: `Models/StoryboardModels.cs`

**修复**: `Align` 属性从 `int?` 改为 `string`，兼容官方字符串值（`"upperLeft"`, `"middleCenter"` 等）。

---

### 2.5 ✅ P1-2: TextState.FontWeight 缺失（已修复）

**文件**: `Models/StoryboardModels.cs`

**修复**: 新增 `string FontWeight` 属性，JSON 键名 `"font_weight"`。

---

### 2.6 ✅ 前期修复（遗留确认）

| 修复项 | 文件 | 说明 |
|--------|------|------|
| UnitFloatConverter 格式兼容 | `Core/Serialization/Converters/UnitFloatConverter.cs` | 输出 `"noteX:0.5"` 官方格式，同时兼容两种格式读取 |
| StoryboardCompiler 相对时间计算 | `Core/Compilation/StoryboardCompiler.cs` | 相对时间从 BaseState 的 time 开始计算 |
| 模板展开 Keyframes 为空 | `Core/Compilation/StoryboardCompiler.cs` | 空 Keyframes 时也从模板展开 |
| 模板子帧相对时间 | `Core/Compilation/StoryboardCompiler.cs` | 基于上一子帧时间计算 |
| StageObjectState 缺失属性 | `Models/StoryboardModels.cs` | 补全 ScaleX/Y, Scale, Pivot, FillWidth, Width, Height |
| TemplateState 重复属性 | `Models/StoryboardModels.cs` | 移除重复的 NoteOpacityMultiplier |

---

## 三、Serialization 关键流程审查（最终状态）

### 3.1 写入流程 (StoryboardEntityConverter.WriteJson)

```
BaseState (根节点属性) + Keyframes → states[] 数组
```

| 检查项 | 状态 | 备注 |
|--------|------|------|
| time 写入根节点 | ✅ | BaseState.Time 被正确序列化到根级别 |
| float.MaxValue time 跳过 | ✅ | 未设置的 time 不会被序列化 |
| note 提取到根节点 | ✅ | NoteController 的 NoteTarget 被提取到外层 |
| easing 从关键帧移除 | ✅ | 官方规范要求在根节点定义 easing |
| target_id 隐藏 id | ✅ | 控制板对象不输出 id |
| 嵌套 states 展平 | ❌ | 官方支持嵌套 states，项目未实现（P2 后续） |
| destroy 序列化 | ✅ | bool? 类型正确序列化 |

### 3.2 读取流程 (StoryboardEntityConverter.ReadJson)

```
根节点属性 → BaseState, states[] → Keyframes
```

| 检查项 | 状态 | 备注 |
|--------|------|------|
| 根节点属性映射到 BaseState | ✅ | 所有非保留属性正确映射 |
| states 数组还原为 Keyframes | ✅ | 类型正确匹配 |
| note 字段特殊处理 | ✅ | NoteController 的 note 正确塞入状态 |
| id/parent_id/target_id 提取 | ✅ | 正确分配到实体属性 |

### 3.3 ID 管理系统 (StoryboardParser)

| 检查项 | 状态 | 备注 |
|--------|------|------|
| 实体 ID 标准化 | ✅ | 无 ID 实体自动生成唯一 ID |
| 控制板 ID 映射 | ✅ | 同步到 NazikiProjectModel.ControlBoardIdMaps |
| $note 占位符替换 | ✅ | 具体 note ID 替换 $note，选择器保留 |
| 控制板 ID 清理 | ✅ | 旧条目在 Sync 时被清除 |

---

## 四、测试体系建设

### 4.1 测试体系架构

```
测试体系 (134 tests, 100% 通过)
├── 单元测试 (Unit Tests)
│   ├── UnitFloatConverterTests (13 tests) — 坐标转换器
│   ├── StoryboardModelTests (11 tests) — 数据模型序列化
│   └── StoryboardValidatorTests (8 tests) — 时空冲突检测
├── 集成测试 (Integration Tests)
│   ├── StoryboardIntegrationTests (12 tests) — 完整导入导出流程
│   ├── StoryboardCompilerTests (8 tests) — 编译器展平/模板/分裂
│   └── StoryboardParserTests (7 tests) — ID 标准化/账本同步
├── 边界测试 (Boundary Tests)
│   └── StoryboardBoundaryTests (37 tests) — 极端值/异常/安全
└── 回归测试 (Regression Tests)
    └── StoryboardRegressionTests (38 tests) — 官方示例/往返稳定性
```

### 4.2 测试文件详细清单

| 测试文件 | 测试数 | 覆盖范围 |
|---------|-------|---------|
| `UnitFloatConverterTests` | 13 | 坐标转换器：官方格式解析、纯数字反序列化、null处理、往返一致性、所有单位类型、大数值 |
| `StoryboardModelTests` | 11 | 数据模型：Sprite/Text/Line/Controller/NoteController 序列化、ID 控制、TargetId 优先级 |
| `StoryboardCompilerTests` | 8 | 编译器：模板展平、相对时间、细胞分裂(Mitosis)、混合控制器、Fov变化 |
| `StoryboardIntegrationTests` | 12 | 集成：完整往返、模板保留、坐标保留、NoteController、多实体、三次往返、大量数据 |
| `StoryboardParserTests` | 7 | 解析器：ID 标准化、控制板ID映射、$note替换、多控制板、账本同步、旧条目清理 |
| `StoryboardValidatorTests` | 8 | 校验器：时空冲突、重叠检测、状态一致性 |
| `StoryboardBoundaryTests` | 37 | 边界：空集合、null、NaN/Infinity、极端值、JSON注入、缺失模板、自引用模板、负时间、细胞分裂边界、空选择器、全过滤器选择器、极端Z值、easing大小写、模板BaseState无time |
| `StoryboardRegressionTests` | 38 | 回归：官方示例单次/三次往返、实体类型保留、模板保留、时间数组、NoteController特效、SceneController特效(glitch/bloom/scanline/arcade)、视频模板、特殊值(destroy/scale)、混合实体、确定性输出、坐标混合格式、时间格式边界 |
| **总计** | **134** | |

### 4.3 边界测试覆盖详情

| 测试类别 | 测试项 | 状态 |
|---------|--------|------|
| 空集合/Null | 空Root、空列表、Null集合、Null BaseState、Null Keyframes、空Keyframes | ✅ |
| 极端浮点数 | NaN、Infinity、float.MaxValue、float.MinValue、极大值 | ✅ |
| 字符串边界 | 空路径、空文本、特殊字符、超长字符串、空白字符串 | ✅ |
| 安全测试 | JSON 注入路径、JSON 注入文本 | ✅ |
| 模板边界 | 缺失模板引用、自引用模板（防无限循环）、模板BaseState无time | ✅ |
| 时间边界 | 负时间值、零时间、极大时间 | ✅ |
| 坐标边界 | 越界坐标、所有坐标单位、极端Z值 | ✅ |
| 控制器边界 | 纯特效控制器不分裂、仅Fov变化保持Camera、NoteController选择器边界 | ✅ |
| 格式边界 | easing大小写不敏感 | ✅ |

### 4.4 回归测试覆盖详情

| 测试类别 | 测试项 | 状态 |
|---------|--------|------|
| 官方示例 | 单次导入导出、三次往返不退化、所有实体类型保留、模板完整保留 | ✅ |
| 模板系统 | 模板Keyframes相对时间、模板展开确定性 | ✅ |
| 时间数组 | 多条目、大量条目(50+)、负偏移量 | ✅ |
| NoteController | 具体note ID、空选择器、opacity_multiplier、fill_color/ring_color | ✅ |
| SceneController | glitch效果、bloom效果、scanline动画、arcade效果、ui_opacity闪烁 | ✅ |
| 视频对象 | 模板Keyframes应用 | ✅ |
| 特殊值 | 数值destroy值、scale快捷方式（与ScaleX/ScaleY共存） | ✅ |
| 格式健壮性 | "start:1134:2" 时间格式、混合坐标单位 | ✅ |
| 确定性 | 简单/复杂场景确定性输出、控制板ID一致性 | ✅ |
| 序列化规范 | 从不输出数值destroy、空lines数组保留、仅BaseState无Keyframes对象 | ✅ |

---

## 五、残余问题（P2 — 后续优化）

以下问题不影响核心导入导出功能，可在后续版本中处理：

| 编号 | 问题 | 影响 | 优先级 |
|------|------|------|--------|
| 1 | 嵌套 states 展平未实现 | 官方支持嵌套 states 语法，复杂场景可能丢失信息 | 🟢 P2 |
| 2 | ControllerState 缺少 artifact 系列属性 | 不影响常规使用，少数特效场景需要 | 🟢 P2 |
| 3 | ControllerState 缺少 scanline_smoothing | 扫描线平滑效果 | 🟢 P2 |
| 4 | SpriteState 多余属性 w/h | 导出时可能产生多余字段，不影响功能 | 🟢 P2 |
| 5 | Trigger 系统支持 | 官方支持 triggers 功能 | 🟢 P2 |
| 6 | TemplateState 中 Controller 属性重复 | 已修复编译错误，但需要评估设计合理性 | 🟢 P2 |

---

## 六、结论

经过系统分析和全面修复，项目的故事板数据逻辑已达到以下状态：

1. **格式兼容性**: ✅ 数据模型与官方规范在关键属性上完全对齐，NoteController 全属性补全，TextState 类型修正。

2. **序列化正确性**: ✅ float.MaxValue time 不再泄露到输出，$note 占位符被正确处理，UnitFloat 格式与官方一致。

3. **测试覆盖**: ✅ 建立了 134 项测试的完整体系，覆盖单元测试、集成测试、边界测试和回归测试。使用官方示例文件进行多次往返验证，确保数据不衰减。

4. **核心问题解决**: ✅ "保存后重新导入概率性报错"的三个根本原因（NoteController 属性缺失、float.MaxValue 序列化、$note 未处理）已全部修复并通过测试验证。

5. **残余风险**: 🟢 仅余 6 个 P2 级别的优化项，不影响核心导入导出功能的正确性和稳定性。