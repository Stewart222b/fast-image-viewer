# Fast Image Viewer

Fast Image Viewer 是一个轻量图片查看器，当前版本为 0.3.0。优先处理连续滚轮缩放、拖动、快捷键和界面操作。

Windows 版使用 C#、WPF 和系统图片解码器，无需浏览器运行环境。Linux 和 macOS 版共用 `core/` 里的视口、滚轮和文件列表，界面是 Avalonia，图片解码使用 Skia。Windows 的构建产物仍然是单个 `FastImageViewer.exe`，不额外带 DLL。

## 运行

需要 Windows 10 / 11 x64 和 .NET Framework 4.8。便携运行时，复制完整 `dist` 目录，其中包括 EXE、`FastImageViewer.exe.config` 和 `third-party\libwebp` 许可文件，再双击 EXE。无需安装程序。WebP 解码器已内置，不需要另装系统编解码器。

在项目目录打开 PowerShell，执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1
```

`run.ps1` 在 EXE 不存在时先构建，再打开查看器；已有 EXE 时直接启动。源码修改后需要重新执行 `build.ps1`。命令中的 `ExecutionPolicy Bypass` 仅用于当前 PowerShell 进程，不修改系统执行策略。

通过工具栏的文件夹图标打开图片，旁边的下拉菜单可打开文件夹；也可以把图片或文件夹拖入窗口。打开单张图片后，可浏览同目录的其他图片；目录只读取顶层文件，按文件名自然排序，例如 `2.png` 排在 `10.png` 前。到达第一张或最后一张后停止，不循环切换。

## 操作

| 输入 | 动作 |
| --- | --- |
| 滚轮 | 以鼠标位置为锚点连续缩放，支持细小滚轮增量 |
| 缩放百分比输入框 | 输入任意比例，例如 `125.5%`；`Enter` 应用，`Esc` 取消；失去焦点也会应用有效输入 |
| 按住鼠标左键拖动 | 平移图片 |
| 双击图片 | 在适应窗口与实际像素之间切换 |
| `←` / `→`、`PageUp` / `PageDown` | 上一张 / 下一张 |
| `Space` / `Shift+Space` | 画布聚焦时，下一张 / 上一张；按钮聚焦时保留按钮激活行为 |
| `Shift+滚轮` | 上一张 / 下一张，细小增量累计到一格后切图 |
| `Home` / `End` | 第一张 / 最后一张 |
| `F` / `0` | 适应窗口 / 实际像素 |
| `+` / `-` | 放大 / 缩小 |
| `Ctrl+方向键` | 平移图片 |
| `R` / `Shift+R` | 顺时针 / 逆时针旋转 90° |
| `F11` | 切换全屏 |
| `Esc` | 关闭信息或帮助面板、退出全屏，或恢复隐藏的工具栏 |
| `T` | 隐藏 / 显示工具栏 |
| `Tab` / `Shift+Tab` | 正常向前 / 向后遍历控件焦点 |
| `I` / `F1` | 图片信息 / 快捷键帮助 |
| `Ctrl+O` / `Ctrl+Shift+O` | 打开图片 / 打开文件夹 |
| `Ctrl+C` / `Ctrl+V` | 复制当前方向的图片像素 / 粘贴图片、图片文件或文件路径 |
| `Ctrl+Enter` | 在 Windows 资源管理器中选中原文件；macOS 在 Finder 中选中；Linux 打开所在目录 |
| `K` | 开启 / 关闭切图时保持缩放与位置，菜单勾选当前状态 |
| `P` / `B` | 切换平滑 / 像素模式、透明背景棋盘；底栏显示当前模式 |
| `D` | 切换浅色 / 深色主题 |

适应窗口保持图片完整可见，且不会把小图放大。自定义缩放后调整窗口大小，会保留画面中心对应的图片位置。旋转只改变当前视图，不会保存或修改原图。

默认切图会重新适应窗口。开启「保持缩放与位置」后，手动缩放和拖动位置会延续到下一张；图片尺寸不同时，保持画面中心在图片内的相对位置。处于适应窗口模式时仍自动适应新图片。打开新文件来源或粘贴图片会重新适应窗口，此选项只在当前窗口内生效。

浏览文件夹时会预加载前后一张，已解码图片按最近使用顺序缓存。缓存默认预算为 128 MiB，当前查看的图片不会因淘汰而被释放；过时的切图请求会取消排队，并优先处理最新请求。文件大小或修改时间变化后会重新解码。预算只约束保留的解码缓存，不是进程总内存上限。

复制图片保留当前旋转与 EXIF 方向、透明度，不包含窗口缩放、棋盘格或界面控件。大图预览状态下复制的是预览像素。粘贴优先接受图片文件，再接受图片像素；内存图片不写入临时图片文件，信息面板标明剪贴板来源，且不提供文件定位操作。输入框编辑时保留系统复制／粘贴行为。

默认使用「平滑模式」，缩放时平滑图像边缘，适合照片。「像素模式」保留放大时的像素边界，适合观察像素块。状态栏会明确显示当前模式，单击状态栏模式文字或按 `P` 可以切换；更多菜单和右键菜单的「显示模式」会勾选当前选项。显示模式只改变缩放渲染方式，不会修改原图，也不会改变 1:1 的缩放比例。

滚轮缩放保持原始增量，不按预设百分比跳档。高分辨率滚轮的每个小增量都会生效；若驱动仍只发送每格 120 的事件，画面也会用 80 ms 的短过渡连接两次比例。连续同向输入会累计目标，反向输入会取消旧方向的未完成距离。拖动、切图和点击适应窗口会立即结束缩放过渡。系统关闭界面动画时，缩放直接按原始增量更新。

Windows 界面提供浅色和深色两套主题：40 px 单排图标工具栏、26 px 状态栏，缩放只有一处比例显示。点击工具栏的半圆主题图标、按 `D`，或通过更多菜单 / 右键菜单的「主题」选择浅色、深色。工具栏、画布、输入框、状态栏、侧栏、图标、菜单和提示会同步切换，当前主题在菜单中勾选。切换主题保持图片、缩放比例、拖动位置和显示模式。

首次打开默认浅色，之后会恢复上次选择。主题偏好保存在当前用户的 `%LOCALAPPDATA%\FastImageViewer\theme.txt`；不能写入时，本次切换仍然生效，界面会提示未能保存。系统文件选择对话框仍使用 Windows 的外观。

信息和快捷键按需停靠在右侧，不覆盖图片；操作按钮的提示包含快捷键。快速切图不立即弹出加载遮罩，只有加载超过 150 ms 才提示。

## 格式与限制

- 支持 JPG / JPEG、PNG、WebP、BMP、GIF、TIF / TIFF 和 ICO。WDP / JXR 是否可用取决于系统解码器。
- GIF、动画 WebP、TIFF 等多帧格式只显示第一帧，暂不播放动画或翻页。
- 暂不支持 AVIF、HEIC、RAW 和 SVG。
- 超过 32 MP 的图片使用缩小预览；信息面板保留原始像素尺寸。预览不能用于判断原图的全部像素细节。
- 32 MP 是解码预览的像素预算，并非进程内存上限。解码器、旋转和渲染仍可能需要额外内存；当前没有强制的总内存上限。
- Windows WebP 使用内置 libwebp 1.6.0，支持透明度和 EXIF 方向；压缩输入上限为 256 MiB，暂不进行 WebP ICC 色彩转换。首次查看 WebP 时按 SHA256 校验并提取对应版本 DLL 到 `%LOCALAPPDATA%\FastImageViewer\codecs`，之后复用该版本。第三方许可与可复现构建步骤见 [libwebp NOTICE](third-party/libwebp/NOTICE.md)。

## 构建与测试

关闭正在运行的查看器后构建，避免 EXE 被占用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建使用 Windows 自带的 x64 .NET Framework C# 编译器，输出到 `dist`，不需要额外 SDK 或网络下载。仓库内的已编译 WebP 解码器会作为资源嵌入 EXE；只有重建原生解码器才需要 CMake 和 C 编译器。

运行核心测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\test.ps1
```

