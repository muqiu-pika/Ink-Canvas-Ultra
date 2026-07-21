# Ink Canvas Ultra — plugin.icplugin 文件规范 (v1.0)

本规范定义 Ink Canvas Ultra plugin 的清单文件 `plugin.icplugin` 的格式、目录结构、加载规则以及 plugin 与主程序之间的接口契约。

> 术语统一：本项目中「插件」一律使用英文 **plugin**（复数 **plugins**），清单文件扩展名为 `.icplugin`，安装包扩展名为 `.icplugin`。

---

## 1. 总体架构

```
Ink-Canvas-Ultra/
├── Ink Canvas/                          # 主程序源码
│   ├── Plugins/                         # plugin API 接口定义 + PluginHost
│   │   ├── IPlugin.cs
│   │   ├── IPluginHost.cs
│   │   ├── PluginManifest.cs
│   │   ├── PluginEntryPoint.cs
│   │   └── PluginHost.cs
│   └── ...
└── Ink-Canvas-Ultra-Plugin/             # plugin 开发资料 + 官方示例
    ├── README.md                        # 本文件
    ├── Specification/                   # 规范文档目录
    │   └── icplugin-spec.md             # 完整规范
    ├── Templates/                       # plugin 项目模板
    │   └── plugin-template/             # 通用模板
    └── visualpresenter/                 # 官方视频展台 plugin 示例
        ├── plugin.icplugin              # 清单
        ├── build.ps1                    # 构建脚本
        └── src/
            └── VisualPresenterPlugin.cs # plugin 源码
```

主程序运行时从 `<可执行文件目录>\Plugins\` 加载 plugin。每个 plugin 占用一个子目录：

```
Ink Canvas Ultra.exe
└── Plugins/
    └── visualpresenter/                 # 一个 plugin = 一个子目录
        ├── plugin.icplugin              # 必需，清单文件
        ├── VisualPresenter.dll          # 可选，入口程序集
        ├── icon.png                     # 可选，图标
        └── ...                          # 可选，plugin 私有资源
