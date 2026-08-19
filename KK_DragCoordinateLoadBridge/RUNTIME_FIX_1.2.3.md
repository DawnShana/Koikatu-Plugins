# v1.2.3 Runtime Follow-up

本文件记录 v1.2.2 再复查后与“第一次 Load 可能出现默认服装 / 状态漂移”直接相关的两处修正。

## 1. Show Selection 必须等原生 Coordinate Load 真正 active

v1.2.2 已经调用 CLO 自己的 `CoordinateLoadBtn.onClick`，但只验证 Button 对象存在。

Maker 的 `CustomFileWindow.Start()` 是协程；`fwType` 的 `UpdateWindow` 订阅可能晚于 Toggle 值变化完成。此时：

```text
fwType == CoordinateLoad
Button 对象存在
但 CoordinateLoad hierarchy 尚未 activeInHierarchy
```

CLO 的 Show Selection 按钮内部会对 `CoordinateLoad/Select` 调用 `GetComponentsInChildren<Toggle>()` 并全勾，以准备 `tmpChara` 完整读取。

v1.2.3 在调用按钮前要求：

```text
showSelectionButton.gameObject.activeInHierarchy == true
```

否则只返回 `UiNotReady`，由现有 deferred retry 稍后重试。

## 2. coordinatePath 漂移时不留下 Bridge 强制启用的按钮

v1.2.2 发现 CLO path 离开拖入路径时只清 pending 字段，可能让此前 Bridge 设置的 `interactable=true` 留到下一次 Maker Update。

v1.2.3 改为：

```text
path drift
-> clear Bridge ownership
-> disable the bridge-armed Load button
-> native Maker Update owns subsequent state
```

## 3. 没有增加额外 Hook

本次没有增加 Harmony patch，也没有接管 CLO Load。Maker 仍只依赖：

```text
DragAndDrop.MakerHandler.Coordinate_Load(string, POINT)
```

作为唯一拦截入口。
