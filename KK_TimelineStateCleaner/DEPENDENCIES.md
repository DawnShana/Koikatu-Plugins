# KK_TimelineStateCleaner 依赖版本

适用于：**KK_TimelineStateCleaner v1.3.1**

版本基线核对日期：**2026-08-20**。

## 直接依赖

| 依赖 | 版本基线 | GUID / 文件 | 类型 |
|---|---:|---|---|
| BepInEx | **5.x** | `BepInEx.dll` | 运行框架 |
| Timeline | **1.5.6** | `com.joan6694.illusionplugins.timeline` / `Timeline.dll` | **HardDependency** |

源码中的依赖声明没有指定最低版本号：

```csharp
[BepInDependency(TimelinePluginGuid, BepInDependency.DependencyFlags.HardDependency)]
```

因此：

- Timeline **必须安装并成功加载**；
- 本插件没有在 BepInEx 元数据层面写死 `>= 1.5.6` 或 `== 1.5.6`；
- **Timeline 1.5.6** 是本仓库当前明确记录的推荐兼容 / 排错基线；
- 原始 v1.3.1 源码包未携带编译时使用的 `Timeline.dll`，所以不能把 1.5.6 描述成“原编译机已证实的精确版本”。

## 为什么推荐 Timeline 1.5.6

截至本说明核对日期，`IllusionMods/HSPlugins` 当前维护源码中的 Timeline 插件版本常量为：

```text
Timeline.Version = 1.5.6
```

本插件只调用 Timeline 的公共 API / 公共类型，不 Patch Timeline，也不反射访问 Timeline 私有成员，所以如果其他 Timeline 版本仍保留这些公共接口，也可能兼容。

遇到加载、轨道枚举或刷新异常时，建议先使用 **Timeline 1.5.6** 复现排查。

## Timeline 自身的依赖

Timeline 当前维护版自身还依赖 KKAPI、Sideloader 等组件。它们属于 Timeline 的**传递依赖**，请按 HSPlugins / 对应整合包的官方安装方式满足；KK_TimelineStateCleaner 本身不直接管理这些版本。

维护来源：<https://github.com/IllusionMods/HSPlugins>