当前核心测试结果为 **13 组通过、0 组失败**，覆盖缩放锚点、缩放边界、适应窗口、拖动、窗口调整、DPI、跨图片保持缩放与相对位置、中文与超长数字自然排序、五种格式的真实解码、文件锁释放、大图预览、EXIF 方向、损坏和缺失文件。测试只读写 `artifacts` 内自行生成的图片，并生成 `artifacts\samples\mountain-lake.png` 作为演示图。

运行 WPF 离屏集成测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration.ps1
```

当前集成测试 **25 组通过、0 组失败**，覆盖快速连续打开、切图边界、加载失败恢复、空目录状态、滚轮、精确百分比输入、文本编辑快捷键保护、只读旋转、信息面板、显示模式、两套主题、偏好保存、滚动条、邻图缓存命中、保持缩放、透明 PNG 复制、剪贴板来源切换、文件定位参数和 WebP 文件夹浏览。测试还检查 700 × 440 和 1100 × 720 的工具栏裁切与侧栏重叠。测试不显示桌面窗口，不读写用户的主题偏好或真实剪贴板，也不会打开文件管理器。`artifacts\viewer-light-preview.png`、`artifacts\viewer-dark-preview.png` 和 `artifacts\viewer-empty-preview.png` 是实际 WPF 控件的离屏渲染预览。

运行滚轮专项测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\wheel.ps1
```

滚轮专项测试 **16 组通过、0 组失败**，覆盖 1 / 15 / 30 / 60 / 120 的增量、等量输入一致性、连续插值、反向响应、锚点、缩放边界、动画取消和减少动画设置。

