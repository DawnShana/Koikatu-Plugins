# Koikatu-Plugins

面向《恋活 / Koikatu》与 CharaStudio 的个人插件源码仓库。

本仓库当前整理三个插件。各插件目录均保留对应最新维护版本的源码、`build.bat` 与审计/说明文件，并提供独立中文使用教程。

| 插件 | 当前版本 | 运行环境 | 主要用途 |
|---|---:|---|---|
| [KK_TimelineStateCleaner](./KK_TimelineStateCleaner/) | 1.3.1 | CharaStudio | 一键取消/恢复指定 Timeline 相机与服装状态轨道勾选 |
| [KK_DragCoordinateLoadBridge](./KK_DragCoordinateLoadBridge/) | 1.2.3 | CharaStudio / Koikatu Maker | 将拖拽服装卡接入 Coordinate Load Option 的选择性加载流程 |
| [KKPEHeightLockStandalone](./KKPEHeightLockStandalone/) | 1.2.1 | CharaStudio | 锁定 `cf_n_height` 身高，并在替换角色时按模式保留体型 |

## 直接依赖版本

> 版本基线核对日期：**2026-08-20**。这里记录本仓库插件直接使用的第三方插件版本；依赖插件自身的传递依赖请按其官方发布包安装。

| 本仓库插件 | 直接依赖 | 插件 GUID | 依赖类型 | 推荐 / 审计版本 | 说明 |
|---|---|---|---|---:|---|
| KK_TimelineStateCleaner 1.3.1 | Timeline | `com.joan6694.illusionplugins.timeline` | **HardDependency** | **1.5.6** | 当前维护源码推荐基线；Cleaner 未在 `BepInDependency` 中声明最低版本 |
| KK_DragCoordinateLoadBridge 1.2.3 | DragAndDrop | `keelhauled.draganddrop` | SoftDependency（功能实际必需） | **1.3.1** | Bridge 自带二进制审计明确记录的目标版本 |
| KK_DragCoordinateLoadBridge 1.2.3 | Coordinate Load Option (CLO) | `com.jim60105.kk.coordinateloadoption` | SoftDependency（功能实际必需） | **21.12.25.1** | Bridge 自带二进制审计明确记录；内部 release 为 `1.1.8.2` |
| KKPEHeightLockStandalone 1.2.1 | KKPE | `com.joan6694.kkplugins.kkpe` | **HardDependency** | **2.21.5** | 当前维护源码推荐基线；本插件未声明最低版本，但 Height Lock 会访问 KKPE 部分内部结构 |

共同运行环境：**BepInEx 5.x**。`0Harmony.dll` 随 BepInEx 环境提供，Bridge 与 KKPEHeightLockStandalone 会使用它，但本仓库不单独锁定 Harmony 版本。

### 版本含义

- **审计版本**：Bridge 附件中的 `MAKER_BINARY_AUDIT.md` 对实际 DLL 做过结构审计，因此 `DragAndDrop 1.3.1 + CLO 21.12.25.1` 是 Bridge 1.2.3 延续使用的明确二进制基线；Bridge 不按版本号或 SHA/MVID 硬锁，只要结构契约兼容，其他版本也可能工作。
- **推荐版本**：Timeline / KKPE 的本仓库源码包没有附带当时编译所用的依赖 DLL，因此不能声称原编译机使用了某个无法核实的精确版本。本仓库以当前维护源码中可核对的 `Timeline 1.5.6`、`KKPE 2.21.5` 作为推荐排错基线。

详细说明：

- [Timeline Cleaner 依赖说明](./KK_TimelineStateCleaner/DEPENDENCIES.md)
- [Drag Coordinate Load Bridge 依赖说明](./KK_DragCoordinateLoadBridge/DEPENDENCIES.md)
- [KKPE Height Lock 依赖说明](./KKPEHeightLockStandalone/DEPENDENCIES.md)

依赖维护来源：

- Timeline / KKPE：<https://github.com/IllusionMods/HSPlugins>
- DragAndDrop：<https://github.com/IllusionMods/DragAndDrop>
- Coordinate Load Option：<https://github.com/jim60105/KK/releases>

## 仓库原则

- 以各插件目录中的源码和 `build.bat` 为维护基线。
- 不修改恋活、CharaStudio 或依赖插件的原始 DLL。
- 三个插件相互独立；只有功能上需要的第三方插件才是运行依赖。
- `build.bat` 均按 Koikatu / CharaStudio 的 Unity Mono / NET35 运行环境设计，不使用 NuGet 或 `dotnet restore`。
- 普通用户如果只使用插件，应优先安装已经编译好的 DLL；源码与 `build.bat` 面向开发、维护和本地编译。

## 目录

```text
Koikatu-Plugins/
├─ KK_TimelineStateCleaner/
├─ KK_DragCoordinateLoadBridge/
└─ KKPEHeightLockStandalone/
```

具体安装、依赖、快捷键、配置、构建和排错方法请进入各插件目录查看 `README.md`。
