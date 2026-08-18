# KK Timeline State Cleaner v1.3.1

## v1.3.1：同一个快捷键切换“清理 / 恢复”

主快捷键现在是一个真正的切换操作：

- 第一次按 `Ctrl + Shift + Backspace`：一键取消指定 Timeline 轨道勾选；
- 再按一次：恢复到本次清理前的状态；
- 再按一次：再次清理；
- 之后按同一个快捷键在“清理 / 恢复”之间循环。

窗口中不再提供独立的“恢复清理前状态”按钮。

窗口只保留一个 `切换：清理 / 恢复` 操作按钮，它与主快捷键执行完全相同的逻辑，并显示“下一次操作”是清理还是恢复。

## 恢复规则

恢复不是把所有目标轨道强制勾上，而是恢复 Cleaner 本次实际修改过的状态：

- 清理前已经勾选、被 Cleaner 取消的轨道会进入恢复记录；
- 清理前本来就没有勾选的轨道不会被误勾选；
- 用户如果在清理后手动把某个原本勾选的轨道重新勾上，恢复时会识别它已经处于原状态；
- 完成恢复后，恢复记录清空，下一次主快捷键重新执行清理。

如果清理后切换或重载了场景，旧恢复点会被识别为失效。此时再次按主快捷键不会先做一次无意义的旧场景恢复，而会直接清理当前场景。

## 窗口关闭 / 隐藏

Cleaner 独立窗口提供：

- 标题栏右上角 `X`：隐藏窗口；
- `隐藏窗口` 按钮：隐藏窗口；
- `Ctrl + Shift + F8`：显示 / 隐藏 Cleaner 窗口。

隐藏窗口不会卸载插件，主快捷键仍然有效。

## 默认快捷键

- `Ctrl + Shift + Backspace`：切换 **清理 / 恢复**；
- `Ctrl + Shift + F8`：显示 / 隐藏窗口。

Ctrl/Shift 左右键都可以。

## 清理范围

相机：

- Camera Origin Position
- Camera Origin Rotation
- Camera Position
- Camera Rotation
- Camera Zoom
- Camera FOV

服装：

- Top
- Bottom
- Bra
- Panties
- Legwear
- Shoes Inside
- Shoes Outside

不会处理 Gloves、Pantyhose，也不会处理其他插件注册到 Timeline 的轨道。

## 非侵入边界

- 无 Harmony Patch Timeline
- 无 Reflection
- 无 private/internal 访问
- 不修改 Timeline.dll
- 不向 Timeline 原生 UI 注入控件
- 不删除轨道
- 不删除关键帧
- 只调用 Timeline 公共 API/公共类型
- 恢复状态只保存在 Cleaner 自己的运行时内存中

## 编译

`build.bat` 延续已验证可用的 NET35 / Unity Mono 编译策略：

1. 拖入或输入恋活游戏根目录；
2. 使用 CharaStudio 自己的 `mscorlib.dll / System.dll / System.Core.dll`；
3. 引用当前安装的 `BepInEx.dll / Timeline.dll / Assembly-CSharp.dll / UnityEngine.dll`；
4. 编译 `KK_TimelineStateCleaner.dll`；
5. 自动复制到：

`BepInEx\plugins\KK_TimelineStateCleaner\KK_TimelineStateCleaner.dll`

编译后请完全退出并重新启动 CharaStudio。

## 普通用户发布

正式发布时普通用户只需要：

`KK_TimelineStateCleaner.dll`

放入：

`BepInEx\plugins\KK_TimelineStateCleaner\`

普通用户不需要源码，也不需要运行 `build.bat`。
