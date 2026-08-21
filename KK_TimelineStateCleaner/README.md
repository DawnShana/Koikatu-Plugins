# KK Timeline State Cleaner

当前版本：**v1.3.1**

用于 CharaStudio 的 Timeline 状态清理插件。它不会删除轨道或关键帧，而是把指定的 Timeline 状态轨道从当前场景中取消勾选；再次执行同一个操作时，可恢复到本次清理之前的状态。

## 功能

默认处理以下内置 Timeline 轨道。

### 相机

- Camera Origin Position
- Camera Origin Rotation
- Camera Position
- Camera Rotation
- Camera Zoom
- Camera FOV

对应源码中的轨道 ID：

```text
cameraOPos
cameraORot
cameraPos
cameraRot
cameraOZoom
cameraFOV
```

### 服装

- Top
- Bottom
- Bra
- Panties
- Legwear
- Shoes Inside
- Shoes Outside

插件不会处理 Gloves、Pantyhose，也不会处理其他插件注册的 Timeline 轨道。

## 清理 / 恢复逻辑

主操作是一个双向切换：

1. 第一次执行：取消目标轨道的勾选。
2. 第二次执行：恢复本次清理前被插件实际修改的轨道。
3. 后续继续在“清理 / 恢复”之间切换。

恢复不是把所有目标轨道强制勾上。清理前本来就未勾选的轨道不会被错误恢复为勾选状态。

如果清理后切换或重载了场景，旧恢复点会被识别为失效；下一次执行会直接对当前场景重新清理。

## 默认快捷键

- `Ctrl + Shift + Backspace`：切换 **清理 / 恢复**
- `Ctrl + Shift + F8`：显示 / 隐藏插件窗口

左右 Ctrl、Shift 均可。

窗口内也提供：

- `切换：清理 / 恢复`
- `隐藏窗口`
- 标题栏右上角 `X`

隐藏窗口不会卸载插件，快捷键仍可继续使用。

## 依赖

运行环境：

- Koikatu / CharaStudio
- BepInEx
- Timeline

插件只加载于：

```text
CharaStudio
```

Timeline 为硬依赖，插件 GUID：

```text
com.joan6694.illusionplugins.timeline
```

## 安装

普通用户将编译好的：

```text
KK_TimelineStateCleaner.dll
```

放到：

```text
BepInEx\plugins\KK_TimelineStateCleaner\
```

然后完全退出并重新启动 CharaStudio。

请避免在其他 `BepInEx\plugins` 子目录中保留旧版本同名 DLL，否则可能发生重复加载。

## 配置

首次运行后，BepInEx 会生成本插件配置。也可以通过 ConfigurationManager 修改。

主要配置项：

```text
[Hotkey]
MainKey = Backspace
ToggleWindowKey = F8
RequireCtrl = true
RequireShift = true

[General]
ShowWindow = true
ShowHotkeyToast = true
```

`ShowWindow` 只控制启动时是否显示窗口；在运行中点击 `X` 或“隐藏窗口”不会永久改写这个设置。

## 源码构建

目录中已经包含最新 `build.bat`。

### 使用方法

最简单的方法：

1. 双击 `build.bat`。
2. 输入恋活游戏根目录，或把游戏根目录拖到命令行窗口中。
3. 脚本检查 `CharaStudio.exe`、游戏 Managed 目录、BepInEx 与 Timeline。
4. 使用本机 .NET Framework `csc.exe` 编译。
5. 编译成功后只发布 DLL 到仓库根目录的 `releases\` 文件夹，不自动安装。

也可以直接传参：

```bat
build.bat "D:\Games\Koikatu"
```

构建脚本使用：

- `CharaStudio_Data\Managed\mscorlib.dll`
- `System.dll`
- `System.Core.dll`（存在时）
- `Assembly-CSharp.dll`
- `UnityEngine.dll`
- `BepInEx.dll`
- `Timeline.dll`

关键编译选项：

```text
/noconfig
/nostdlib+
/langversion:4
```

构建脚本不会写入恋活目录、BepInEx 插件目录或系统缓存目录。

这套方式是为了匹配 Koikatu / CharaStudio 的 Unity Mono / NET35 运行环境，避免错误引用桌面 .NET 4 标准库。

## 非侵入边界

v1.3.1：

- 不修改 `Timeline.dll`
- 不删除 Timeline 轨道
- 不删除关键帧
- 不向 Timeline 原生 UI 注入控件
- 不使用 Harmony Patch Timeline
- 不使用 Reflection
- 只调用 Timeline 公共 API / 公共类型
- 恢复记录只保存在插件自己的运行时内存中

## 常见问题

### 按快捷键没有反应

检查：

1. Timeline 是否正常加载。
2. 是否在 CharaStudio，而不是 Koikatu Maker。
3. 是否存在重复的旧版 `KK_TimelineStateCleaner.dll`。
4. BepInEx 控制台 / LogOutput.log 是否有依赖加载错误。
5. ConfigurationManager 中快捷键是否被改过。

### 清理后重新载入场景，再按一次为什么没有恢复旧场景

这是设计行为。恢复点只对应清理时的场景 / Timeline 集合；切换场景后旧恢复点会失效，插件会直接清理当前场景。

### 会不会删除 Timeline 数据

不会。插件只切换指定轨道的启用/勾选状态，不删除轨道和关键帧。

## 附件原始说明

本次整理前压缩包内原始 `README.md` 已保留在：

```text
docs/README_ORIGINAL.md
```

源码、`build.bat` 与 `RELEASE_LAYOUT.txt` 来自 v1.3.1 最新附件。
