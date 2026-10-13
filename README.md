# Fast Image Viewer

Fast Image Viewer 是一个面向 Windows 的轻量图片查看器，当前版本为 0.2.1。优先处理连续滚轮缩放、拖动、快捷键和界面操作。使用 C#、WPF 和系统图片解码器，无需浏览器运行环境。

## 运行

需要 Windows 10 / 11 x64 和 .NET Framework 4.8。便携运行时，将 `dist\FastImageViewer.exe` 与同目录下的 `FastImageViewer.exe.config` 一起复制到目标目录，再双击 EXE。无需安装程序。

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
| `P` / `B` | 切换平滑 / 像素模式、透明背景棋盘；底栏显示当前模式 |

适应窗口保持图片完整可见，且不会把小图放大。自定义缩放后调整窗口大小，会保留画面中心对应的图片位置。旋转只改变当前视图，不会保存或修改原图。

默认使用「平滑模式」，缩放时平滑图像边缘，适合照片。「像素模式」保留放大时的像素边界，适合观察像素块。状态栏会明确显示当前模式，单击状态栏模式文字或按 `P` 可以切换；更多菜单和右键菜单的「显示模式」会勾选当前选项。显示模式只改变缩放渲染方式，不会修改原图，也不会改变 1:1 的缩放比例。

滚轮缩放保持原始增量，不按预设百分比跳档。高分辨率滚轮的每个小增量都会生效；若驱动仍只发送每格 120 的事件，画面也会用 80 ms 的短过渡连接两次比例。连续同向输入会累计目标，反向输入会取消旧方向的未完成距离。拖动、切图和点击适应窗口会立即结束缩放过渡。系统关闭界面动画时，缩放直接按原始增量更新。

界面采用系统浅灰工具区和中性深灰画布：40 px 单排图标工具栏、26 px 状态栏，缩放只有一处比例显示。信息和快捷键按需停靠在右侧，不覆盖图片；操作按钮的提示包含快捷键。快速切图不立即弹出加载遮罩，只有加载超过 150 ms 才提示。

## 格式与限制

- 支持 JPG / JPEG、PNG、BMP、GIF、TIF / TIFF 和 ICO。WDP / JXR 是否可用取决于系统解码器。
- GIF、TIFF 等多帧格式只显示第一帧，暂不播放动画或翻页。
- 暂不支持 WebP、AVIF、RAW 和 SVG。
- 超过 32 MP 的图片使用缩小预览；信息面板保留原始像素尺寸。预览不能用于判断原图的全部像素细节。
- 32 MP 是解码预览的像素预算，并非进程内存上限。解码器、旋转和渲染仍可能需要额外内存；当前没有强制的总内存上限。

## 构建与测试

关闭正在运行的查看器后构建，避免 EXE 被占用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建使用 Windows 自带的 x64 .NET Framework C# 编译器，输出到 `dist`，不需要额外 SDK 或网络下载。

运行核心测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\test.ps1
```

当前核心测试结果为 **12 组通过、0 组失败**，覆盖缩放锚点、缩放边界、适应窗口、拖动、窗口调整、DPI、中文与超长数字自然排序、五种格式的真实解码、文件锁释放、大图预览、EXIF 方向、损坏和缺失文件。测试只读写 `artifacts` 内自行生成的图片，并生成 `artifacts\samples\mountain-lake.png` 作为演示图。

运行 WPF 离屏集成测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration.ps1
```

当前集成测试 **15 组通过、0 组失败**，覆盖快速连续打开、切图边界、加载失败恢复、空目录状态、滚轮事件、缩放动画取消、精确百分比输入、文本编辑快捷键保护、只读旋转、信息面板、显示模式状态与菜单同步，以及 700 × 440 和 1100 × 720 的工具栏裁切与侧栏重叠。测试不显示桌面窗口；`artifacts\viewer-preview.png` 和 `artifacts\viewer-empty-preview.png` 是实际 WPF 控件的离屏渲染预览。

运行滚轮专项测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\wheel.ps1
```

滚轮专项测试 **16 组通过、0 组失败**，覆盖 1 / 15 / 30 / 60 / 120 的增量、等量输入一致性、连续插值、反向响应、锚点、缩放边界、动画取消和减少动画设置。

这些测试验证核心逻辑，不代表真实桌面上的缩放、拖动、焦点和多显示器操作已经全部验证。桌面交互仍需人工试用确认。

## 设计说明

- 图片优先和紧凑界面参考 [qView 官方产品示例](https://interversehq.com/qview/)，保留本项目的图标工具栏，便于发现常用操作。
- 鼠标锚定缩放参考 [ImageGlass 功能文档](https://imageglass.org/docs/features)；细状态栏和连续比例参考 [Microsoft Photos 官方改进说明](https://blogs.windows.com/windows-insider/2024/06/20/viewer-and-import-enhancements-for-microsoft-photos-on-windows-11/)。仅借鉴交互，没有复制第三方代码或图片素材。
- 无级滚轮处理依据 [Microsoft WM_MOUSEWHEEL 文档](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)：120 是标准一格的参考量，高分辨率滚轮可以发送更细的增量。实际硬件是否提供细小增量仍取决于鼠标驱动。
- 图片以 `BitmapCacheOption.OnLoad` 解码，在加载完成后关闭文件流，避免长时间锁住原图。参见 [Microsoft BitmapImage.CacheOption 文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapimage.cacheoption)。
- 视口区分图片像素、界面逻辑坐标与显示器 DPI，并在应用清单中声明 DPI 感知。参见 [Microsoft DPI 与设备无关像素文档](https://learn.microsoft.com/en-us/windows/win32/learnwin32/dpi-and-device-independent-pixels)。
