# KK Drag Coordinate Load Bridge

当前版本：**v1.2.1**

把 DragAndDrop 的“拖入 Coordinate / 服装卡”动作接入 Coordinate Load Option（CLO）的选择性加载流程。

目标不是重新实现换装，也不是修改 DragAndDrop 或 CLO，而是让“拖拽服装卡”也能使用原生 / CLO 的部位选择逻辑。

支持：

- CharaStudio
- Koikatu Maker

## 依赖

运行时需要：

- BepInEx
- 0Harmony
- DragAndDrop
- Coordinate Load Option（CLO）

源码声明的软依赖 GUID：

```text
DragAndDrop:
keelhauled.draganddrop

Coordinate Load Option:
com.jim60105.kk.coordinateloadoption
```

虽然 BepInEx 元数据使用 SoftDependency，但 Bridge 的目标工作流实际依赖这两个插件；缺失时 Bridge 会拒绝启用对应桥接功能并记录诊断状态。

## CharaStudio 使用方法

1. 在 Studio 中选中一个角色。
2. 从资源管理器拖入一张 Coordinate / 服装卡。
3. Bridge 阻止 DragAndDrop 立即执行“整套完整换装”。
4. 自动进入角色的 `anim -> 衣服 / Costume` 页面。
5. Bridge 把拖入文件路径交给 CLO 的 Studio 选择逻辑。
6. CLO 打开“显示选择”面板。
7. 勾选需要加载的服装 / 饰品部位。
8. 点击 Studio 原有的 `Load` 按钮。
9. 由 CLO 自己执行选择性加载。

Bridge 不创建第二套换装界面，也不直接写角色服装 / 饰品数组。

## Koikatu Maker 使用方法

1. 启动 `Koikatu`。
2. 进入角色编辑器 Maker。
3. 从资源管理器直接拖入一张 Coordinate 卡。
4. Bridge 阻止 DragAndDrop 立即完整换装。
5. 自动进入原生：
   - `System`
   - `Coordinate Load`
6. Bridge 用临时 proxy 把外部路径交给 CLO Maker 分支。
7. CLO 自动打开“显示选择”面板。
8. 勾选要加载的部位。
9. 点击 Maker 原生 `Load` 按钮。
10. CLO 执行选择性加载。

### Maker 的重要边界

Bridge 不会：

- 把拖入文件复制进 `UserData`
- 向 Maker 真实 Coordinate 列表注入项目
- 修改真实文件列表
- 创建新的 Load 按钮
- Patch CLO 方法
- 修改 DragAndDrop / CLO 原 DLL

由于 Maker 原生 `CustomCoordinateFile` 会每帧根据真实列表选择状态重新计算 Load 按钮是否可点击，而外部拖入卡并不存在于真实列表中，因此 Bridge 会在 `LateUpdate()` 中维持**原生 Load 按钮**的可用状态。

这只是保持现有按钮可点，不会替代用户点击。

## Maker 多文件拖入

Maker 的 DragAndDrop 接口是单文件调用。

如果一次拖入多张卡，v1.2.1 使用：

```text
last drop wins
```

也就是：

- 每个有效 Coordinate 调用都会阻止立即完整换装；
- 后到的卡覆盖前一张 pending 路径；
- 最终 CLO 面板对应最后一张拖入卡。

建议日常一次只拖一张，行为最直观。

## 安装

将编译好的：

```text
KK_DragCoordinateLoadBridge.dll
```

放到：

```text
BepInEx\plugins\KK_DragCoordinateLoadBridge\
```

并确保 DragAndDrop 与 Coordinate Load Option 已正确安装。

替换 DLL 后需要重新启动当前 Koikatu / CharaStudio 进程。

## 配置与诊断

主要配置：

```text
[General]
Enabled = true

[Diagnostics]
Verbose logging = true
Runtime status = ...
Last action = ...
Observed coordinate drops = 0
```

其中：

