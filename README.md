# Koikatu-Plugins

面向《恋活 / Koikatu》与 CharaStudio 的个人插件源码仓库。

本仓库当前整理三个插件。各插件目录均保留对应最新维护版本的源码、`build.bat` 与审计/说明文件，并提供独立中文使用教程。

| 插件 | 当前版本 | 运行环境 | 主要用途 |
|---|---:|---|---|
| [KK_TimelineStateCleaner](./KK_TimelineStateCleaner/) | 1.3.1 | CharaStudio | 一键取消/恢复指定 Timeline 相机与服装状态轨道勾选 |
| [KK_DragCoordinateLoadBridge](./KK_DragCoordinateLoadBridge/) | 1.2.3 | CharaStudio / Koikatu Maker | 将拖拽服装卡接入 Coordinate Load Option 的选择性加载流程 |
| [KKPEHeightLockStandalone](./KKPEHeightLockStandalone/) | 1.2.1 | CharaStudio | 锁定 `cf_n_height` 身高，并在替换角色时按模式保留体型 |

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