```

---

## 2. plugin.icplugin 文件格式

### 2.1 编码与语法

- **编码**：UTF-8 with BOM
- **语法**：JSON（[RFC 8259](https://www.rfc-editor.org/rfc/rfc8259)）
- **大小**：建议 ≤ 16 KB
- **文件名**：固定为 `plugin.icplugin`，区分大小写

### 2.2 字段定义

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `id` | string | ✅ | plugin 唯一标识符 |
| `name` | string | ✅ | 展示名称 |
| `version` | string | ✅ | 语义化版本号 |
| `author` | string | ❌ | 作者或组织 |
| `description` | string | ❌ | 简短描述（建议 ≤ 128 字符） |
| `entryAssembly` | string | ❌ | 入口程序集文件名（相对 plugin 目录） |
| `entryClass` | string | ❌ | 实现 `IPlugin` 的类全限定名 |
| `minHostVersion` | string | ❌ | 兼容主程序最低版本 |
| `homepage` | string | ❌ | 主页 URL |
| `icon` | string | ❌ | 图标文件相对路径（建议 64×64 PNG） |
| `entryPoints` | array | ❌ | 声明式 UI 入口点列表 |

### 2.3 字段细则

#### `id`
- 反向域名格式：`ink-canvas.visualpresenter`
- 字符集：`[a-z0-9.-]`，必须以字母开头
- 长度：2–64
- 全局唯一，重复时仅加载首个

#### `version`
- 语义化版本号：`MAJOR.MINOR.PATCH`（如 `1.0.0`）
- 升级判断按 SemVer 2.0 比较

#### `entryAssembly`
- 相对 plugin 目录的路径，如 `VisualPresenter.dll`
- 仅当 plugin 需要执行代码时填写；纯声明式 plugin 可省略
- 主程序使用 `Assembly.LoadFrom` 加载，**plugin 程序集必须以 .NET Framework 4.7.2 为目标**

#### `entryClass`
- 实现 `Ink_Canvas.Plugins.IPlugin` 接口的类的全限定名
- 必须有无参公共构造函数
- 仅在 `entryAssembly` 非空时有效

#### `entryPoints`
- 数组，每项为一个声明式 UI 入口点（详见 §4）
- 允许 plugin 在不写任何代码的情况下向主程序注册按钮

### 2.4 完整示例

```json
{
    "id": "ink-canvas.visualpresenter",
    "name": "视频展台",
    "version": "1.0.0",
    "author": "Ink Canvas Ultra",
    "description": "为 Ink Canvas Ultra 提供视频展台（摄像头捕获、拍照、实时矫正）能力。",
    "entryAssembly": "VisualPresenter.dll",
    "entryClass": "Ink_Canvas.Plugins.VisualPresenter.VisualPresenterPlugin",
    "minHostVersion": "7.0.0",
    "icon": "icon.png",
    "entryPoints": [
        {
            "route": "video-presenter",
            "placement": "board-toolbar",
            "label": "视频展台",
            "glyph": "",
            "order": 50,
            "tooltip": "打开视频展台侧栏"
        }
    ]
}
```

---

## 3. plugin 生命周期

### 3.1 加载流程

主程序启动时按以下顺序加载 plugin：

1. **扫描目录**：遍历 `<RootPath>\Plugins\*` 一级子目录
2. **读取清单**：每个子目录必须存在 `plugin.icplugin` 文件，否则跳过并记录 Warning 日志
3. **解析校验**：JSON 反序列化为 `PluginManifest`，`id` 必填且唯一
4. **注册入口点**：将 `entryPoints` 中每一项加入路由表（`route` 为键）
5. **加载程序集**：若 `entryAssembly` + `entryClass` 均存在，则
   - `Assembly.LoadFrom(assemblyPath)`
   - 通过反射创建 `entryClass` 实例（必须实现 `IPlugin`）
   - 调用 `IPlugin.Initialize(host)`
6. **加载完成**：记录 Event 日志 `plugin 已加载: <id> v<version>`

### 3.2 卸载流程

主程序退出时：

1. 触发 `IPluginHost.ApplicationExiting` 事件
2. 对每个已加载 plugin 调用 `IPlugin.Shutdown()`
3. 清空路由表与已加载列表

### 3.3 错误处理

- 任何 plugin 加载失败**不影响**其他 plugin 与主程序启动
- 所有异常写入 `Log.txt`，级别为 `Error`
- plugin `Initialize` 抛出异常时，该 plugin 不会被注册到已加载列表

---

## 4. 声明式 UI 入口点

plugin 可在 manifest 中声明 `entryPoints` 数组，主程序据此自动渲染 UI 按钮。无需编写任何 C# 代码。

### 4.1 字段定义

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `route` | string | ✅ | 路由键（主程序预定义，见 §4.3） |
| `placement` | string | ✅ | 放置位置（见 §4.2） |
| `label` | string | ✅ | 按钮文本 |
| `glyph` | string | ❌ | Segoe Fluent Icons 字符（如 `&#xe714;`） |
| `icon` | string | ❌ | 图标文件相对路径（与 `glyph` 二选一，`glyph` 优先） |
| `order` | int | ❌ | 排序权重，默认 100，越小越靠前 |
| `tooltip` | string | ❌ | 鼠标悬停提示 |

### 4.2 placement 可选值

| 值 | 说明 |
|---|---|
| `board-toolbar` | 白板工具栏（批注模式与白板模式下均显示） |
| `float-bar` | 浮动工具栏 |
| `sidebar` | 侧栏头部 |

### 4.3 route 可选值（主程序内建路由）

| route | 说明 | 主程序响应 |
|---|---|---|
| `video-presenter` | 视频展台 | 显示 `VideoPresenterSidebar` 侧栏 |
| `settings:appearance` | 外观设置 | 跳转到设置 → 外观 |
| `settings:automation` | 自动化设置 | 跳转到设置 → 自动 |
| `settings:plugin-workshop` | 插件工坊 | 在设置中跳转到插件工坊入口 |

主程序后续版本可扩展更多路由。plugin 调用 `host.TriggerRoute(route)` 即可触发。

### 4.4 声明式 plugin 与编程式 plugin

