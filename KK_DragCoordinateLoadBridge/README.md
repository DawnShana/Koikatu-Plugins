# KK Drag Coordinate Load Bridge v1.2.3

把 DragAndDrop 的“拖入 Coordinate / 服装卡”动作接入 Coordinate Load Option（CLO）的选择性加载流程。

支持：

- CharaStudio
- Koikatu Maker / 角色编辑器

核心原则：**Bridge 只负责桥接拖卡入口，不重新实现换装，不修改 DragAndDrop/CLO 原 DLL，也不把外部服装卡强行写进游戏真实列表。**

## 更新日志

### 2026-09-12：修复预览图闪现后再次空白

实机测试确认，第一版 Studio 预览补偿会先正确显示外部服装卡缩略图，但随后又恢复为空白。进一步核对 Studio 原生 `MPCharCtrl.CostumeInfo` 后确认：`LoadImage(int)` 会把 `imageThumbnail.color` 设为 `Color.white`，而后续 `CostumeInfo.InitList()` 在列表刷新时又会执行 `imageThumbnail.color = Color.clear`。

因此当前预览辅助逻辑升级为持久化维护：

- 外部拖卡时仍只调用一次 Studio 原生 `CostumeInfo.LoadImage(select)`；
- 缓存原生加载得到的 `imageThumbnail.texture`；
- CLO 当前 `coordinatePath` 仍指向这张外部卡时，在 `LateUpdate()` 中保持同一个 RawImage 可见；
- 如果 UI 只把 texture 清成 `null`，恢复已经加载好的 texture，不重新读取 PNG；
- 如果 texture 被替换成另一个非空纹理，视为正常 Studio 预览已经接管，立即停止维护；
- CLO 路径改变时立即释放预览维护状态；
- 不在每帧调用 `LoadImage()`，避免反复触发 `Resources.UnloadUnusedAssets()` / `GC.Collect()`。

预览辅助插件同时标记为不在 ConfigurationManager 中显示，避免 F1 页面出现无用的内部辅助条目。

### 2026-09-10：Studio 拖入服装卡预览空白修复

此前 CharaStudio 中从资源管理器拖入外部 Coordinate / 服装卡后，CLO 可以正确取得文件路径并打开选择性加载界面，但 Studio 原生服装卡预览仍为空白。

原因是两条逻辑彼此独立：

1. CLO 的 `OnSelectPostfix(...)` 只需要当前 `CharaFileSort.selectPath`，Bridge 的 detached `CharaFileSort` 已能满足这一点；
2. Studio 的预览图不会因为 `selectPath` 改变自动刷新，而是由 `MPCharCtrl.CostumeInfo.LoadImage(int)` 单独调用 `PngAssist.LoadTexture(...)`，再把 `imageThumbnail` 显示出来。

因此增加 `KK_DragCoordinatePreviewRefresh.cs`，仍编译进同一个 `KK_DragCoordinateLoadBridge.dll`：

- 仅在 CharaStudio 启用；
- 在 CLO 完成 `OnSelectPostfix(...)` 后、Bridge 尚未恢复真实 `fileSort` 的瞬间执行；
- 只识别 Bridge 创建的 detached `CharaFileInfo`（它没有真实 UI `node`）；
- 调用 Studio 自己的 `CostumeInfo.LoadImage(select)` 加载预览；
- 普通 Studio 文件列表点击不会触发 detached 条目逻辑；
- 预览异常不会中断 CLO 选择性加载。

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
5. 外部服装卡预览由 Studio 原生 `LoadImage` 加载，并在当前 CLO 外部路径有效期间保持显示。
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

## Maker 实现边界

Bridge 不会：

- 把外部 Coordinate 复制进 `UserData`；
- 向 Maker 真实 `listCtrl` 注入项目；
- 修改 DragAndDrop / CLO 原 DLL；
- Patch `CustomCoordinateFile` / `CustomFileWindow` / `ChaControl` 的换装实现；
- 创建第二套选择 UI；
- 自己写角色的 clothes/accessory 数据；
- 锁死依赖 DLL 的 SHA/MVID；
- 排斥其他 Harmony owner。

Studio 的预览补偿只针对 CLO 的外部拖卡选择事件调用一次游戏已有缩略图加载方法，随后只维护已经加载好的 RawImage 状态；它不改变 CLO 的实际选择性换装逻辑。

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

现在会同时编译：

```text
KK_DragCoordinateLoadBridge.cs
KK_DragCoordinatePreviewRefresh.cs
```

不使用 NuGet，也不调用 `dotnet restore`。成功后只发布到仓库根目录：

```text
releases\KK_DragCoordinateLoadBridge.dll
```

构建脚本不会写入恋活目录、BepInEx 插件目录或系统缓存目录。

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

Studio 预览持久化插件成功挂载时，BepInEx 日志会出现：

```text
Studio external-coordinate preview persistence hook installed.
```

## 自检说明

原 Bridge 的既有静态检查仍可通过 `self_check.py` 执行。当前自检同时验证：原生 `LoadImage` 只调用一次、LateUpdate 不重复读取 PNG、CLO 路径改变时释放预览状态，以及原生 Studio 新纹理接管时停止干预。仓库提交环境不包含用户本机 Koikatu 运行库，因此最终 DLL 仍应使用 `build.bat` 在实际游戏目录依赖下编译。

相关资料：

- `SELF_REVIEW_1.2.3.md`
- `RUNTIME_FIX_1.2.3.md`
- `SELF_CHECK_RESULT.txt`
- `MAKER_BINARY_AUDIT.md`
