# v1.2.1 Maker Binary Audit

## 1. 审计输入

### Assembly-CSharp.dll

- SHA256: `361ee4c5a7dafbfa56845bb0a71a034ef388ce07663b7709b858136eb3486958`
- MVID: `cdaa1658-214e-4370-bd5b-16aee18cdad1`

### DragAndDrop.Koikatu.dll

- SHA256: `1a3d21b323c2d31a7ab4e36a0a9e35d31298594b22968679548043c10a822125`
- plugin version: `1.3.1`
- MVID: `a362e3a6-0de2-4d23-a2dd-677dd743766e`

### KK_CoordinateLoadOption.dll

- SHA256: `30fdb2d5601266a2e38b571a969d86003361f517912ee172a58540d5abc5a30b`
- plugin version: `21.12.25.1`
- internal release: `1.1.8.2`
- MVID: `f01cbe34-e9de-44da-9799-42e6838df45c`

> v1.2.1 继续采用运行时结构契约验证，不把这些 SHA/MVID 写成强制版本锁。上面的值用于说明本次设计依据的实际二进制。

---

## 2. DragAndDrop Maker 入口

实际类型：

```text
DragAndDrop.MakerHandler
```

实际入口：

```text
void Coordinate_Load(string path, DragAndDrop.POINT pos)
```

IL 确认原实现会立即：

1. 查找 Maker `CustomFileWindow`；
2. 读取原生 `tglCoordeLoadClothes / tglCoordeLoadAcs`；
3. 获取 `CustomBase.Instance.chaCtrl`；
4. 备份当前 clothes/accessory；
5. `chaCtrl.nowCoordinate.LoadFile(path)`；
6. 恢复未勾选的粗粒度部分；
7. `Reload(false, true, true, true)`；
8. `AssignCoordinate(...)`；
9. `CustomBase.Instance.updateCustomUI = true`。

因此 Bridge 对有效拖入必须在此入口 **Prefix suppress** 原实现；如果 Bridge 自己准备失败，也不能 fail-open 到完整换装。

---

## 3. Maker Coordinate 文件 UI

实际类型：

```text
ChaCustom.CustomCoordinateFile
```

关键字段：

```text
listCtrl   : ChaCustom.CustomFileListCtrl
fileWindow : ChaCustom.CustomFileWindow
```

关键行为：

- `Start()` 给 `btnCoordeLoadLoad` 注册原生加载逻辑；
- 原生加载逻辑调用 `listCtrl.GetSelectTopItem()`；
- 最终从 `selectTopItem.info.FullPath` 取得路径；
- 每帧更新逻辑根据 `listCtrl.GetSelectIndex()` 决定 Load 按钮是否可点击。

因此外部拖入卡如果不进入真实列表，原生 Update 会在下一帧再次禁用 Load 按钮。

v1.2.1 的处理：

- 不修改真实 `listCtrl`；
- 不复制外部文件；
- `LateUpdate()` 仅在当前 CLO pending 路径仍归 Bridge 所有时维持已有 Load 按钮。

---

## 4. Maker 原生菜单导航

### CustomChangeMainMenu

实际类型：

```text
ChaCustom.CustomChangeMainMenu : UI_ToggleGroupCtrl
```

关键字段：

```text
ccSystemMenu : ChaCustom.CustomChangeSystemMenu
```

实际 `ChangeWindowSetting(int)` IL 的 switch 共 7 个分支，index `6` 对应 `ccSystemMenu`。

v1.2.1 直接使用这个已经审计到的游戏语义 index `6`，不再通过 `Transform` 父子层级猜测主菜单项。场景 Transform 层级来自序列化资源，不属于 Assembly-CSharp 代码契约；继续猜层级反而会增加误拒绝。

### UI_ToggleGroupCtrl

实际字段：

```text
items : UI_ToggleGroupCtrl.ItemInfo[]
```

ItemInfo：

```text
tglItem : UnityEngine.UI.Toggle
cgItem  : UnityEngine.CanvasGroup
```

基类 `Start()` 对每个 `tglItem.OnValueChanged` 注册监听；选择某项后会：

- 其他 `cgItem` -> `CanvasGroupExtensions.Enable(false, false)`；
- 当前 `cgItem` -> `CanvasGroupExtensions.Enable(true, false)`。

实际 `CanvasGroupExtensions.Enable` IL 确认选中时设置：

```text
alpha = 1
interactable = true
blocksRaycasts = true
```

v1.2.1 设置真实 `Toggle.isOn=true` 后，会验证：

- `GetSelectIndex()` 确实指向目标项；
- 目标 CanvasGroup 已变为可见/可交互状态。

这样可以避免把“Toggle 值已经改了，但 Start/UniRx 监听尚未接好”误判为 UI 已就绪。

### CustomChangeSystemMenu

实际类型：

```text
ChaCustom.CustomChangeSystemMenu : UI_ToggleGroupCtrl
```

