# v1.2.3 自我复查：Maker 生命周期与“拒绝过度防御”

## 结论

对 v1.2.2 再次沿 Maker 原生 UI、CLO Show Selection、pending Load Button 三条链复查后，发现 2 个值得修的真实问题，并删除 1 个已经没有必要的防御性结构检查。

本轮没有新增 Harmony Hook，没有 Patch CLO / Assembly-CSharp，也没有增加角色、服装、饰品事务状态机。

---

## Bug 1：CLO Show Selection Button“对象存在”不等于“Maker Coordinate Load 已真正进入可用状态”

v1.2.2 已经从 `panel.SetActive(true)` 改成调用 CLO 自己的 `CoordinateLoadPanel/CoordinateLoadBtn.onClick`，方向是正确的。

但仍有一个 Maker 首次打开窗口的生命周期缝隙：

```text
System / Coordinate Load Toggle 已切换
-> fwType 数值已经变成 CoordinateLoad
-> CLO 创建的 Button 对象也已经存在
-> 但 CustomFileWindow.Start() 的 fwType -> UpdateWindow 订阅可能仍在初始化
-> 原生 CoordinateLoad 层级尚未 activeInHierarchy
```

公开反编译代码显示 `CustomFileWindow.Start()` 本身是协程，会先 `WaitUntil(CustomBase.lstFileList != null)`，之后才订阅 `_fwType` 并调用 `UpdateWindow()`。`UpdateWindow()` 才负责真正激活 `objCoordinateLoad` 并触发 `eventUpdateWindow`。

与此同时，CLO Maker 的“显示选择”按钮会执行：

```text
CoordinateLoad/Select.GetComponentsInChildren<Toggle>()
-> 全部 isOn = true
```

且源码注释明确说明这一步用于让 `tmpChara` 完整加载。如果在 CoordinateLoad 父层级仍 inactive 时过早调用按钮，`GetComponentsInChildren<Toggle>()` 默认不会包含 inactive 子对象，可能导致原生粗粒度 Toggle 没有被打开。

### v1.2.3 修复

在调用真实 Show Selection Button 之前，只增加一个直接的运行条件：

```csharp
showSelectionButton.gameObject.activeInHierarchy == true
```

若还未 active，则返回 `UiNotReady`，交给现有短 deferred retry。等原生 Coordinate Load 真正激活后，再调用 CLO 按钮。

这不是额外状态机；它直接验证“准备调用的真实按钮当前是否属于活动的原生窗口”。

---

## Bug 2：coordinatePath 漂移时 v1.2.2 放弃 ownership，但可能留下 Bridge 刚刚强制启用的 Load Button

v1.2.2 在 pending 期间如果发现：

```text
CLO.coordinatePath != _preparedPath
```

会清空：

```text
_preparedPath
_preparedLoadButton
```

但不会撤销 Bridge 自己刚刚设置的：

```text
LoadButton.interactable = true
```

如果路径漂移发生在真实 Maker/CLO UI 状态切换附近，就可能留下一个短暂的“路径已经不是拖入卡，但按钮仍保持 Bridge 强制可点”的窗口。

### v1.2.3 修复

路径漂移时：

```text
保存当前 Button 引用
-> 清除 Bridge pending ownership
-> 将这个 Button 设为不可点
-> 后续状态完全交回 Maker 自己的 Update
```

如果用户确实手动选中了真实列表项，Maker 原生 Update 会按真实列表选择重新计算按钮，不需要 Bridge 继续干涉。

---

## 删除的过度防御：CanvasGroup readiness gate

v1.2.2 的 `SelectNativeToggle()` 除了验证：

```text
Toggle.isOn
GetSelectIndex() == 目标 index
```

还要求目标 Item 的 `CanvasGroup` 同时满足：

```text
alpha > 0.5
interactable == true
blocksRaycasts == true
```

在 v1.2.3 中这组检查已删除，原因是后续已经有更贴近真实工作流的验证：

```text
CoordinateLoad fwType == CoordinateLoad
CLO Show Selection Button.activeInHierarchy == true
CLO panel.IsActive() == true
```

继续把 CanvasGroup 的视觉/交互参数作为硬门槛，只会增加对 UI 布局/美化插件的脆弱性，却不能额外证明 CLO 路径或加载逻辑正确。

因此 v1.2.3 不再反射 `cgItem`，不再依赖 `CanvasGroup` 数值。

---

## 有意没有修改的部分

### 不在 CLO busy 时“消费”pending path

复查时曾考虑在 `tmpChaCtrl != null` 后立即清除 `_preparedPath`，但这会破坏 CLO 自己的合法二次 Load 流程。

CLO 在绑定饰品等情况下可能第一次 Load 后中止，并提示用户检查选项后再次按 Load。外部拖入卡没有真实 listCtrl 选择项，因此 Bridge 必须在 CLO busy 结束后仍能把原 Load Button 重新维持为可用。

因此保留：

```text
busy -> 临时禁用 Load
busy 结束 -> 若 coordinatePath 仍是拖入卡且 panel 仍 active，则重新允许 Load
```

这不是过度防御，而是保持 CLO 原有工作流可继续。

---

## Patch / 状态边界仍然不变

- 同一进程仅 Patch 一个 DragAndDrop Coordinate_Load 入口；
- 不 Patch CLO；
- 不 Patch CustomCoordinateFile / CustomFileWindow / ChaControl；
- 不修改真实 Maker listCtrl；
- 不复制拖入文件；
- 不自己读取并写入服装数组；
- 不保存 tgls/tgls2/boundAcc 大型事务快照；
- 不排斥未知 Harmony owner；
- 不做 DLL SHA/MVID 强锁。

---

## 自检结果

`self_check.py`：

```text
47 / 47 PASS
```

包括新增检查：

- Show Selection 等待真实原生 Coordinate Load hierarchy active；
- path drift 会禁用 Bridge-armed Load Button 再释放 ownership；
- 不再依赖 Maker CanvasGroup readiness gate；
- CLO busy 时不错误消费 pending external path。

当前环境仍没有执行 Windows `csc.exe` 和 Koikatu Maker 实机运行，因此 47/47 是源码静态/结构检查，不冒充编译或运行结果。
