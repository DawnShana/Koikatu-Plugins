# KKPEHeightLockStandalone v1.2.5 SOURCE-PROVEN

本版按 Koikatu / KKPE 源码执行链路实现身高锁定与角色替换时的体型保留，并将运行时控制彻底收敛到 **BepInEx ConfigurationManager（通常按 F1 打开）**。

## 控制方式：仅 F1 / ConfigurationManager

当前插件只注册两个可配置项：

- `Lock > HeightLockEnabled`：开启 / 关闭身高锁定；
- `Lock > BodyPreserveMode`：`Off` / `ShapeOnly` / `AllBody`。

v1.2.5 已从源码中删除旧控制入口及其配置注册：

- `Hotkey > HeightToggleKey`
- `Hotkey > BodyModeKey`
- `Hotkey > ToggleWindowKey`
- `Hotkey > RequireCtrl`
- `Hotkey > RequireShift`
- `General > ShowWindow`
- `General > ShowHotkeyToast`
- 插件自带 `OnGUI` 控制窗口
- 插件自带快捷键处理
- toast 提示逻辑

因此这些旧项目不会再出现在 F1 ConfigurationManager 页面中。旧版 `.cfg` 文件中即使仍残留同名文本项，新版本也不会再 `Config.Bind` 它们，因此不会注册到 ConfigurationManager，也不会影响插件运行。

关闭 `HeightLockEnabled` 时，插件仍保留 `Update()` 中的配置变更检测，并照常执行 `ReleaseAllAndRefresh()`，不会因为移除旧窗口/快捷键而破坏身高恢复逻辑。

## 身高锁定

身高锁仍使用：

`BonesEditor.ApplyBoneManualCorrection` 的 Harmony Postfix。

源码依据：

- `Manager.Character.LateUpdate` 的 Unity executionOrder = 0；
- `IKExecutionOrder.LateUpdate` 的 executionOrder = 9996；
- KKPE 的 `CharaPoseController` 在 `IKExecutionOrder.LateUpdate` Postfix 中调用各模块的 `IKExecutionOrderOnPostLateUpdate()`；
- `BonesEditor.IKExecutionOrderOnPostLateUpdate()` 调用 `ApplyBoneManualCorrection()`。

因此 HeightLock Postfix 在角色普通 LateUpdate、体型更新、FinalIK 和 KKPE bone correction 之后执行，适合作为当前帧最后阶段的 `cf_n_height.localScale` 写回。

实现保持简单：

- 第一次遇到角色：记录当前 `cf_n_height.localScale`；
- 后续每次 KKPE bone correction 完成后：写回记录值；
- 关闭：按当前人物卡参数刷新已捕获角色的身高，再清空本插件缓存；
- 再开启：重新捕获当前身高；
- 不访问 `_dirtyBones`；
- 不创建/Reset KKPE scale correction；
- 不做所有权/epsilon/LastValue 推断。

### v1.2.4 的关闭修正

关闭身高锁时，插件会先把当前人物的 `shapeValueBody` 重新同步到 `cf_n_height`，再清理锁定缓存。因此关闭后当前人物立即恢复人物卡身高，不会留下最后一次锁定写入的 Transform 缩放。

当体型保留模式为 `Off` 时，后续 `ChangeChara` 完成后也会立即执行同一套身体更新，确保新人物卡使用自己的身高参数。`ShapeOnly` / `AllBody` 仍按用户选择保留旧体型。

## 体型保留

Patch 最外层：

- `Studio.OCICharFemale.ChangeChara(string)`
- `Studio.OCICharMale.ChangeChara(string)`

模式：

- `Off`
- `ShapeOnly`
- `AllBody`

### v1.2.3 的关键修正

Koikatu 的 `UpdateShapeBodyValueFromCustomInfo()` 会：

1. 把 `shapeValueBody` 转换并写入 `sibBody`；
2. 设置 `updateShapeBody = true`。

它并不保证调用点立即完成实际骨骼 Transform 更新。

因此 v1.2.3 在恢复体型后明确执行：

```csharp
charInfo.UpdateShapeBodyValueFromCustomInfo();
charInfo.UpdateShapeBody();
```

`UpdateShapeBody()` 是 Koikatu 的公开方法，内部直接执行 `sibBody.Update()`，因此 `cf_n_height` 和其它体型骨骼会在当前 ChangeChara Postfix 内立即与恢复后的 `shapeValueBody` 对齐。

这样下一次 executionOrder=9996 的身高锁捕获到的是恢复后的最终身高，不是替换过程中的中间值。

同时删除了 v1.2.2 Postfix 末尾第二次 `HeightLockPatch.ClearForCharacter()`：Prefix 在角色重建前已经清除旧缓存；ChangeChara 是同步调用，Postfix 之前不会跨帧重新建立 HeightLock 状态，所以第二次清除没有必要。

## “关闭体型保留”的语义

`Off` 对后续 `ChangeChara` 生效。

它不会撤销一个已经完成的角色替换。这不是防御限制，而是该功能的数据流定义：插件只在 ChangeChara Prefix 保存旧体型，在 Postfix 决定是否写回。

## 构建

`build.bat`：

- `/noconfig`
- `/nostdlib+`
- `/langversion:4`
- 仅编译 `KKPEHeightLockStandalone.cs`
- 输出到仓库根目录 `releases\KKPEHeightLockStandalone.dll`
- 不自动安装
- 不修改恋活原文件
- 不使用系统缓存目录
