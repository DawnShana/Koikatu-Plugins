# KK Drag Coordinate Load Bridge v1.2.3

把 DragAndDrop 的“拖入 Coordinate / 服装卡”动作接入 Coordinate Load Option（CLO）的选择性加载流程。

支持：

- CharaStudio
- Koikatu Maker / 角色编辑器

核心原则：**Bridge 只负责桥接拖卡入口，不重新实现换装，不修改 DragAndDrop/CLO 原 DLL，也不为了理论风险增加大型状态机。**

## v1.2.3 修复重点

v1.2.3 延续 v1.2.2 的 Maker 修复，并继续处理首次进入 Coordinate Load 时的 UI 生命周期问题：

1. CLO 的真实 `CoordinateLoadBtn` 必须已经 `activeInHierarchy == true`，Bridge 才会调用它；如果原生 Coordinate Load 层级还没真正激活，只做现有的短 deferred retry。
2. 当 CLO `coordinatePath` 已经离开 Bridge 的拖入路径时，Bridge 立即释放 ownership，并取消自己强制保持的 Load 按钮状态，之后交回 Maker 原生 UI 更新。
3. 删除 v1.2.2 中没有直接功能价值的 Maker `CanvasGroup alpha/interactable/blocksRaycasts` readiness gate，避免把 UI 外观/布局状态当作功能契约。

## CharaStudio 工作流

1. 在 Studio 中选中角色。
2. 从资源管理器拖入一张 Coordinate / 服装卡。
3. Bridge 阻止 DragAndDrop 立即整套换装。
4. 自动进入 `anim -> 衣服 / Costume`。
5. CLO 打开“显示选择”。
6. 用户勾选要加载的部位。
7. 点击 Studio 原 Load。
8. CLO 自己执行选择性加载。

Studio 分支保持 v1.1.6 以来的工作流，不因 Maker 支持重新实现。

## Koikatu Maker 工作流

1. 进入角色编辑器。
2. 从资源管理器拖入一张 Coordinate 卡。
3. Bridge suppress `DragAndDrop.MakerHandler.Coordinate_Load(...)` 原来的立即完整换装。
4. 通过 Maker 原生 Toggle 链进入 `System -> Coordinate Load`。
5. detached proxy 只在 CLO 同步解析期间提供外部拖入路径，不修改 Maker 真实 Coordinate 列表。
6. 等待 CLO 自己的“显示选择”按钮真正处于活动层级后，调用其原 `Button.onClick.Invoke()`。
7. 用户勾选服装 / 饰品部位。
8. 点击 Maker 原 Load。
9. CLO 自己执行选择性加载。

## Maker 实现边界

Bridge 不会：

- 把外部 Coordinate 复制进 `UserData`；
- 向 Maker 真实 `listCtrl` 注入项目；
- Patch CLO；
- Patch `CustomCoordinateFile` / `CustomFileWindow` / `ChaControl`；
- 创建第二套选择 UI；
- 自己写角色的 clothes/accessory 数据；
- 锁死依赖 DLL 的 SHA/MVID；
- 排斥其他 Harmony owner。

## 为什么需要维持原 Load 按钮

Maker 原 `CustomCoordinateFile` 会根据真实 `listCtrl` 是否有选中项目持续重算 `btnCoordeLoadLoad.interactable`。外部拖入卡不进入真实列表，所以 Bridge 只在当前 pending 拖入路径仍归自己所有、CLO selective panel 仍有效时，于 `LateUpdate()` 维持**原生 Load 按钮**可用。

这不会替代用户点击 Load。

## 多文件拖入

Maker 的 DragAndDrop 接口是单文件：

```text
Coordinate_Load(string, POINT)
```

同一批连续拖入多张卡时采用 **last drop wins**：后到路径覆盖旧 pending，原完整换装仍全部 suppress。

## 构建

运行：

```text
build.bat
```

输入或拖入 Koikatu 游戏根目录。脚本使用本机 .NET Framework `csc.exe` 与游戏 NET35/Mono 依赖：

```text
/nostdlib+
/langversion:4
```

不使用 NuGet，也不调用 `dotnet restore`。

成功后安装到：

```text
BepInEx\plugins\KK_DragCoordinateLoadBridge\KK_DragCoordinateLoadBridge.dll
```

## 诊断

运行时日志：

```text
BepInEx\config\KK_DragCoordinateLoadBridge.runtime.log
```

Maker 正常拖卡应看到类似：

```text
Maker coordinate drop intercepted; original DragAndDrop whole-coordinate load will be suppressed: ...
[MakerAdapter] Invoked CLO Maker's real Show Selection button.
[MakerAdapter] Prepared dropped coordinate and armed Maker Coordinate Load button: ...
```

## 当前自检

```text
47 / 47 PASS
```

这是源码静态/结构检查，不代表本仓库提交环境已经运行 Windows `csc.exe` 或 Koikatu/CharaStudio 实机。

相关资料：

- `SELF_REVIEW_1.2.3.md`
- `RUNTIME_FIX_1.2.3.md`
- `SELF_CHECK_RESULT.txt`
- `MAKER_BINARY_AUDIT.md`（实际二进制审计基线；v1.2.3 未改变其核心二进制契约）
