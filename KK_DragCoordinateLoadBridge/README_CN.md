# KK Drag Coordinate Load Bridge v1.2.3

把 DragAndDrop 的“拖入 Coordinate / 服装卡”动作接入 Coordinate Load Option（CLO）的选择性加载流程。

支持：

- CharaStudio
- Koikatu Maker / 角色编辑器

核心原则：**Bridge 只负责桥接拖卡入口，不重新实现换装，不修改 DragAndDrop/CLO 原 DLL，也不把外部服装卡强行写进游戏真实列表。**

## 2026-09-10：Studio 拖入服装卡预览空白修复

此前 CharaStudio 中从资源管理器拖入外部 Coordinate / 服装卡后，CLO 可以正确取得文件路径并打开选择性加载界面，但 Studio 原生服装卡预览仍为空白。

原因是两条逻辑彼此独立：

1. CLO 的 `OnSelectPostfix(...)` 只需要当前 `CharaFileSort.selectPath`，Bridge 的 detached `CharaFileSort` 已能满足这一点；
2. Studio 的预览图不会因为 `selectPath` 改变自动刷新，而是由 `MPCharCtrl.CostumeInfo.LoadImage(int)` 单独调用 `PngAssist.LoadTexture(...)`，再把 `imageThumbnail` 显示出来。

本次增加 `KK_DragCoordinatePreviewRefresh.cs`，仍编译进同一个 `KK_DragCoordinateLoadBridge.dll`：

- 仅在 CharaStudio 启用；
- 在 CLO 完成 `OnSelectPostfix(...)` 后、Bridge 尚未恢复真实 `fileSort` 的瞬间执行；
- 只识别 Bridge 创建的 detached `CharaFileInfo`（它没有真实 UI `node`）；
- 调用 Studio 自己的 `CostumeInfo.LoadImage(select)` 刷新预览；
- 普通 Studio 文件列表点击不会触发这条补偿逻辑；
- 预览刷新失败也不会中断 CLO 选择性加载。

因此该修复不会自行解析/改写 Coordinate PNG，也不会向 Studio 的真实服装卡列表注入临时项目。

## CharaStudio 工作流

1. 在 Studio 中选中角色。
2. 从资源管理器拖入一张 Coordinate / 服装卡。
3. Bridge 阻止 DragAndDrop 立即整套换装。
4. 自动进入 `anim -> 衣服 / Costume`。
5. 外部服装卡预览由 Studio 原生 `LoadImage` 链刷新。
6. CLO 打开“显示选择”。
7. 用户勾选要加载的部位。
8. 点击 Studio 原 Load。
9. CLO 自己执行选择性加载。

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

## 实现边界

Bridge 不会：

- 把外部 Coordinate 复制进 `UserData`；
- 向 Maker / Studio 真实文件列表注入项目；
- 修改 DragAndDrop / CLO 原 DLL；
- 创建第二套选择 UI；
- 自己写角色的 clothes/accessory 数据；
- 自己解析或改写拖入 Coordinate PNG；
- 锁死依赖 DLL 的 SHA/MVID。

Studio 的预览补偿只针对 CLO 的选择事件增加只读式 Postfix，并调用游戏已有的缩略图加载方法；它不改变 CLO 的实际选择性换装逻辑。

## 构建

运行：

```text
build.bat
```

输入或拖入 Koikatu 游戏根目录。脚本使用本机 .NET Framework `csc.exe` 与游戏 NET35/Mono 依赖，并同时编译：

```text
KK_DragCoordinateLoadBridge.cs
KK_DragCoordinatePreviewRefresh.cs
```

构建参数包括：

```text
/nostdlib+
/langversion:4
```

不使用 NuGet，也不调用 `dotnet restore`。成功后只发布到：

```text
releases\KK_DragCoordinateLoadBridge.dll
```

## 诊断

运行时日志：

```text
BepInEx\config\KK_DragCoordinateLoadBridge.runtime.log
```

Studio 预览补偿插件成功挂载时，BepInEx 日志会出现：

```text
Studio external-coordinate preview refresh hook installed.
```

## 自检说明

`self_check.py` 已增加本次预览修复的结构检查。仓库提交环境不包含用户本机 Koikatu 运行库，因此源码审查不能代替最终 Windows/Koikatu 依赖下的 DLL 编译与实机运行。
