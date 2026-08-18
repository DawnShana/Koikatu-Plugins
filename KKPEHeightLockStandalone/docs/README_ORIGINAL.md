# KKPEHeightLockStandalone v1.2.1 SIMPLIFIED

本版重点：**拒绝过度防御编程，保留必要的可控逻辑。**

## 控制

- `Ctrl + Shift + H`：身高锁定 开 / 关
- `Ctrl + Shift + B`：体型保留模式循环
- `Ctrl + Shift + F9`：显示 / 隐藏窗口

F9 是为了避免与 `KK_TimelineStateCleaner` 默认的 `Ctrl + Shift + F8` 冲突。

体型模式：

- `Off`：替换角色时不保留
- `ShapeOnly`：只保留 `shapeValueBody`
- `AllBody`：保留 `shapeValueBody + bustSoftness + bustWeight`

## 身高锁定的简单所有权规则

- 如果 `cf_n_height` 在插件介入前已经有 KKPE scale correction：
  - 插件不接管；
  - 关闭插件身高锁时也不删除它。
- 如果原来没有：
  - 插件调用 KKPE `SetBoneScale` 创建 scale correction；
  - 该 correction 由本插件拥有；
  - 关闭时调用 `EditableValue.Reset()` + KKPE `SetBoneNotDirtyIf()` 恢复锁定前 scale。

不再做：

- LastValue 浮点比较；
- “用户后来改了 scale 就转移所有权”的推断；
- epsilon 判断；
- 对公开 API 的多层反射；
- 不存在的 `ChaFileBody.typeBone` 兼容。

## 反射范围

只反射 KKPE 真正的私有实现：

- `BonesEditor._target`
- `BonesEditor._dirtyBones`
- `BonesEditor.SetBoneScale`
- `BonesEditor.SetBoneNotDirtyIf`
- 私有 `TransformData.scale`

以下公开类型/API直接使用：

- `GenericOCITarget.type / ociChar`
- `HSPE.EditableValue<Vector3>`
- `ChaFileBody.shapeValueBody`
- `ChaFileBody.bustSoftness`
- `ChaFileBody.bustWeight`
- `ChaControl.UpdateShapeBodyValueFromCustomInfo`
- `ChaControl.UpdateBustSoftnessAndGravity`

## 安全边界

源码不修改恋活 EXE/DLL 原文件。

`build.bat` 不自动安装，只在源码目录生成：

`KKPEHeightLockStandalone.dll`

BepInEx 会正常创建本插件自己的配置文件，这是插件配置，不是恋活原始程序文件。
