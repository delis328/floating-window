# Floating TXT Reader

Windows 桌面悬浮 TXT 阅读器：无边框、半透明、始终置顶的小窗口，适合一边看小说一边做别的事。

基于 .NET 8 WPF 开发，托盘常驻，窗口本身只显示当前几行文字，不抢焦点、不占任务栏。

## 功能

- **悬浮阅读**：无边框透明窗口，始终置顶，不显示在任务栏。
- **托盘菜单**：按当前 TXT、章节操作、窗口控制、设置与退出分组；阅读设置和快捷键设置统一收在「设置」子菜单中。
- **章节跳转**：自动识别 `第N卷 / 第N章 / 第N话`（支持阿拉伯数字、全角数字、中文数字），托盘内提供「上一章 / 下一章 / 下五章」快捷按钮。
- **章节列表**：`章节列表...` 打开独立窗口，支持按标题筛选，回车或双击跳转到任意章节。
- **阅读设置**：每次显示行数、每行最大字符、字体、字号、字重、字形、文字颜色、文字不透明度、行高倍数。
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
│  ├─ HotkeySettingsWindow.xaml(.cs) # 独立快捷键设置窗口
│  ├─ Hotkeys.cs               # 快捷键解析、校验与快进间隔
│  ├─ ChapterJumpControl.cs    # 托盘内的章节跳转控件
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

检查包括快捷键解析、重复 / 保留键校验、旧配置兼容、全局注册与占用失败恢复、隐藏 / 恢复、上下行 / 章节跳转、可调匀速快进 / 松开停止和文末边界。测试会短暂创建窗口和注册测试快捷键，不读取或保存个人配置。
