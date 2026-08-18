# KKPEHeightLockStandalone

当前版本：**v1.2.1 SIMPLIFIED**

CharaStudio 独立插件，用于保留 KKPE 身高 / 体型相关状态，不依赖 VR 插件、KKAPI、Timeline API 或 MaterialEditor。

核心功能：

1. 锁定 `cf_n_height`，避免姿势 / 动画覆盖角色身高。
2. 在 Studio 替换角色时，按设置保留原角色体型。

## 功能 1：身高锁定

插件针对：

```text
cf_n_height
```

维持 KKPE 的 scale correction，使当前角色身高不会被姿势或动画重新覆盖。

### 所有权规则

v1.2.1 使用简化且明确的规则：

- 如果插件介入前 `cf_n_height` 已存在 KKPE scale correction：
  - 插件不会接管它；
  - 关闭身高锁时也不会删除它。
- 如果原本没有 correction：
  - 插件通过 KKPE `SetBoneScale` 创建；
  - 该 correction 由本插件拥有；
  - 关闭身高锁时调用 `EditableValue.Reset()` 并执行 KKPE 清理，恢复锁定前的 scale。

因此，插件不会为了“锁身高”覆盖用户原本手动设置的 KKPE scale correction。

## 功能 2：替换角色时保留体型

插件 Patch `Studio.OCIChar.ChangeChara`，在角色替换前后保存并恢复体型参数。

支持三种模式：

- `Off`：不保留
- `ShapeOnly`：只保留 `shapeValueBody`
- `AllBody`：保留 `shapeValueBody + bustSoftness + bustWeight`

窗口中的 `AllBody` 对应“体型+胸部”。

## 默认快捷键

- `Ctrl + Shift + H`：身高锁定 开 / 关
- `Ctrl + Shift + B`：循环切换体型保留模式
- `Ctrl + Shift + F9`：显示 / 隐藏窗口

F9 用于避开 `KK_TimelineStateCleaner` 默认的 `Ctrl + Shift + F8`。

左右 Ctrl、Shift 均可。

插件窗口标题：

```text
KKPE Height / Body Lock v1.2.1
```

窗口可以直接切换：

- 身高锁定
- 关闭体型保留
- 仅体型
- 体型+胸部

## 依赖

运行环境：

- Koikatu / CharaStudio
- BepInEx
- 0Harmony
- KKPE

插件只加载于：

```text
CharaStudio
```

KKPE 为硬依赖，GUID：

```text
com.joan6694.kkplugins.kkpe
```

本插件不依赖：

- VR
- KKAPI
- Timeline API
- MaterialEditor

## 安装

普通用户将编译好的：

```text
KKPEHeightLockStandalone.dll
```

放到：

```text
BepInEx\plugins\KKPEHeightLockStandalone\
```

也可以放在其他独立插件子目录，但应确保只存在一份同名 DLL。

启动 CharaStudio 后，如果 KKPE 依赖正常，插件窗口默认显示。

## 配置

主要配置项：

```text
[Lock]
HeightLockEnabled = true
BodyPreserveMode = ShapeOnly

[Hotkey]
HeightToggleKey = H
BodyModeKey = B
ToggleWindowKey = F9
RequireCtrl = true
RequireShift = true

[General]
ShowWindow = true
ShowHotkeyToast = true
```

`HeightLockEnabled` 默认开启。

`BodyPreserveMode` 默认：

```text
ShapeOnly
```

如果插件无法找到 KKPE 身高锁所需的内部成员，会自动关闭 Height Lock，但 Body Preserve 仍可继续工作。

## 源码构建

目录中包含最新 `build.bat`。

### 使用方法

1. 双击 `build.bat`。
2. 输入或拖入恋活游戏根目录。
3. 脚本读取 CharaStudio / BepInEx / KKPE 依赖。
4. 使用本机 .NET Framework `csc.exe` 编译。
5. 成功后在当前源码目录生成：

```text
KKPEHeightLockStandalone.dll
```

也可以：

```bat
build.bat "D:\Games\Koikatu"
```

### 注意

与另外两个插件的构建脚本不同，**KKPEHeightLockStandalone 的 `build.bat` 不会自动安装 DLL**。

编译成功后需要手动复制到：

```text
BepInEx\plugins\KKPEHeightLockStandalone\
```

构建使用：

- CharaStudio 自带 NET35 / Mono 程序集
- `BepInEx.dll`
- `0Harmony.dll`
- `KKPE.dll`
- `Assembly-CSharp.dll`
- `UnityEngine.dll`

关键选项：

```text
/noconfig
/nostdlib+
/langversion:4
```

## 实现边界

v1.2.1 SIMPLIFIED 的设计重点是减少过度防御逻辑：

- 仅 2 个 Harmony Patch
- 不使用 `typeBone`
- 不使用 LastValue 推断
- 不做 epsilon 浮点所有权判断
- 关闭插件自有身高锁时执行真实 Reset / KKPE cleanup
- 不覆盖预先存在的 KKPE scale correction
- `ChangeChara` 时会释放旧角色的身高状态
- 体型刷新使用公开 API

反射只用于 KKPE 必须访问的私有实现，公开类型 / API 直接调用。

## 常见问题

### 身高锁打开但某个角色没有被插件接管

如果该角色的 `cf_n_height` 在插件介入前已经存在 KKPE scale correction，插件会尊重已有设置，不覆盖它。这是 v1.2.1 的设计行为。

### 关闭身高锁会不会删掉我手动设置的 KKPE correction

不会。只有由本插件创建并拥有的 correction 才会在关闭时 Reset / 清理。

### 替换角色后想只保留身体滑块，不保留胸部软硬 / 重量

将 `BodyPreserveMode` 设为：

```text
ShapeOnly
```

### 是否需要 VR 插件

不需要。

## 审计资料

附件中的最终自检文件保留为：

```text
SELF_AUDIT.txt
```

原始压缩包 `README.md` 保留在：

```text
docs/README_ORIGINAL.md
```