`powershell -NoProfile -ExecutionPolicy Bypass -File tests\webp.ps1` 运行 **7 组 WebP 测试**，覆盖真实有损／无损、透明度、动画首帧画布、大图预览、EXIF、损坏文件和版本化解码器校验。`tests\cache.ps1` 运行 **16 组缓存测试**，覆盖前台优先、邻图任务替换、独立取消、合并请求、文件变更失效、字节预算与活动图片生命周期。解码器测试提取位置隔离到 `artifacts`。

这些测试验证核心逻辑，不代表真实桌面上的缩放、拖动、焦点和多显示器操作已经全部验证。桌面交互仍需人工试用确认。

## Linux 与 macOS

Windows 脚本不变：`build.ps1` 会把 `core\*.cs` 和 `src\*.cs` 编进同一个 EXE。Linux 和 macOS 不编译 WPF 工程，而是发布 `cross/FastImageViewer.Desktop.csproj`。

需要 .NET 8 SDK。Linux 运行时还需要 X11、fontconfig、freetype，中文界面建议安装 Noto Sans CJK。

在项目目录执行：

```bash
bash ./tests/cross-test.sh
bash ./build-unix.sh linux-x64
bash ./build-unix.sh osx-x64 osx-arm64
bash ./run-unix.sh
```

`build-unix.sh` 生成自包含目录 `artifacts/publish/<rid>/`，并打包为 `artifacts/FastImageViewer-0.3.0-<rid>.tar.gz`。支持的 rid 是 `linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。macOS 包未签名。

`tests/cross-test.sh` 在 .NET 8 上跑视口、滚轮、排序、解码预算、EXIF 映射，以及 PNG、JPEG、BMP、GIF、TIFF 的解码。WPF 窗口和 `ImageSurface` 仍由 Windows 上的 `tests/integration.ps1`、`tests/wheel.ps1` 覆盖。

跨平台回归测试还验证透明 TIFF 的颜色合成、棋盘格只绘制可见区域，以及 DPI 改变时的适应窗口和物理缩放比例。Avalonia 界面测试使用 Headless 平台，不会打开桌面窗口。`bash tests/unix-runner-test.sh` 验证 Linux/macOS 的 x64、ARM64 启动选择和带空格的路径参数；`run-unix.sh` 会按当前系统和架构选择发布目录。

当前跨平台测试为 **44 组通过、0 组失败**，包括共享缓存回归、WebP 文件夹浏览、保持缩放与位置、剪贴板方向与透明度、过时剪贴板响应保护和文件定位参数。

和 Windows 版的差异：

- 界面控件是 Avalonia，布局和快捷键对齐现有查看器，不是同一套 WPF 控件。
- 滚轮一格在 Avalonia 里大约是 `1.0`，程序乘回 `120` 后再走同一套缩放。
- JPG、PNG、WebP、BMP、GIF、ICO 由 Skia 解码。当前 Skia 构建不含 TIFF 解码器，TIFF 由 LibTiff.NET 读取第一帧；方向标签仍交给和 Windows 相同的映射，不提前烘焙进像素。超过约 1.6 亿像素的 TIFF 会拒绝解码。WDP / JXR 仍会出现在文件列表里，但只有 Windows 系统解码器能打开。
- GIF、TIFF、ICO 仍然只显示第一帧。
- 系统关闭动画时，Windows 版会立刻跳到目标比例；Linux / macOS 版始终使用 80 ms 过渡。

## 设计说明

- 图片优先和紧凑界面参考 [qView 官方产品示例](https://interversehq.com/qview/)，保留本项目的图标工具栏，便于发现常用操作。
- 鼠标锚定缩放参考 [ImageGlass 功能文档](https://imageglass.org/docs/features)；细状态栏和连续比例参考 [Microsoft Photos 官方改进说明](https://blogs.windows.com/windows-insider/2024/06/20/viewer-and-import-enhancements-for-microsoft-photos-on-windows-11/)。仅借鉴交互，没有复制第三方代码或图片素材。
- 无级滚轮处理依据 [Microsoft WM_MOUSEWHEEL 文档](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)：120 是标准一格的参考量，高分辨率滚轮可以发送更细的增量。实际硬件是否提供细小增量仍取决于鼠标驱动。
- 图片以 `BitmapCacheOption.OnLoad` 解码，在加载完成后关闭文件流，避免长时间锁住原图。参见 [Microsoft BitmapImage.CacheOption 文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapimage.cacheoption)。
- 视口区分图片像素、界面逻辑坐标与显示器 DPI，并在应用清单中声明 DPI 感知。参见 [Microsoft DPI 与设备无关像素文档](https://learn.microsoft.com/en-us/windows/win32/learnwin32/dpi-and-device-independent-pixels)。
- 浅色 / 深色主题共用 WPF 动态资源，颜色变化时更新现有控件，不重建图片视图。参见 [Microsoft WPF 资源说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/systems/xaml-resources-overview)。
