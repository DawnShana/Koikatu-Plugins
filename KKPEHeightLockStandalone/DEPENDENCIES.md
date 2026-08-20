# KKPEHeightLockStandalone 依赖版本

适用于：**KKPEHeightLockStandalone v1.2.3 SOURCE-PROVEN**

版本基线核对日期：**2026-08-20**。

## 直接依赖

| 依赖 | 版本基线 | GUID / 文件 | 类型 |
|---|---:|---|---|
| BepInEx | **5.x** | `BepInEx.dll` | 运行框架 |
| 0Harmony | 随 BepInEx 5 环境提供 | `0Harmony.dll` | Patch 库 |
| KKPE | **2.21.5** | `com.joan6694.kkplugins.kkpe` / `KKPE.dll` | **HardDependency** |

KKPE 为硬依赖，但本插件源码没有声明最低版本号。因此：

- KKPE **必须安装并成功加载**；
- BepInEx 元数据没有写死 `>= 2.21.5` 或 `== 2.21.5`；
- **KKPE 2.21.5** 是本仓库当前推荐兼容 / 排错基线。

## v1.2.3 对 KKPE 的实际使用范围

当前 Height Lock 不再访问或修改 KKPE `_dirtyBones`，也不再调用 `SetBoneScale`、`SetBoneNotDirtyIf` 或维护 scale correction 所有权。

它只反射读取 KKPE `BonesEditor` 的私有 `_target` 字段，用来取得当前 `GenericOCITarget` / `OCIChar`，然后在 `BonesEditor.ApplyBoneManualCorrection` 的 Harmony Postfix 中把已捕获的 `cf_n_height.localScale` 写回。

因此与 v1.2.1 相比，v1.2.3 对 KKPE 内部结构的耦合明显降低。

## 为什么仍推荐 KKPE 2.21.5

截至本说明核对日期，`IllusionMods/HSPlugins` 当前维护源码中的 KKPE / PoseEditor 版本常量为：

```text
2.21.5
```

该版本也是本次源码执行链审计的参考基线，包括：

- `BonesEditor.ApplyBoneManualCorrection()`；
- `CharaPoseController` 的 IKExecutionOrder post-LateUpdate 调用链；
- `GenericOCITarget` 角色目标结构。

## Koikatu / CharaStudio 侧

体型保留直接使用游戏公开 API：

- `ChaFileBody.shapeValueBody`
- `ChaFileBody.bustSoftness`
- `ChaFileBody.bustWeight`
- `ChaControl.UpdateShapeBodyValueFromCustomInfo()`
- `ChaControl.UpdateShapeBody()`
- `ChaControl.UpdateBustSoftnessAndGravity()`

不通过反射访问这些公开体型 API。

## KKPE 自身的依赖

当前维护版 KKPE 自身还依赖 ExtensibleSave、KKAPI 等组件。这些属于 KKPE 的**传递依赖**，建议直接按 HSPlugins / 对应整合包完整安装。

`KKPEHeightLockStandalone` 本身不直接调用 KKAPI，也不依赖 VR、Timeline API 或 MaterialEditor。

维护来源：<https://github.com/IllusionMods/HSPlugins>