- **纯声明式 plugin**：仅提供 `plugin.icplugin`，不提供程序集。主程序根据 `entryPoints` 渲染按钮，点击时调用预定义路由。适合只暴露主程序已有功能的 plugin。
- **编程式 plugin**：提供 `entryAssembly` + `entryClass`，在 `Initialize` 中可执行任意逻辑：注册自定义按钮、订阅事件、启动线程等。仍可同时声明 `entryPoints` 以获得主程序内建按钮。

---

## 5. plugin API 接口

### 5.1 `IPlugin`（plugin 实现）

```csharp
public interface IPlugin
{
    PluginManifest Manifest { get; }
    void Initialize(IPluginHost host);
    void Shutdown();
}
```

### 5.2 `IPluginHost`（主程序实现，注入给 plugin）

```csharp
public interface IPluginHost
{
    string PluginDirectory { get; }       // plugin 自身目录
    string HostRootPath { get; }          // 主程序根目录
    Window MainWindow { get; }            // 主窗口

    void Log(string message, PluginLogLevel logLevel = PluginLogLevel.Info);
    void ShowNotification(string message);

    bool TriggerRoute(string route, object parameter = null);

    event EventHandler ApplicationExiting;
    event EventHandler<BoardModeChangedEventArgs> BoardModeChanged;
}

// 日志级别枚举（与主程序 LogHelper.LogType 一一对应，但对 plugin 公开）
public enum PluginLogLevel
{
    Info,
    Trace,
    Warning,
    Error,
    Event
}
```

### 5.3 实现示例

```csharp
using System;
using Ink_Canvas.Plugins;

namespace Ink_Canvas.Plugins.Example
{
    public class ExamplePlugin : IPlugin
    {
        public PluginManifest Manifest { get; private set; }
        private IPluginHost _host;

        public void Initialize(IPluginHost host)
        {
            _host = host;
            Manifest = new PluginManifest
            {
                Id = "ink-canvas.example",
                Name = "示例 plugin",
                Version = "1.0.0"
            };

            _host.Log($"ExamplePlugin 已初始化，目录: {_host.PluginDirectory}", PluginLogLevel.Event);

            // 订阅主程序事件
            _host.ApplicationExiting += OnExiting;
            _host.BoardModeChanged += OnBoardModeChanged;
        }

        public void Shutdown()
        {
            _host.Log("ExamplePlugin 已关闭", PluginLogLevel.Event);
        }

        private void OnExiting(object sender, EventArgs e)
        {
            // 释放资源
        }

        private void OnBoardModeChanged(object sender, BoardModeChangedEventArgs e)
        {
            _host.Log($"白板模式变化: {e.NewMode}", PluginLogLevel.Trace);
        }
    }
}
```

---

## 6. plugin 程序集要求

- **目标框架**：.NET Framework 4.7.2
- **平台**：AnyCPU 或 x64（与主程序一致）
- **依赖**：
  - 必须引用主程序 `Ink Canvas Ultra.exe`（仅访问 `Ink_Canvas.Plugins` 命名空间下的公共类型）
  - 可引用主程序已加载的 NuGet 包（Newtonsoft.Json、iNKORE.UI.WPF.Modern 等）
  - **不应**捆绑主程序已加载的 DLL 副本，避免版本冲突
- **签名**：可选，主程序不强制验证签名

---

## 7. 安装包格式

plugin 分发文件使用 `.icplugin` 扩展名（与清单文件 `plugin.icplugin` 同名仅是约定）。安装包本质上是一个 ZIP 压缩文件，扩展名改为 `.icplugin`。

> 注意区分：
> - **清单文件** `plugin.icplugin`：位于 plugin 目录内，JSON 格式，文件名固定
> - **安装包** `<name>.icplugin`：ZIP 压缩文件，用于分发，文件名即插件目录名

### 7.1 单文件安装包

最简形式：将清单与所有资源打包为 ZIP 压缩文件，扩展名改为 `.icplugin`。

主程序「从本地安装」对话框接受 `.icplugin` 文件，安装流程如下：

