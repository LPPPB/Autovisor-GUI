# AutovisorPanel

为 [Autovisor](https://github.com/CXRunfree/Autovisor) 做的第三方图形前端。
把原本要手工编辑的 `config.ini` 搬进界面，密码改为 Windows DPAPI 加密保存，
编译产物是 **31 KB 的单文件 EXE，零运行时依赖**。

> 本项目不是上游官方发行版。上游功能的实现与维护归 CXRunfree/Autovisor 所有，
> 本项目只提供配置与运行控制的外壳，通过 `--config` 参数和日志文件与之交互。

## 项目结构

```
AutovisorPanel/
├─ src/
│  ├─ Program.cs        入口，单实例互斥
│  ├─ Core.cs           Config / CredentialStore / LogReader / StudyRunner / GameGuard
│  ├─ MainForm.cs       布局与交互
│  ├─ Theme.cs          设计令牌 + 自绘控件
│  └─ app.manifest      DPI 感知、asInvoker
├─ runtime/             上游发行包（不入库，见 .gitignore）
│  ├─ Autovisor.exe
│  ├─ internal/         Python 运行时与 Playwright
│  ├─ resources/        反检测脚本与图片资源
│  └─ data/             mirrors.json
├─ build.ps1            编译面板
├─ package.ps1          打包为分发 zip
├─ config.template.ini  随包分发的配置模板
├─ 使用说明.txt          随包分发的用户手册
├─ dist/                构建与打包产物（不入库）
└─ .github/workflows/   CI
```

## 功能

- 图形化编辑全部配置项（账号、浏览器、播放参数、课程链接）
- 密码框默认星号隐藏，右侧小眼睛可临时查看明文
- 密码加密保存，`config.ini` 中不出现明文
- 一键开始 / 停止刷课，按进程树结束浏览器子进程
- 实时状态：当前课时、平台进度条、已完成节数、日志静默时长
- 日志预览自动滚动，异常时状态徽标变红并闪烁任务栏
- 全屏游戏时自动暂停提醒，不打断游戏
- 游戏检测：按 GPU 3D 占用识别游戏进程，自动暂停刷课并在游戏结束后恢复
- 窗口可缩放，可最大化；两栏之间可拖拽分隔条，比例记入配置
- 日志增量读取：只解析新增行，不重复扫描整个文件
- 崩溃后可恢复：启动时清理上次遗留的临时配置

## 架构

分三层，依赖单向向下：

| 层 | 职责 | 文件 |
|---|---|---|
| 表现层 | 布局、绘制、事件 | `MainForm.cs`、`Theme.cs` |
| 领域层 | 配置读写、凭据加解密、日志解析、进程控制、全屏检测 | `Core.cs` |
| 边界 | 上游进程 + 日志文件 | 仅两个契约 |

**与上游的两个契约**

1. 命令行 `Autovisor.exe --config <ini路径>` —— 用于注入一次性运行配置
2. `<runtime>/logs/Log*.txt` 的文本格式 —— 用于解析课时与进度

上游升级时只需调整 `LogReader` 里的正则，上游二进制不需要改动。

**上游定位规则**：优先使用 `runtime/Autovisor.exe`；若不存在，
回退到面板同级的 `Autovisor.exe`。因此两种布局都能运行：

```
布局 A（推荐，随包分发）        布局 B（平铺，兼容旧安装）
AutovisorPanel.exe              AutovisorPanel.exe
runtime/Autovisor.exe           Autovisor.exe
runtime/internal/               internal/
config.ini                      config.ini
```

## 构建

需要 Windows 10/11 自带的 .NET Framework 4.x（无需安装 SDK）。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

产物为 `dist\AutovisorPanel.exe`。若 `runtime\Autovisor.exe` 存在，
脚本会同时把 EXE 放到仓库根目录，可直接双击调试。

## 打包分发

```powershell
powershell -ExecutionPolicy Bypass -File .\package.ps1 -Version 1.0.0
```

产出 `dist\AutovisorPanel-1.0.0.zip`（约 38 MB），解压后的结构：

```
AutovisorPanel.exe
config.ini          账号密码为空，首次运行填写
使用说明.txt
runtime/            上游发行包，勿单独移动
```

打包会自动剔除 `logs/`、`packages/`、`data/cookies.json`、`data/cred.bin`，
不会把你的凭据和日志发给别人。

## 使用

1. 解压 zip 到任意可写目录（不要放 `C:\Program Files`）
2. 双击 `AutovisorPanel.exe`
3. 填写账号与课程链接，点「开始刷课」
4. 首次运行会自动下载 numpy / opencv 到 `runtime\packages`

## 密码处理

| 环节 | 做法 |
|---|---|
| 落盘 | DPAPI `ProtectedData.Protect`，`CurrentUser` 作用域 + 自定义 entropy，保存为 `data\cred.bin` |
| 可解密范围 | 仅当前 Windows 账户 + 当前机器；换机或换账户无法解开 |
| 旧配置迁移 | 启动时若发现 `config.ini` 残留明文密码，自动加密迁移并清除明文 |
| 运行期注入 | 生成 `%TEMP%\autovisor-panel-<guid>.ini`，通过 `--config` 传给上游 |
| 清理 | 停止刷课、关闭面板时删除；下次启动清理遗留文件 |

> 说明：上游只接受配置文件，因此运行时明文必然短暂存在于临时目录。
> 若不希望如此，可将账号密码留空，改用浏览器手动登录 + Cookie 免密。

## 游戏检测

刷课期间浏览器可能因安全验证被顶到前台，打断全屏游戏。面板用 GPU 占用识别游戏并主动让路。

**判定方式**

- 读取 Windows 性能计数器 `GPU Engine` 的 `Utilization Percentage`（仅 `engtype_3D`，按进程汇总）
- 过滤系统进程、浏览器、IDE、终端、播放器等固定白名单，以及本工具自身的浏览器
- 单个进程 3D 占用 ≥ 25% 且**持续 3 次采样（约 15 秒）** → 判定为游戏
- 连续 12 次采样（约 60 秒）低于阈值 → 判定游戏结束

**触发动作**

| 阶段 | 行为 |
|---|---|
| 检测到游戏 | 立即停止刷课（结束浏览器进程树），状态徽标变「游戏暂停」 |
| 游戏进行中 | 不弹通知、不闪烁任务栏 |
| 游戏结束 | 若此前是自动暂停，则自动恢复刷课 |

事件写入 `logs\panel.log`，形如：

```
2026-11-09 13:20:11 | 检测到游戏 cs2.exe 68%，暂停刷课
2026-11-09 14:05:33 | 游戏结束，GPU 已空闲
2026-11-09 14:05:34 | 已自动恢复刷课
```

**开关**：配置卡「游戏时自动暂停」复选框，状态保存在 `config.ini` 的 `[panel]` 段（上游程序忽略该段）。

> 无条件全屏（`QUNS_RUNNING_D3D_FULL_SCREEN`）仍作为辅助信号，用于抑制任务栏闪烁。

## 界面设计

- 三主色：底色 `#F6F7F9`、卡片 `#FFFFFF`、主色 `#2563EB`
- 无渐变、无阴影、无发光；卡片 10px 圆角 + 1px 描边
- 字号层级：15pt 标题 / 11pt 卡片标题 / 9pt 正文 / 8.5pt 辅助
- 自绘控件：圆角按钮（悬停仅变色不位移）、无边框输入框（聚焦变蓝边）、
  圆角进度条、胶囊状态徽标
- 密码框默认星号，右侧自绘眼睛图标；点击切换明文，再次点击复原，焦点保持在输入框
- 响应式：左侧配置栏宽度由分隔条决定，右侧状态栏随窗口拉伸；日志框与进度条同步变宽变高
- 分隔把手为自绘控件（`GripBar`）：浅灰圆角竖条 + 三点，悬停变蓝，拖拽调整两栏比例

## 性能

| 项 | 做法 |
|---|---|
| 状态刷新 | 3 秒一次，日志按追加偏移量增量解析，文件无变化时不产生 IO |
| 日志预览 | 维护 40 行环形缓冲，只在有新行时更新文本框 |
| GPU 检测 | 5 秒采样，性能计数器对象按实例缓存，退出进程的计数器自动回收 |
| 拖拽重排 | 24ms 节流；内容面板双缓冲 + 拖拽期间暂停状态刷新，避免残影 |
| 界面重绘 | 提示文字等内容比对后才赋值，避免无效刷新 |
| 内存 | 运行时工作集约 20~30 MB，无后台常驻进程 |

## 已知限制

- 上游窗口若被最小化或手动操作，可能打断目录/播放状态
- 上游只处理视频课时，PPT/PDF 章节需要手动切换
- 课中弹题由上游自动选第一个选项，不保证正确
- 检测依赖日志文本，平台改版后需同步调整解析正则
- 打包体积由上游二进制决定（约 38 MB），面板本身仅 31 KB

## 免责声明

本仓库仅供学习与研究浏览器自动化与桌面程序开发。
使用本工具可能违反在线学习平台的服务条款，相关风险与后果由使用者自行承担。

## 许可证

[MIT](LICENSE)。上游 Autovisor 亦为 MIT 许可证，Copyright (c) 2024 PengChengxu。