关键字段：

```text
fileWindow : ChaCustom.CustomFileWindow[]
types      : ChaCustom.CustomFileWindow.FileWindowType[]
```

其 Toggle listener 实际执行：

```text
fileWindow[item.idx].fwType = types[item.idx]
```

`FileWindowType.CoordinateLoad` 的实际数值在本次审计二进制中为 `3`，但 v1.2.1 不再把这个数值作为运行时硬锁，只按枚举名 `CoordinateLoad` 取得实际枚举值。

v1.2.1 运行时通过：

```text
fileWindow[i] == 当前 CustomCoordinateFile.fileWindow
AND
types[i] == CoordinateLoad(3)
```

找到真正的 Coordinate Load Toggle，再设置其 `isOn=true`。

Bridge 不再采用早期草案中的：

```text
fileWindow.gameObject.SetActive(true)
fileWindow.fwType = CoordinateLoad
fileWindow.UpdateWindow()
```

因此 Maker UI 的父级 CanvasGroup、其他菜单关闭逻辑和原生事件都由游戏自己的 Toggle 链处理。

---

## 5. CLO Maker 分支

实际 CLO `Awake()` 确认会 Patch：

```text
ChaCustom.CustomCoordinateFile.Start          -> Patches.InitPostfix
ChaCustom.CustomCoordinateFile.OnChangeSelect -> Patches.OnSelectPostfix
```

### InitPostfix

Maker 分支会取得 `CustomCoordinateFile.fileWindow`，并初始化 CLO 面板/Load 行为。

v1.2.1 不再每次拖卡读取 Harmony patch table 来证明 `InitPostfix/OnSelectPostfix` 是否仍挂在游戏方法上。这个检查不能证明 Button listener 的最终状态，反而可能误拒绝兼容环境。

Bridge 改看实际运行状态：CLO `panel` 已初始化、CLO 静态 `CustomFileWindow` 与当前 Maker `fileWindow` 相同、手动 `OnSelectPostfix(proxy)` 后 `coordinatePath` 能回读为拖入路径。Bridge 仍不自己调用 CLO Load。

### v1.2.1 UI 顺序修正

v1.2.0 先要求 CLO panel 已存在，再尝试进入 `System -> Coordinate Load`。v1.2.1 改为先走 Maker 真实 Toggle 链打开 Coordinate Load，再检查 CLO panel/binding；这样不会因为 UI 的惰性初始化顺序形成无意义等待。

### OnSelectPostfix(object)

Maker 分支通过反射从传入对象读取：

```text
fileWindow
listCtrl
```

再调用：

```text
listCtrl.GetSelectTopItem()
```

并从返回对象的：

```text
info.FullPath
```

取得 Coordinate 路径写入 `Patches.coordinatePath`。

因此 v1.2.1 使用 detached proxy：

```text
MakerSelectProxy.fileWindow -> 真实 CustomFileWindow
MakerSelectProxy.listCtrl   -> 临时 MakerListCtrlProxy
    GetSelectTopItem()
        -> info.FullPath = 外部拖入路径
```

CLO 同步完成解析后 proxy 不再参与游戏状态。

### OnClickLoadPostfix

实际 Maker 分支确认：

- `panel.IsActive() == false`：走完整 Maker Coordinate 加载；
- `panel.IsActive() == true`：进入 CLO selective `MakeTmpChara/ChangeCoordinate` 路径。

因此 v1.2.0 在启用 Maker Load 按钮前必须：

1. `panel.gameObject.SetActive(true)`；
2. 调用实际 `UIBehaviour.IsActive()`；
3. 只有返回 `true` 才把 Load 按钮保持可用。

---

## 6. v1.2.1 Patch 边界

同一运行进程只选择一个 DragAndDrop 目标：

```text
CharaStudio -> StudioHandler.Coordinate_Load(List<string>, POINT)
Koikatu     -> MakerHandler.Coordinate_Load(string, POINT)
```

源码只有一个 `HarmonyInstance.Patch(...)` 调用点。

Maker 分支：

- 不 Patch CLO；
- 不 Patch Assembly-CSharp Maker 类；
- 不写角色 Coordinate 数据；
- 不修改 Maker 真实列表；
- 不复制 Coordinate 文件；
- 不创建第二 UI。

---

## 7. 尚不能由静态二进制审查证明的部分

以下必须实机验证：

1. 用户当前 Maker 场景的序列化 `ccSystemMenu/items/fileWindow/types` 引用是否与审计结构一致；
2. 实际插件集中是否有其他插件在 CLO `InitPostfix` 之后再次重写 Maker Load Button listeners；
3. 其他 UI 插件是否动态修改 Maker Toggle/CanvasGroup 层级；
4. CLO 的选择性加载与用户当前全部扩展插件组合的最终运行效果。

v1.2.1 对这些情况采用必要的结构验证和 fail-closed，但不能对未知第三方插件做绝对兼容保证。