- `Enabled`：启用 / 禁用 Bridge，修改后建议重启进程。
- `Runtime status`：当前 Hook / 初始化状态。
- `Last action`：最近一次桥接动作。
- `Observed coordinate drops`：本次进程观测到的 Coordinate 拖入次数。
- `Verbose logging`：输出更详细诊断。

运行时诊断文件：

```text
BepInEx\config\KK_DragCoordinateLoadBridge.runtime.log
```

如果安装了 BepInEx ConfigurationManager，可在 F1 配置界面查看这些字段。

### 正常状态示例

Studio：

```text
ENABLED - CharaStudio drag hook installed
```

Maker：

```text
ENABLED - Koikatu Maker drag hook installed
```

Maker 成功准备拖入卡后，`Last action` 应出现类似：

```text
PREPARED - Maker CLO selection open; Load armed
```

## 源码构建

目录中已经包含最新 `build.bat`。

### 使用方法

1. 双击 `build.bat`。
2. 输入或拖入 Koikatu 游戏根目录。
3. 脚本优先寻找：

```text
Koikatu_Data\Managed
```

如果不存在，再尝试：

```text
CharaStudio_Data\Managed
```

4. 使用本机 .NET Framework `csc.exe` 编译。
5. 成功后自动删除插件目录中旧的同名 Bridge DLL，并安装新 DLL 到：

```text
BepInEx\plugins\KK_DragCoordinateLoadBridge\KK_DragCoordinateLoadBridge.dll
```

也可以：

```bat
build.bat "D:\Games\Koikatu"
```

构建不使用：

- NuGet
- `dotnet restore`

关键选项：

```text
/noconfig
/nostdlib+
/langversion:4
```

构建时引用游戏自己的：

- `mscorlib.dll`
- `System.dll`
- `UnityEngine.dll`

以及：

- `BepInEx.dll`
- `0Harmony.dll`

Bridge 对 DragAndDrop / CLO 的运行时连接通过动态检查完成，因此构建脚本不直接把它们作为编译引用。

## 首次 Maker 实机验证建议

使用一张已知能够正常加载的 Coordinate 卡：

1. 启动 Koikatu Maker。
2. 确认 ConfigurationManager 中有 `KK Drag Coordinate Load Bridge`。
3. 查看 `Runtime status` 是否为 Maker hook installed。
4. 确认 `Observed coordinate drops = 0`。
5. 拖入一张卡。
6. 应自动进入 `System -> Coordinate Load`。
7. CLO 选择面板应显示。
8. `Observed coordinate drops` 应增加。
9. 选择部位后点击 Maker 原 Load。

如果失败，优先提供：

```text
BepInEx\config\KK_DragCoordinateLoadBridge.runtime.log
```

以及 F1 中这三项：

```text
Runtime status
Observed coordinate drops
Last action
```

## 非侵入边界

v1.2.1 的核心原则：

- Studio 只 Patch DragAndDrop 的 Studio Coordinate Load 入口
- Maker 只 Patch DragAndDrop 的 Maker Coordinate Load 入口
- 不 Patch CLO
- 不修改原 DLL
- 不重新实现换装
- 不创建第二套选择 UI
- 不复制外部卡进 UserData
- 不注入 Maker 真实 Coordinate 列表

CharaStudio 与 Koikatu 是不同进程，所以同一个运行实例只会安装当前进程需要的入口 Patch。

## 附带审计资料

本目录保留附件中的：

- `README_CN.md`
- `SELF_CHECK_RESULT.txt`
- `SELF_REVIEW_1.2.1.md`
- `MAKER_BINARY_AUDIT.md`
- `self_check.py`

其中 `SELF_CHECK_RESULT.txt` 的源码静态自检结果为 31/31 PASS；原附件同时明确说明当前生成环境没有完成 Windows `csc.exe` 编译或 Maker 实机运行，因此最终运行兼容性仍应以你的实际游戏环境验证为准。
