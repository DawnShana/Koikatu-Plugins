# KK_DragCoordinateLoadBridge 依赖版本

适用于：**KK_DragCoordinateLoadBridge v1.2.3**

版本基线核对日期：**2026-08-20**。

## 直接依赖

| 依赖 | 审计版本 | GUID / 文件 | BepInEx 元数据 | 实际用途 |
|---|---:|---|---|---|
| BepInEx | **5.x** | `BepInEx.dll` | — | 运行框架 |
| 0Harmony | 随 BepInEx 5 环境提供 | `0Harmony.dll` | — | Patch DragAndDrop Coordinate Load 入口 |
| DragAndDrop | **1.3.1** | `keelhauled.draganddrop` / `DragAndDrop.Koikatu.dll` | SoftDependency | **Bridge 功能实际必需** |
| Coordinate Load Option (CLO) | **21.12.25.1** | `com.jim60105.kk.coordinateloadoption` / `KK_CoordinateLoadOption.dll` | SoftDependency | **Bridge 功能实际必需** |

CLO 审计二进制另外记录：

```text
internal release: 1.1.8.2
```

## 为什么这两个版本可以明确写成审计版本

本仓库保留的 `MAKER_BINARY_AUDIT.md` 对实际二进制给出了明确记录：

```text
DragAndDrop.Koikatu.dll
plugin version: 1.3.1

KK_CoordinateLoadOption.dll
plugin version: 21.12.25.1
internal release: 1.1.8.2
```

Bridge v1.2.3 的 README 也明确说明：v1.2.3 **未改变这套核心二进制契约**。因此推荐首先使用：

```text
DragAndDrop 1.3.1
Coordinate Load Option 21.12.25.1
```

## SoftDependency 不等于“可不用”

源码声明：

```csharp
[BepInDependency(DragAndDropGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(CoordinateLoadOptionGuid, BepInDependency.DependencyFlags.SoftDependency)]
```

但 Bridge 的业务目标就是把 DragAndDrop 接到 CLO，所以运行时会主动检查两个插件是否已经加载。任意一个缺失时，对应桥接功能不会启用。

使用 SoftDependency 的目的主要是让 Bridge 自己执行兼容性检查和诊断，而不是表示这两个插件在功能上“可有可无”。

## 是否只能使用精确版本

不是。Bridge 有意**不锁死**：

- DLL SHA256；
- MVID；
- 精确版本号相等判断。

它基于真正使用到的类型、方法、字段和 UI 契约检查兼容性。因此其他版本只要结构契约不变，也可能正常工作。

但是出现 Maker / Studio 拖卡异常时，排错第一基线应回到：

```text
DragAndDrop 1.3.1 + CLO 21.12.25.1
```

维护来源：

- DragAndDrop：<https://github.com/IllusionMods/DragAndDrop>
- Coordinate Load Option：<https://github.com/jim60105/KK/releases>
