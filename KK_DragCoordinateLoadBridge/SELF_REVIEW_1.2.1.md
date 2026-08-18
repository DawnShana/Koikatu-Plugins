# v1.2.1 自检与“拒绝过度防御”审查

## 结论

v1.2.0 没发现会直接修改角色数据或破坏 Studio v1.1.6 主流程的新增代码，但 Maker 分支有 4 个值得修正的问题，其中 3 个属于“为了防未来/猜布局而增加的脆弱检查”，1 个属于真实的 UI 生命周期顺序风险。

v1.2.1 只修改 Maker 分支；Studio `CoordinateLoadOptionAdapter` 与 v1.2.0 保持原样。

## 修正 1：删除 Transform 层级猜测

v1.2.0 为了避免硬编码 System 主菜单索引，尝试根据：

`ccSystemMenu.transform` 是否位于某个 `items[i].cgItem.transform` 下方

来推断 System Toggle。

这个关系属于 Unity 场景布局，不是 Assembly-CSharp 代码契约；仅审 DLL 不能证明序列化层级一定如此。实际代码已经明确 `CustomChangeMainMenu.ChangeWindowSetting(6)` 对应 `ccSystemMenu`。

v1.2.1 直接使用实际语义：

`MainSystemIndex = 6`

仍通过真实 Toggle.isOn 触发游戏自己的 UI 链，不直接 SetActive 子窗口。

## 修正 2：删除 CoordinateLoad 数值 3 的运行时硬锁

实际目标版本中 `FileWindowType.CoordinateLoad == 3` 是事实，但 Bridge 并不需要数值 3。

v1.2.0 同时要求枚举名存在、底层数值必须等于 3，属于重复防御。

v1.2.1 只：

- `Enum.Parse(..., "CoordinateLoad")`
- 用枚举对象相等比较 `types[i]` 和 `fwType`

不再对底层整数做额外版本锁。

## 修正 3：删除 Maker Harmony patch-table gate

v1.2.0 每次拖卡会检查 CLO 是否仍 Harmony Patch 了：

- `CustomCoordinateFile.Start`
- `CustomCoordinateFile.OnChangeSelect`

但 Bridge 本身并不调用这两个游戏方法；而且 patch-table 存在也不能证明当前 Load Button listener 没被其他插件改写。

因此这个检查既可能误拒绝兼容环境，又不能真正证明最终按钮行为。

v1.2.1 改为检查实际运行状态：

- CLO `panel` 已存在；
- CLO 静态 `CustomFileWindow` 已绑定当前 Maker `fileWindow`；
- 手动调用 `OnSelectPostfix(proxy)` 后 `coordinatePath` 必须回读为拖入路径；
- `panel.IsActive()` 必须为 true 后才启用原 Load Button。

这些都是 Bridge 真正依赖的状态。

## 修正 4：先打开原生 Coordinate Load，再要求 CLO panel 就绪

v1.2.0 的顺序是：

1. 找 CLO panel；
2. panel 不存在就重试；
3. 之后才打开 Maker System -> Coordinate Load。

如果 CLO/Unity 的某些 UI 初始化依赖 Coordinate Load 窗口先进入可见状态，这个顺序可能形成等待死锁。

v1.2.1 改为：

1. 通过 Maker 真实 Toggle 链打开 System -> Coordinate Load；
2. 再检查 CLO panel / CLO window binding；
3. 未就绪才做短暂重试。

这不是增加防御，而是把生命周期顺序改成更自然的宿主 UI 顺序。

## 明确保留的必要保护

以下没有删除：

- 有效 Coordinate 进入 Bridge 后不 fail-open 到 DragAndDrop 整套换装；
- CLO 正在异步加载时不改 `coordinatePath`；
- 新拖卡取消旧的 deferred retry（last-drop-wins）；
- `coordinatePath` 回读必须等于拖入路径；
- `panel.IsActive()` 必须与 CLO 自己选择性加载分支一致；
- Maker 原生 Update 会反复禁用 Load，因此 Bridge 只在 pending 路径仍有效时用 LateUpdate 维持原按钮；
- 不修改真实 `listCtrl`，不复制文件，不新增 UI，不 Patch CLO/Assembly-CSharp。

这些直接防止“突然整套换装”或让功能根本无法点击，不属于理论性防御。

## 删除的无必要代码

MakerAdapter 中删除了仅用于 patch-table 审查的：

- `_coordinateFileStartMethod`
- `_coordinateFileOnChangeSelectMethod`
- `_initPostfix`
- `_onClickLoadPostfix`
- `HasRequiredCloMakerPatches`
- Maker 自己重复的一套 `ContainsPatchMethod/GetPatchMethod/SameMethod`
- `FindOwningToggleIndex`

## 自检结果

`self_check.py`：31/31 PASS。

这仍然不是 Windows csc 编译结果或 Koikatu 实机结果；当前容器没有游戏运行环境。
