# SignPath Foundation 申请表草稿

> 对应申请表：https://signpath.org/apply （表单经 HubSpot 动态渲染，字段以 2026-11-20 实测为准）
> 带 `【待填】` 的项需要维护者本人信息；其余为建议文案，提交前可润色。
> 数据快照：2026-11-20（GitHub API 实测），提交前请刷新为最新数字。

## 表单字段

| 字段 | 填写内容 |
| --- | --- |
| Project Name * | `Ink Canvas Ultra` |
| Repository URL * | `https://github.com/muqiu-pika/Ink-Canvas-Ultra` |
| Homepage URL * | `https://github.com/muqiu-pika/Ink-Canvas-Ultra`（表单允许用仓库页作为主页） |
| Download URL | `https://github.com/muqiu-pika/Ink-Canvas-Ultra/releases/latest`（**前置条件**：Releases 页须提及 SignPath Foundation，见文末"前置条件"） |
| Privacy Policy URL | `https://github.com/muqiu-pika/Ink-Canvas-Ultra/blob/master/privacy.txt`（privacy.txt 已于 2026-11-20 重写为实际网络行为说明） |
| Wikipedia URL（可选） | 无（留空） |
| Tagline * | 英文：`A lightweight, high-performance electronic whiteboard for teaching, optimized for Seewo interactive boards.`<br>中文（备查）：轻量高性能的电子白板，为希沃教学大屏优化 |
| Category * | 首选 `Educational`（教学白板定位）；备选 `Graphics`（绘图工具） |
| Description * | 见下方"Description 文案" |
| Reputation * | 见下方"Reputation 材料" |
| Maintainer Type | `Individual maintainer(s)` |
| Build System * | `GitHub Actions`（表单仅提供 GitHub Actions / GitLab CI/CD 两个选项） |
| First Name * / Last Name * | 【待填】（将创建 SignPath 账户） |
| Email * | 【待填】 |
| Company Name | 可留空 |
| Primary Discovery Channel * | 【待填：如实选择，如 GitHub / 搜索 / 博客】 |
| Specify the exact source（可选） | 【待填：如 "GitHub search" 或来源 URL】 |
| 同意 Code of Conduct * | 勾选（signpath.org/terms，注意该页标注为 Draft 版） |
| 通讯与数据同意 | 按个人意愿勾选（后者必填） |

## Description 文案（英文，可直接粘贴）

> Ink Canvas Ultra is a lightweight, high-performance open-source electronic whiteboard (WPF, .NET Framework) designed for classroom teaching on Seewo (CVTE) interactive boards, where it starts 5-10x faster than the pre-installed vendor software. It provides PowerPoint slide annotation, blackboard/whiteboard modes, and an on-screen pen for annotating any application, with touch-first features such as palm erasing, gesture erasing, ink shape recognition, coordinate systems and geometry tools, countdown timer, random name picker, and ink replay. It is actively maintained and distributed as Windows installers and portable builds via GitHub Releases with an auto-update mechanism. Licensed under GPL-3.0.

## Reputation 材料（申请必填，诚实陈述）

**现有可引用的客观事实（2026-11-20 快照）：**

| 指标 | 数值 | 来源 |
| --- | --- | --- |
| GitHub Stars | 21 | github.com/muqiu-pika/Ink-Canvas-Ultra |
| GitHub Forks | 3 | 同上 |
| 全部 Releases 累计下载 | 933 次 | GitHub API `download_count` 合计 |
| 用户社群 | QQ 群 617707731（README 首页入口） | qm.qq.com/q/iMzFTuZgDS |
| 文档 | README（含 FAQ/功能清单）+ Manual.md 使用手册 | 仓库内 |
| 上游渊源 | 基于 WXRIW/Ink-Canvas（社区活跃的 Ink Canvas 项目），持续独立维护并大量增强 | README 徽章 |
| 持续构建 | GitHub Actions 自动构建（.github/workflows/dotnet-desktop.yml） | 仓库内 |

**提交前建议补充（Reputation 是 OSS 审核的关键项）：**
1. QQ 群人数与活跃度截图（群资料页）
2. 下载量随版本增长曲线（GitHub Insights → Traffic / Releases 页截图）
3. 用户反馈/使用场景截图（群聊中的使用反馈，注意脱敏）
4. 如有学校/机构批量部署的实例，匿名整理为列表

**获取实时数据的命令：**
```bash
curl -s https://api.github.com/repos/muqiu-pika/Ink-Canvas-Ultra | grep -E 'stargazers_count|forks_count'
curl -s "https://api.github.com/repos/muqiu-pika/Ink-Canvas-Ultra/releases?per_page=100" \
  | grep -o '"download_count": [0-9]*' | awk -F': ' '{s+=$2} END {print s}'
```

## 前置条件（提交申请前必须完成，对应差距清单 A1/B1）

1. **Releases（下载页）提及 SignPath**：下一个 Release 的说明按 `docs/release-notes-template.md` 的"代码签名"一节撰写（模板已含必填署名原文 `Free code signing provided by SignPath.io, certificate by SignPath Foundation`）。
2. **Code signing policy 页面**（README 内），包含：署名原文、团队角色（Authors/Reviewers/Approvers 及成员）、隐私政策链接（privacy.txt）、受影响第三方服务说明 —— 属于差距清单 B1，尚未完成。
3. 签名流水线（差距清单 D 项）实际落地后，再提交申请——申请时 Download URL 与政策页描述的状态必须是真实状态。

## 申请后流程（备忘）

1. 收到 SignPath 账户 → 开启 MFA → 创建/加入 Organization
2. 创建 Project（Repository URL 填本仓库）→ 配置 Signing Policy（release-signing：Origin verification 开启 + Allowed branches 限定 + 审批 ≥1）
3. 编写 Artifact Configuration（上传安装包样本生成，排除 IACore.dll 等第三方 DLL，元数据约束：product name = "Ink Canvas Ultra"、版本一致）
4. 添加预定义 Trusted Build System "GitHub.com" 并链接项目；安装 SignPath GitHub App
5. 创建 CI 用户与 API Token → GitHub Secrets：`SIGNPATH_API_TOKEN`、organization-id、project-slug、signing-policy-slug
6. 改造 workflow：Inno Setup 打包 → `upload-artifact` → `SignPath/github-action-submit-signing-request` → 下载已签名产物发布 Release
