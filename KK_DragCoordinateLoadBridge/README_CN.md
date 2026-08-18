# KK Drag Coordinate Load Bridge v1.2.1

v1.2.1 在 v1.2.0 Maker 支持基础上做一次“拒绝过度防御”修订。Studio 工作流保持不变；Maker 删除了不必要的 Transform 层级猜测、枚举数值硬锁和 Harmony patch-table gate，并调整为先进入原生 Coordinate Load UI、再等待 CLO UI 就绪。

## v1.2.1 相对 v1.2.0

- `System` 主菜单直接使用已审计的游戏语义 index `6`，不再猜 `Transform` 父子层级；
- `CoordinateLoad` 通过枚举名匹配，不再要求底层整数必须等于 `3`；
- Maker 不再检查 CLO 的 Harmony patch table，改看实际 `panel / CustomFileWindow / coordinatePath` 运行状态；
- 先通过原生 Toggle 打开 `System -> Coordinate Load`，再检查 CLO panel 是否就绪；
- 保留 fail-closed、busy、last-drop-wins、`panel.IsActive()`、Load Button LateUpdate 等真正必要的保护。

详见 `SELF_REVIEW_1.2.1.md`。

## 目标工作流

### CharaStudio

保持 v1.1.6 的现有行为：

1. 选中角色；
2. 从资源管理器拖入单张 Coordinate 卡；
3. Bridge 阻止 DragAndDrop 立即整套换装；
4. 自动进入 `anim -> 衣服/Costume`；
5. 自动让 Coordinate Load Option（CLO）解析拖入卡并显示选择面板；
6. 用户勾选部位后点击 Studio 原 Load 按钮；
7. CLO 自己执行选择性加载。

### Koikatu Maker

1. 进入恋活角色编辑器；
2. 直接拖入外部 Coordinate 卡；
3. Bridge 阻止 `DragAndDrop.MakerHandler.Coordinate_Load(...)` 的立即完整换装；
4. Bridge 通过 Maker **真实的 Toggle UI 链**切换到：
   - 主菜单 `System`；
   - `Coordinate Load`；
5. 不把外部文件复制进 `UserData`，也不向 Maker 的真实 `listCtrl` 注入项目；
6. 用一个只在 CLO 同步解析调用期间存在的 detached proxy，把拖入文件路径交给 CLO Maker 分支；
7. 自动显示 CLO “显示选择”面板；
8. Bridge 维持 Maker 原 Load 按钮可用；
9. 用户点击 Maker 原 Load 按钮，由 CLO 自己执行选择性加载。

## Maker 实现边界

v1.2.1 不重新实现换装，也不直接写角色服装/饰品数组。

Maker 分支只 Patch 一个入口：

```text
DragAndDrop.MakerHandler.Coordinate_Load(string, POINT)
```

Studio 分支仍然只 Patch：

```text
DragAndDrop.StudioHandler.Coordinate_Load(List<string>, POINT)
```

由于 CharaStudio 与 Koikatu 是不同进程，同一运行实例只会安装其中一个 Patch。源码中也只有一个 `HarmonyInstance.Patch(...)` 调用点。

Bridge **不会**：

- Patch CLO 的方法；
- Patch `CustomCoordinateFile` / `CustomChangeMainMenu` / `CustomChangeSystemMenu`；
- 创建第二套选择 UI；
- 修改 DragAndDrop/CLO 原 DLL；
- 自动复制拖入 Coordinate 到 `UserData`；
- 修改 Maker 真实 Coordinate 文件列表。

## 为什么 Maker Load 按钮需要持续维持

实际 `Assembly-CSharp.dll` 中，`CustomCoordinateFile` 每帧会根据真实 `listCtrl` 是否存在选中项目重算 `btnCoordeLoadLoad.interactable`。

外部拖入卡没有被加入真实列表，因此如果 Bridge 只把按钮启用一次，下一帧就会再次被 Maker 禁用。

所以 v1.2.1 与 Studio 分支相同，在 `LateUpdate()` 中只维护**现有原生 Load 按钮的 interactable 状态**；它不创建新按钮，也不代替用户点击 Load。

## Maker 多文件拖入

Maker 的 DragAndDrop 接口是单文件：

```text
Coordinate_Load(string, POINT)
```

因此一次拖入多张卡时会表现为连续多次调用。v1.2.1 采用 **last drop wins**：

- 所有有效 Coordinate 调用都阻止立即完整换装；
- 后到的卡覆盖前一张 pending 路径；
- 最终 CLO 面板对应最后一张拖入卡。

## 构建

运行 `build.bat`，输入或拖入 Koikatu 游戏根目录。

脚本会自动选择：

```text
Koikatu_Data\Managed
```

如果不存在，则尝试：

```text
CharaStudio_Data\Managed
```

构建仍使用本机 .NET Framework `csc.exe`、游戏自带 `mscorlib/System/UnityEngine`、BepInEx 与 0Harmony：

- 不使用 NuGet；
- 不使用 `dotnet restore`；
- `/nostdlib+`；
- `/langversion:4`。

成功后自动安装到：

```text
BepInEx\plugins\KK_DragCoordinateLoadBridge\KK_DragCoordinateLoadBridge.dll
```

## 首次 Maker 实机测试

建议只用一张已知可正常加载的 Coordinate 卡测试：

1. 启动 `Koikatu`；
2. 进入角色编辑器；
3. 确认 F1 ConfigurationManager 中存在 `KK Drag Coordinate Load Bridge`；
4. 查看：
   - `Runtime status` 应为 `ENABLED - Koikatu Maker drag hook installed`；
   - `Observed coordinate drops` 初始为 0；
5. 从资源管理器拖入一张 Coordinate；
6. 预期自动进入 `System -> Coordinate Load`；
7. 预期 CLO 选择面板自动显示；
8. `Observed coordinate drops` 增加；
9. `Last action` 应出现 `PREPARED - Maker CLO selection open; Load armed`；
10. 勾选部位并点击 Maker 原 Load 按钮。

如果失败，请提供：

```text
BepInEx\config\KK_DragCoordinateLoadBridge.runtime.log
```

以及 F1 中：

```text
Runtime status
Observed coordinate drops
Last action
```

## 当前验证级别

本源码已经对用户提供的实际二进制做 Maker 专项静态/IL 审查：

- `Assembly-CSharp.dll`
- `DragAndDrop.Koikatu.dll`
- `KK_CoordinateLoadOption.dll`

并通过源码静态自检。

当前生成环境没有 Windows `csc.exe`、Koikatu/CharaStudio Unity 运行时，因此：

> **不声称 v1.2.1 已在本环境完成编译或 Maker 实机运行。**

实际 Maker UI 的序列化对象引用、其他第三方插件对 Toggle/Load Button 的修改，仍必须由你的实际插件环境最终验证。
