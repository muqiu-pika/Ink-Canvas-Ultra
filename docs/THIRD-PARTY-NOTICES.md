# 第三方组件与资产清单（THIRD-PARTY NOTICES）

本清单列出 Ink Canvas Ultra 仓库中随源码分发或构建引用的第三方组件、资产及其来源与许可依据。
本项目主许可证为 GPL-3.0（见根目录 [LICENSE](../LICENSE)）。清单按"可复审计"标准维护，供用户与
SignPath Foundation 审核（Code signing policy 见 README）。

## 1. Microsoft Ink Analysis 组件（系统组件回退副本）

| 文件 | 来源 | 用途 | 许可依据 |
| --- | --- | --- | --- |
| `Ink Canvas/IACore.dll` | Windows 系统组件（`C:\Program Files\Common Files\microsoft shared\INK`） | 墨迹分析（图形识别） | 随 Windows 分发的系统组件 |
| `Ink Canvas/IALoader.dll` | 同上 | 墨迹分析加载器 | 同上 |
| `Ink Canvas/IAWinFX.dll` | .NET Framework / WPF（System.Windows.Ink.Analysis） | WPF 墨迹分析接口 | 随 .NET Framework 分发的系统组件 |

说明：这三个 DLL 是 Windows / .NET Framework 自带的 Ink Analysis 组件的回退副本，仅用于
目标机器系统组件缺失或版本不匹配时保证程序可运行。按 GPLv3 第 1 条 "System Libraries" 的
定义精神处理：程序运行依赖的系统组件，不以 GPL 重新授权、不修改、不声称所有权，版权归属
Microsoft Corporation。

## 2. 嵌入字体

| 文件 | 来源 | 用途 | 许可依据 |
| --- | --- | --- | --- |
| `Ink Canvas/Resources/Segoe Fluent Icons.ttf` | Microsoft Windows 11 系统图标字体 | UI 图标渲染（系统字体缺失时的回退） | 随 Windows 分发的系统字体；版权归属 Microsoft |

## 3. NuGet 包（通过 NuGet 自动还原，不随源码仓库分发）

| 包 | 版本 | 许可证 |
| --- | --- | --- |
| iNKORE.UI.WPF.Modern | 0.9.30 | MIT |
| Microsoft.CSharp | 4.7.0 | MIT |
| Microsoft.Office.Interop.PowerPoint | 15.0.4420.1018 | Microsoft Office 主互操作程序集（可再分发 PIA） |
| Microsoft.VisualBasic | 10.3.0 | MIT |
| MicrosoftOfficeCore | 15.0.0 | Microsoft Office PIA |
| Newtonsoft.Json | 13.0.3 | MIT |
| OSVersionExt | 3.0.0 | MIT |
| System.IO.Compression | 4.3.0 | MIT |
| System.Net.Http | 4.3.4 | MIT |

## 4. COM 互操作（编译期内嵌，EmbedInteropTypes）

- `IWshRuntimeLibrary`（Windows Script Host）、`stdole`、`VBIDE`（Office VBA 扩展）——
  均为 Windows / Office 系统组件的类型库封装，版权归属 Microsoft。

## 5. 开发者鸣谢头像（`Ink Canvas/Resources/DeveloperAvatars/`）

`CN-Ironegg.jpg`、`ChangSakura.png`、`Raspberry-Monster.jpg`、`STBBRD.png`、`WXRIW.png`、
`clover-yan.png`、`jiajiaxd.jpg`、`kengwang.png`、`muqiu.png` —— 取自各 GitHub 用户的公开头像，
仅用于"关于"窗口的开发者鸣谢展示。头像版权归各自用户，展示遵循 GitHub 服务条款；如相关用户
要求移除，请联系仓库维护者。

## 6. 第三方产品名称与图标

- `Desmos.png`、`EasiCamera.png` 等功能入口图标：用于标识跳转目标（Desmos 计算器、希沃展台），
  属描述性引用；Desmos 为 [desmos.com](https://www.desmos.com) 的商标，相关权利归属其所有者。

## 7. 运行期访问的第三方服务（隐私相关，见 [privacy.txt](../privacy.txt)）

- GitHub（raw/releases/api）、jsDelivr CDN、ghproxy.net、维护者自建镜像 `gh.muqiu.eu.org` /
  `plugin.muqiu.eu.org`、Desmos 网页计算器、更新日志站 `doc.muqiu.eu.org`。
- 全部为下载或浏览器跳转，本程序不向其上传任何用户数据。

## 8. 上游渊源

本项目 fork 自 [WXRIW/Ink-Canvas](https://github.com/WXRIW/Ink-Canvas)（GPL-3.0），由
muqiu-pika 独立全量维护（所有源文件与构建脚本在本仓库内维护）。依据 GPL-3.0 第 2 条，
原始版权声明在 AssemblyInfo 版权行与本清单中予以保留。