1. 用户在插件工坊点击「从本地安装」
2. 选择 `.icplugin` 文件
3. 主程序以文件名（去掉扩展名）作为目标子目录名，计算安装目录 `<RootPath>\Plugins\<fileNameWithoutExt>\`
4. 若目标目录已存在，弹窗询问是否覆盖（确认后先删除旧目录再解压）
5. 使用 `System.IO.Compression.ZipFile.ExtractToDirectory` 解压到目标目录（.NET Framework 4.7.2 的重载不支持 overwriteFiles，靠步骤 4 清空目录保证解压成功）
6. 校验解压后是否包含 `plugin.icplugin` 清单文件；若缺失则删除目标目录并报错
7. 刷新插件工坊已安装列表（运行时不自动加载新装的 plugin，需重启主程序）

### 7.2 目录式安装

开发者也可直接将整个 plugin 目录放入 `<RootPath>\Plugins\<id>\`，主程序下次启动时自动发现并加载。无需打包。

### 7.3 安装包目录结构示例

```
visualpresenter.icplugin（实为 ZIP）
├── plugin.icplugin              # 清单（必需，文件名固定）
├── VisualPresenterPlugin.dll    # 入口程序集（entryAssembly 指定）
├── icon.png                     # 图标（可选）
└── assets/                      # 私有资源（可选）
    └── ...
```

### 7.4 打包脚本示例

`visualpresenter\pack.ps1` 演示了如何从源码构建并打包 `.icplugin` 文件：

1. 调用 `dotnet build` 编译 `VisualPresenterPlugin.csproj`，生成 `VisualPresenterPlugin.dll`
2. 在临时目录中收集 `plugin.icplugin`、`*.dll`、`*.pdb`、`icon.png`（如有）
3. 使用 `[System.IO.Compression.ZipFile]::CreateFromDirectory` 将临时目录打包为 `visualpresenter.icplugin`
4. 输出到上级目录 `Ink-Canvas-Ultra-Plugin\visualpresenter.icplugin`

> 注意：plugin 程序集目标框架必须为 .NET Framework 4.7.2，且不应捆绑主程序已加载的 DLL 副本（详见 §6）。

---

## 8. 安全注意事项

- plugin 在主程序进程内运行，拥有与主程序相同的权限
- 安装第三方 plugin 前**必须**确认来源可信
- 主程序当前**不**对 plugin 进行沙箱隔离
- plugin 异常不会崩溃主程序（已通过 try-catch 隔离 `Initialize` / `Shutdown`），但 plugin 启动的后台线程异常**可能**影响主程序稳定性
- 建议 plugin 在 `Shutdown` 中停止所有后台线程并释放非托管资源

---

## 9. 调试

### 9.1 日志

所有 plugin 行为通过 `IPluginHost.Log` 写入 `<RootPath>\Log.txt`，可按级别过滤：

- `Info` 一般信息
- `Trace` 详细跟踪
- `Warning` 警告
- `Error` 错误
- `Event` 关键事件

### 9.2 调试流程

1. 在 Visual Studio 中打开主程序解决方案
2. 新建类库项目，目标框架 .NET Framework 4.7.2
3. 引用主程序 `Ink Canvas Ultra.exe`
4. 实现 `IPlugin`
5. 编译输出到 `<主程序输出目录>\Plugins\<plugin-id>\`
6. 在主程序 `Plugins\<plugin-id>\` 放置 `plugin.icplugin`
7. 启动主程序（F5），断点会命中 plugin 代码

### 9.3 重新加载

当前版本 plugin **不支持运行时热重载**。修改 plugin 后需重启主程序。后续版本可考虑通过插件工坊的「刷新列表」触发卸载 / 重新加载。

---

## 10. 版本演进

### v1.0（当前）

- manifest 格式固定为本文档定义
- 支持声明式入口点 + 编程式入口
- 支持路由表机制
- 不支持 plugin 间直接通信（plugin 之间相互独立）

### 未来计划

- plugin 间消息总线
- plugin 依赖声明（`dependencies` 字段）
- plugin 权限模型
- 热重载与运行时启停
- 在线 plugin 商店
