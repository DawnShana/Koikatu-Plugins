# KKPEHeightLockStandalone 依赖版本

适用于：**KKPEHeightLockStandalone v1.2.1 SIMPLIFIED**

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

## 为什么 KKPE 版本比 Timeline 更需要注意

Height Lock 为了管理 `cf_n_height` 的 KKPE scale correction，除了使用 KKPE 公开 API，还会反射访问实现该功能所必需的部分内部成员。

因此建议：

1. 优先使用 **KKPE 2.21.5**；
2. 更新 KKPE 后如果身高锁失效，先检查 KKPE 内部结构是否发生变化；
3. 本插件找不到必须的 KKPE 内部成员时，会尽量关闭 Height Lock，而不是继续写入未知结构；Body Preserve 仍可独立工作。

原始 v1.2.1 SIMPLIFIED 源码包未携带编译时使用的 `KKPE.dll`，所以 2.21.5 是**当前维护版推荐基线**，不是虚构的“原编译机精确版本”。

## 为什么推荐 KKPE 2.21.5

截至本说明核对日期，`IllusionMods/HSPlugins` 当前维护源码中的 KKPE 版本常量为：

```text
KKPE / PoseEditor Version = 2.21.5
```

## KKPE 自身的依赖

当前维护版 KKPE 自身还依赖 ExtensibleSave、KKAPI 等组件。这些属于 KKPE 的**传递依赖**，建议直接按 HSPlugins / 对应整合包完整安装。

`KKPEHeightLockStandalone` 本身并不直接调用 KKAPI，也不依赖 VR、Timeline API 或 MaterialEditor。

维护来源：<https://github.com/IllusionMods/HSPlugins>
