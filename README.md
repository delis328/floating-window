# Floating TXT Reader

Windows 桌面悬浮 TXT 阅读器：无边框、半透明、始终置顶的小窗口，适合一边看小说一边做别的事。

基于 .NET 8 WPF 开发，托盘常驻，窗口本身只显示当前几行文字，不抢焦点、不占任务栏。

## 下载

[前往 Releases 下载最新版](https://github.com/delis328/floating-window/releases/latest)

- **轻量版 ZIP**：下载 `FloatingTxtReader-win-x64-framework-dependent.zip`，完整解压后运行；需要安装 **.NET 8 Desktop Runtime x64**，不要只复制其中的 exe。
- **免环境单文件版**：下载 `FloatingTxtReader-win-x64-standalone.exe`，无需另装 .NET 环境，直接运行；首次启动可能需要短暂解压运行组件。
- `Source code (zip)` / `Source code (tar.gz)` 是源码，不是可直接运行的程序；可使用附件 `SHA256SUMS.txt` 校验下载文件。

两个版本均不附带 TXT 和个人配置；首次使用请从托盘选择自己的 TXT，之后会自动保存阅读进度。

> 运行环境说明：当前为 .NET 8 兼容版，.NET 8 已于 **2026-11-10** 结束官方支持。v1.2.0 自包含版使用最终补丁 **8.0.34**；后续长期维护需要迁移到受支持的框架版本。

## 功能

- **悬浮阅读**：无边框透明窗口，始终置顶，不显示在任务栏。
- **托盘菜单**：使用 WPF 透明圆角弹出层，描边外真实透明；按当前 TXT、章节操作、窗口控制、设置与退出分组，保留分隔线；阅读设置和快捷键设置统一收在「设置」子菜单中。
- **章节跳转**：自动识别 `第N卷 / 第N章 / 第N话`（支持阿拉伯数字、全角数字、中文数字），托盘菜单右侧竖排提供「上一章 / 下一章」快捷按钮。
- **章节列表**：`章节列表...` 打开独立窗口，支持按标题筛选，回车或双击跳转到任意章节。
- **阅读设置**：每次显示行数、每行最大字符、字体、字号、字重、字形、文字颜色、文字不透明度、行高倍数。
- **空行预览**：TXT 含整行空白时，展示「处理前 → 处理后」节选，由用户选择保留或去除；只过滤阅读显示，不修改原文件或正文缩进。仅记住当前文件的决定，同文件重启不重复询问，切换文件后重置；阅读进度按原文位置保存。
- **全局快捷键**：托盘「设置 → 快捷键设置...」可分别设置快速关闭、隐藏 / 恢复、上一行、下一行、上一章、下一章，以及按住匀速逐行前进的「快进行」（可设置每秒行数）；支持清空、重复校验和系统占用提示。
- **滚轮翻行**：鼠标停在窗口上滚动滚轮即可上下翻行，行数由「每次显示行数」决定。
- **锁定窗口**：锁定后隐藏拖拽手柄、去掉边框，纯文字贴在任何背景上。
- **编码兼容**：优先按 UTF-8 读取，失败时回退 GB18030。
- **配置持久化**：所有设置与阅读进度保存在 `%APPDATA%\FloatingTxtReader\settings.json`，重启后自动恢复到上次位置和行号。

## 目录结构

```
floating-window/
├─ FloatingTxtReader/          # 工程目录
│  ├─ App.xaml(.cs)            # 应用入口
│  ├─ MainWindow.xaml(.cs)     # 悬浮主窗口：渲染、滚轮、章节、热键、托盘
│  ├─ ChapterListWindow.xaml(.cs)  # 章节列表窗口（筛选 + 跳转）
│  ├─ SettingsWindow.xaml(.cs) # 阅读设置窗口
│  ├─ BlankLinePreviewWindow.xaml(.cs) # 去除空行前后预览与确认
│  ├─ ReadingDocument.cs       # 分行、空行过滤与原文进度映射
│  ├─ HotkeySettingsWindow.xaml(.cs) # 独立快捷键设置窗口
│  ├─ Hotkeys.cs               # 快捷键解析、校验与快进间隔
│  ├─ TrayMenu.xaml(.cs)       # 透明圆角托盘菜单与章节跳转按钮
│  ├─ AppState.cs              # 设置模型与默认值
│  ├─ SettingsStore.cs         # 设置读写（原子写入）
│  └─ Assets/                  # 图标资源
└─ .gitignore
```

## 环境要求

| 用途 | 需要安装 |
| --- | --- |
| 编译 / 发布 | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 运行「框架依赖」版 | .NET 8 Desktop Runtime |
| 运行「自包含单文件」版 | 无需任何运行时 |

## 打包

先进入仓库根目录，所有命令都在根目录执行。

### 1. 编译检查

```powershell
dotnet build .\FloatingTxtReader\FloatingTxtReader.csproj -c Release
```

产物在 `FloatingTxtReader\bin\Release\net8.0-windows\`。

### 2. 框架依赖发布（体积小，需装运行时）

```powershell
dotnet publish .\FloatingTxtReader\FloatingTxtReader.csproj -c Release -r win-x64 --self-contained false -o .\publish
```

输出 5 个文件（约 268 KB）：

```
publish/
├─ FloatingTxtReader.exe            # 双击启动
├─ FloatingTxtReader.dll
├─ FloatingTxtReader.deps.json
├─ FloatingTxtReader.runtimeconfig.json
└─ FloatingTxtReader.pdb            # 调试符号，可删
```

> 目标机器必须安装 .NET 8 Desktop Runtime，否则启动时会提示缺少框架。

### 3. 自包含单文件发布（体积大，免装运行时）

```powershell
dotnet publish .\FloatingTxtReader\FloatingTxtReader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\publish-single-file
```

输出一个约 68 MB 的 `FloatingTxtReader.exe`，拷到任何 Windows x64 机器上直接双击即可，不需要装 .NET。同目录下的 `.pdb` 是调试符号，可以删掉。

> 两个 `-p:` 参数缺一不可：`IncludeNativeLibrariesForSelfExtract` 负责把 WPF 的原生 DLL 一并塞进 exe，`EnableCompressionInSingleFile` 负责压缩。否则会在 exe 旁边散落 `wpfgfx_cor3.dll` 等 6 个原生文件。

`publish/` 与 `publish-*/` 已在 `.gitignore` 中忽略，不会被提交。

## 启动与使用

1. 双击 `FloatingTxtReader.exe`。程序没有主界面，只在屏幕上出现一个半透明的悬浮小窗，同时托盘出现图标。
2. 右键托盘图标 → 第一项 `当前 TXT：未绑定` → 选择要读的 `.txt` 文件。
3. 鼠标移到悬浮窗上滚动滚轮翻行；左键拖动窗口顶部居中的小圆点手柄可移动窗口（锁定后手柄隐藏，窗口固定）。
   首次启动窗口出现在 (180, 180)，托盘菜单 `复位位置` 可把它移到屏幕底部居中。
4. 右键托盘图标 → `设置` → `阅读设置...` 调整行数、字体、颜色等（托盘图标双击同样可以打开阅读设置）。
5. 右键托盘图标 → `章节列表...` 筛选并跳转到任意章节。
6. 右键托盘图标 → `设置` → `快捷键设置...`，点击输入框并按下组合键，点击 `应用` 保存；按 `Backspace` / `Delete` 或点击 `清空` 可取消该项绑定。
7. 「快进行」按下时前进一行，继续按住后按设定速度匀速逐行前进（默认 10 行 / 秒，可在快捷键设置中调整为 1-30 行 / 秒），不会随按住时间加速，松开主键或任一修饰键立即停止；到达文末、隐藏窗口或打开设置 / 章节列表时也会停止。普通上下行快捷键每次只移动一行，不受显示行数影响。
8. 「快速隐藏 / 恢复」使用同一快捷键切换，隐藏期间暂停快捷键翻行 / 跳章，托盘仍可操作。打开快捷键设置窗口时临时暂停所有全局快捷键，避免录入时误触；取消或关闭后恢复原配置。
9. 按 `Ctrl+Alt+Q`（可在快捷键设置里改）或托盘菜单 `退出` 关闭程序。已有关闭快捷键会保留，新增操作默认不绑定。

### 开机自启

按 `Win+R` 输入 `shell:startup`，在打开的启动文件夹里给 `FloatingTxtReader.exe` 建一个快捷方式即可。

### 配置位置

```
%APPDATA%\FloatingTxtReader\settings.json
```

删除该文件即可恢复出厂设置。

## 回归检查

在 Windows 上安装 .NET 8 SDK 后执行，无需额外测试包：

```powershell
dotnet run --project .\FloatingTxtReader.Tests -c Release
```

检查包括快捷键解析、重复 / 保留键校验、旧配置兼容、全局注册与占用失败恢复、隐藏 / 恢复、上下行 / 章节跳转、可调匀速快进 / 松开停止和文末边界，以及透明菜单的多缩放圆角、连续跳章、子菜单关闭和句柄稳定性。测试会短暂创建窗口和注册测试快捷键，不读取或保存个人配置。

跨应用菜单交互可单独验证：`dotnet run --project .\FloatingTxtReader.Tests -c Release -- --tray-interaction`。
测试会创建两个独立进程窗口；从“TXT 外部点击测试”触发托盘回调，检查切换应用、Esc 和外部点击关闭。
该工具使用合成文本，禁用文件选择、设置和退出命令，不保存个人配置；关闭“TXT 菜单交互验证”窗口结束测试。
