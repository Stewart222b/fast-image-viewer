using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FastImageViewer.Imaging;

namespace FastImageViewer.Desktop
{
    public sealed class ViewerWindow : Window
    {
        private readonly ImageCanvas canvas = new ImageCanvas();
        private readonly TextBlock filename = Text("", 12, "#404040");
        private readonly TextBlock metadata = Text("", 12, "#666666");
        private readonly TextBlock counter = Text("", 12, "#404040");
        private readonly TextBox zoomInput = new TextBox();
        private readonly Button displayMode = new Button();
        private readonly List<MenuItem> smoothModeItems = new List<MenuItem>();
        private readonly List<MenuItem> pixelModeItems = new List<MenuItem>();
        private readonly TextBlock messageText = Text("", 13, "#303030");
        private readonly StackPanel panelBody = new StackPanel();
        private readonly SemaphoreSlim decodeGate = new SemaphoreSlim(1, 1);
        private readonly string[] startupArgs;
        private readonly SolidColorBrush activeBrush = Brush("#E1E1E1");
        private Border welcome;
        private Border message;
        private Border panel;
        private Control toolbar;
        private Control statusBar;
        private Button previous;
        private Button next;
        private Button fit;
        private Button actual;
        private ContextMenu commandMenu;
        private List<string> files = new List<string>();
        private Picture loaded;
        private int index = -1;
        private int requestId;
        private int userTurns;
        private bool? displayedPixelMode;
        private bool syncingZoomText;
        private bool zoomTextDirty;
        private bool closed;
        private bool fullscreen;
        private bool chromeHidden;
        private string panelKind;
        private WindowState savedState;
        private PixelPoint savedPosition;
        private double savedWidth;
        private double savedHeight;

        public ViewerWindow(string[] args)
        {
            startupArgs = args ?? new string[0];
            Title = "拾图";
            Width = 1100;
            Height = 760;
            MinWidth = 700;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brush("#202020");
            FontFamily = new FontFamily("PingFang SC, Segoe UI, Microsoft YaHei UI, Noto Sans CJK SC, WenQuanYi Micro Hei, DejaVu Sans");
            FontSize = 12;
            UseLayoutRounding = true;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Content = root;

            toolbar = BuildToolbar();
            root.Children.Add(toolbar);

            var stage = new Grid();
            stage.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            stage.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetRow(stage, 1);
            root.Children.Add(stage);
            stage.Children.Add(canvas);
            welcome = BuildWelcome();
            stage.Children.Add(welcome);
            message = new Border
            {
                Background = Brush("#F3F3F3"),
                Padding = new Thickness(18, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
                MaxWidth = 500,
                Margin = new Thickness(16)
            };
            messageText.TextWrapping = TextWrapping.Wrap;
            messageText.TextAlignment = TextAlignment.Center;
            message.Child = messageText;
            stage.Children.Add(message);
            panel = new Border
            {
                Background = Brush("#F3F3F3"),
                BorderBrush = Brush("#D1D1D1"),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Width = 300,
                Padding = new Thickness(16, 12),
                IsVisible = false
            };
            panel.Child = new ScrollViewer { Content = panelBody, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            Grid.SetColumn(panel, 1);
            stage.Children.Add(panel);

            statusBar = BuildStatus();
            Grid.SetRow(statusBar, 2);
            root.Children.Add(statusBar);

            commandMenu = BuildCommandMenu();
            canvas.ContextMenu = commandMenu;
            canvas.ViewChanged += UpdateViewStatus;
            canvas.Navigate += Navigate;
            canvas.ToggleActualSize += delegate { if (canvas.View.IsFit) canvas.ActualSize(); else canvas.Fit(); };
            AddHandler(KeyDownEvent, OnPreviewKey, RoutingStrategies.Tunnel);
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
            Opened += delegate
            {
                canvas.Focus();
                if (startupArgs.Length > 0) OpenPaths(startupArgs);
            };
            Closed += delegate
            {
                closed = true;
                requestId++;
                if (loaded != null) loaded.Dispose();
                canvas.ClearImage();
            };
            UpdateNavigation();
            UpdateViewStatus();
        }

        private Control BuildToolbar()
        {
            var bar = new Grid { Height = 40, Background = Brush("#F3F3F3") };
            bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(ToolButton("open", "打开图片  Ctrl+O", OpenFile));
            var fileOptions = ToolButton("down", "文件菜单", delegate { });
            fileOptions.Width = 20;
            var fileMenu = new ContextMenu();
            AddMenu(fileMenu, "打开图片…", OpenFile);
            AddMenu(fileMenu, "打开文件夹…", OpenFolder);
            fileOptions.Click += delegate { fileMenu.Open(fileOptions); };
            left.Children.Add(fileOptions);
            left.Children.Add(Divider());
            previous = ToolButton("left", "上一张  ← / Page Up", delegate { Navigate(-1); });
            next = ToolButton("right", "下一张  → / Space / Page Down", delegate { Navigate(1); });
            left.Children.Add(previous);
            left.Children.Add(next);
            left.Children.Add(Divider());
            fit = ToolButton("fit", "适应窗口  F", canvas.Fit);
            actual = TextButton("1:1", "实际像素  0", canvas.ActualSize, 38);
            left.Children.Add(fit);
            left.Children.Add(actual);
            left.Children.Add(Divider());
            left.Children.Add(ToolButton("minus", "缩小  −", delegate { canvas.Zoom(1 / 1.18); }));
            zoomInput.Width = 74;
            zoomInput.Height = 26;
            zoomInput.FontSize = 12;
            zoomInput.TextAlignment = TextAlignment.Center;
            zoomInput.VerticalContentAlignment = VerticalAlignment.Center;
            zoomInput.Margin = new Thickness(4, 0);
            zoomInput.Padding = new Thickness(2, 0);
            ToolTip.SetTip(zoomInput, "输入缩放百分比，按 Enter 应用");
            zoomInput.GotFocus += delegate { zoomInput.SelectAll(); };
            zoomInput.TextChanged += delegate { if (!syncingZoomText) zoomTextDirty = true; };
            zoomInput.LostFocus += delegate { TryApplyZoom(); };
            left.Children.Add(zoomInput);
            left.Children.Add(ToolButton("plus", "放大  +", delegate { canvas.Zoom(1.18); }));
            left.Children.Add(Divider());
            left.Children.Add(ToolButton("rotate", "顺时针旋转  R；反向旋转  Shift+R", delegate { Rotate(1); }));
            bar.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            right.Children.Add(ToolButton("info", "图片信息  I", delegate { TogglePanel("info"); }));
            right.Children.Add(ToolButton("fullscreen", "全屏 / 返回窗口  F11", ToggleFullscreen));
            Button more = null;
            more = ToolButton("more", "更多操作", delegate { commandMenu.Open(more); });
            right.Children.Add(more);
            Grid.SetColumn(right, 2);
            bar.Children.Add(right);
            return bar;
        }

        private Control BuildStatus()
        {
            var bottom = new Grid { Height = 26, Background = Brush("#F3F3F3") };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            filename.VerticalAlignment = VerticalAlignment.Center;
            filename.Margin = new Thickness(10, 0);
            filename.TextTrimming = TextTrimming.CharacterEllipsis;
            bottom.Children.Add(filename);
            metadata.VerticalAlignment = VerticalAlignment.Center;
            metadata.Margin = new Thickness(8, 0, 10, 0);
            metadata.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(metadata, 1);
            bottom.Children.Add(metadata);
            displayMode.Height = 22;
            displayMode.MinWidth = 74;
            displayMode.VerticalAlignment = VerticalAlignment.Center;
            displayMode.Margin = new Thickness(6, 0);
            displayMode.Classes.Add("tool");
            displayMode.Click += delegate { SetDisplayMode(!canvas.PixelMode); };
            Grid.SetColumn(displayMode, 2);
            bottom.Children.Add(displayMode);
            counter.VerticalAlignment = VerticalAlignment.Center;
            counter.Margin = new Thickness(10, 0, 12, 0);
            Grid.SetColumn(counter, 3);
            bottom.Children.Add(counter);
            return bottom;
        }

        private Border BuildWelcome()
        {
            var box = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var body = new StackPanel();
            body.Children.Add(Text("拖入图片，或打开文件", 14, "#BBBBBB"));
            var open = new Button
            {
                Content = "打开图片…",
                Height = 30,
                MinWidth = 106,
                Foreground = Brush("#262626"),
                Background = Brush("#F3F3F3"),
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(open, "打开图片  Ctrl+O");
            open.Click += delegate { OpenFile(); };
            body.Children.Add(open);
            box.Child = body;
            return box;
        }

        private ContextMenu BuildCommandMenu()
        {
            var menu = new ContextMenu();
            AddMenu(menu, "打开图片", OpenFile);
            AddMenu(menu, "打开文件夹", OpenFolder);
            menu.Items.Add(new Separator());
            AddMenu(menu, "适应窗口    F", canvas.Fit);
            AddMenu(menu, "实际像素    0", canvas.ActualSize);
            AddMenu(menu, "顺时针旋转    R", delegate { Rotate(1); });
            var modes = new MenuItem { Header = "显示模式    P" };
            var smooth = new MenuItem { Header = "平滑模式（适合照片）", ToggleType = MenuItemToggleType.CheckBox, IsChecked = !canvas.PixelMode };
            var pixel = new MenuItem { Header = "像素模式（保留像素边界）", ToggleType = MenuItemToggleType.CheckBox, IsChecked = canvas.PixelMode };
            smooth.Click += delegate { SetDisplayMode(false); };
            pixel.Click += delegate { SetDisplayMode(true); };
            smoothModeItems.Add(smooth);
            pixelModeItems.Add(pixel);
            modes.Items.Add(smooth);
            modes.Items.Add(pixel);
            menu.Items.Add(modes);
            AddMenu(menu, "透明背景棋盘    B", canvas.ToggleCheckerboard);
            menu.Items.Add(new Separator());
            AddMenu(menu, "全屏    F11", ToggleFullscreen);
            AddMenu(menu, "隐藏 / 显示工具栏    T", ToggleChrome);
            AddMenu(menu, "操作指南    F1", delegate { TogglePanel("help"); });
            return menu;
        }

        private static void AddMenu(ContextMenu menu, string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += delegate { action(); };
            menu.Items.Add(item);
        }

        private Button ToolButton(string icon, string tip, Action action)
        {
            var button = new Button
            {
                Content = new ToolIcon(icon),
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            button.Classes.Add("tool");
            ToolTip.SetTip(button, tip);
            button.Click += delegate { action(); };
            return button;
        }

        private Button TextButton(string label, string tip, Action action, double width)
        {
            var button = new Button
            {
                Content = label,
                MinWidth = width,
                Height = 32,
                Margin = new Thickness(1, 0),
                Padding = new Thickness(6, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            button.Classes.Add("tool");
            ToolTip.SetTip(button, tip);
            button.Click += delegate { action(); };
            return button;
        }

        private static Border Divider()
        {
            return new Border
            {
                Width = 1,
                Height = 18,
                Background = Brush("#D1D1D1"),
                Margin = new Thickness(6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static SolidColorBrush Brush(string value) { return new SolidColorBrush(Color.Parse(value)); }

        private static TextBlock Text(string value, double size, string color)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color) };
        }

        private async void OpenFile()
        {
            try
            {
                if (StorageProvider == null) return;
                var options = new FilePickerOpenOptions
                {
                    Title = "选择图片",
                    AllowMultiple = true,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new FilePickerFileType("图片文件")
                        {
                            Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.tif", "*.tiff", "*.ico", "*.wdp", "*.jxr" }
                        },
                        new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } }
                    }
                };
                if (loaded != null)
                    options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(loaded.Path));
                var picked = await StorageProvider.OpenFilePickerAsync(options);
                var paths = LocalPaths(picked);
                if (paths.Count > 0) OpenPaths(paths.ToArray());
            }
            catch (Exception ex) { ShowMessage("无法打开：" + ex.Message + "\n可以重新选择图片或文件夹。"); }
        }

        private async void OpenFolder()
        {
            try
            {
                if (StorageProvider == null) return;
                var options = new FolderPickerOpenOptions { Title = "选择图片所在的文件夹", AllowMultiple = false };
                if (loaded != null)
                    options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(loaded.Path));
                var picked = await StorageProvider.OpenFolderPickerAsync(options);
                var paths = LocalPaths(picked);
                if (paths.Count > 0) OpenPaths(paths.ToArray());
            }
            catch (Exception ex) { ShowMessage("无法打开：" + ex.Message + "\n可以重新选择图片或文件夹。"); }
        }

        private static List<string> LocalPaths(IEnumerable<IStorageItem> items)
        {
            var paths = new List<string>();
            if (items == null) return paths;
            foreach (var item in items)
            {
                string path = item.TryGetLocalPath();
                if (!String.IsNullOrWhiteSpace(path)) paths.Add(path);
            }
            return paths;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer != null && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            var paths = LocalPaths(e.DataTransfer == null ? null : e.DataTransfer.TryGetFiles());
            if (paths.Count > 0) OpenPaths(paths.ToArray());
            e.Handled = true;
        }

        public async void OpenPaths(string[] paths)
        {
            int version = ++requestId;
            files = new List<string>();
            index = -1;
            UpdateNavigation();
            ResetDisplay();
            welcome.IsVisible = false;
            ShowMessage("正在读取图片列表…");
            try
            {
                var list = await Task.Run(delegate
                {
                    if (paths.Length == 1 && File.Exists(paths[0]) && FastImageViewer.ImageFiles.IsSupported(paths[0]))
                        return FastImageViewer.ImageFiles.InDirectory(paths[0]);
                    return FastImageViewer.ImageFiles.FromPaths(paths);
                });
                if (closed || version != requestId) return;
                files = list;
                if (files.Count == 0)
                {
                    ResetDisplay();
                    ShowMessage("没有找到可查看的图片。\n请打开 JPG、PNG、BMP、GIF、TIFF 或 ICO 文件。");
                    return;
                }
                string selected = paths.Length == 1 && File.Exists(paths[0]) ? Path.GetFullPath(paths[0]) : null;
                int selectedIndex = selected == null ? 0 : files.FindIndex(p => ImageFiles.PathComparer.Equals(p, selected));
                ShowIndex(Math.Max(0, selectedIndex));
            }
            catch (Exception ex)
            {
                if (!closed && version == requestId)
                {
                    ResetDisplay();
                    ShowMessage("无法打开：" + ex.Message + "\n可以重新选择图片或文件夹。");
                }
            }
        }

        public void Navigate(int direction)
        {
            if (files.Count == 0) return;
            int target = Math.Max(0, Math.Min(files.Count - 1, index + direction));
            if (target != index) ShowIndex(target);
        }

        private async void ShowIndex(int target)
        {
            if (target < 0 || target >= files.Count) return;
            index = target;
            int version = ++requestId;
            string path = files[index];
            if (loaded != null) { loaded.Dispose(); loaded = null; }
            if (panelKind == "info") BuildPanel();
            canvas.CancelWheelZoom();
            userTurns = 0;
            filename.Text = Path.GetFileName(path);
            ToolTip.SetTip(filename, path);
            Title = Path.GetFileName(path) + " - 拾图";
            metadata.Text = "正在打开…";
            UpdateNavigation();
            message.IsVisible = false;
            ShowLoadingDelayed(version);
            Picture result = null;
            try
            {
                await decodeGate.WaitAsync();
                try
                {
                    if (closed || version != requestId) return;
                    result = await Task.Run(delegate { return PictureLoader.Load(path); });
                }
                finally { decodeGate.Release(); }
                if (closed || version != requestId)
                {
                    if (result != null) result.Dispose();
                    return;
                }
                loaded = result;
                result = null;
                ApplyOrientation();
                message.IsVisible = false;
                metadata.Text = loaded.PixelWidth + " × " + loaded.PixelHeight + "   " + FormatSize(loaded.FileSize) + (loaded.IsPreview ? "   已缩小预览" : "");
                if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase)) metadata.Text += "   首帧";
                if (panelKind == "info") BuildPanel();
            }
            catch (Exception ex)
            {
                if (result != null) result.Dispose();
                if (loaded != null) { loaded.Dispose(); loaded = null; }
                if (!closed && version == requestId)
                {
                    canvas.ClearImage();
                    metadata.Text = "打开失败";
                    ShowMessage("无法显示「" + Path.GetFileName(path) + "」\n" + ex.Message + "\n可用方向键继续浏览，或重新打开图片。");
                }
            }
        }

        private void ApplyOrientation()
        {
            if (loaded == null) return;
            var oriented = ImageOrientation.Apply(loaded.Pixels, loaded.ExifQuarterTurns, loaded.ExifFlipHorizontal, userTurns);
            try
            {
                var bitmap = AvaloniaImages.From(oriented);
                bool swapped = ((loaded.ExifQuarterTurns + userTurns) % 2) != 0;
                canvas.SetImage(bitmap, swapped ? loaded.PixelHeight : loaded.PixelWidth, swapped ? loaded.PixelWidth : loaded.PixelHeight);
            }
            finally { oriented.Dispose(); }
        }

        private void Rotate(int turns)
        {
            if (loaded == null) return;
            userTurns = (userTurns + turns + 4) % 4;
            ApplyOrientation();
        }

        private void UpdateNavigation()
        {
            previous.IsEnabled = index > 0;
            next.IsEnabled = index >= 0 && index < files.Count - 1;
            counter.Text = files.Count == 0 ? "" : (index + 1) + " / " + files.Count;
        }

        private void ResetDisplay()
        {
            if (loaded != null) { loaded.Dispose(); loaded = null; }
            canvas.ClearImage();
            filename.Text = "";
            ToolTip.SetTip(filename, null);
            metadata.Text = "";
            Title = "拾图";
            if (panelKind == "info") BuildPanel();
        }

        private void UpdateViewStatus()
        {
            UpdateDisplayModeStatus(false);
            UpdateZoomText(false);
            bool hasImage = canvas.HasImage;
            zoomInput.IsEnabled = fit.IsEnabled = actual.IsEnabled = hasImage;
            fit.Background = hasImage && canvas.View.IsFit ? activeBrush : Brushes.Transparent;
            actual.Background = hasImage && !canvas.View.IsFit && Math.Abs(canvas.View.Scale * canvas.DpiScale - 1) < 0.001 ? activeBrush : Brushes.Transparent;
        }

        private void SetDisplayMode(bool pixel)
        {
            canvas.PixelMode = pixel;
            UpdateDisplayModeStatus(true);
        }

        private void UpdateDisplayModeStatus(bool force)
        {
            if (!force && displayedPixelMode == canvas.PixelMode) return;
            displayedPixelMode = canvas.PixelMode;
            displayMode.Content = canvas.PixelMode ? "像素模式" : "平滑模式";
            ToolTip.SetTip(displayMode, canvas.PixelMode
                ? "当前：像素模式。放大时保留像素边界。单击或按 P 切换为平滑模式。"
                : "当前：平滑模式。缩放时平滑图像边缘，适合照片。单击或按 P 切换为像素模式。");
            foreach (var item in smoothModeItems) item.IsChecked = !canvas.PixelMode;
            foreach (var item in pixelModeItems) item.IsChecked = canvas.PixelMode;
        }

        private void UpdateZoomText(bool force)
        {
            if (!force && zoomInput.IsFocused && zoomTextDirty) return;
            syncingZoomText = true;
            zoomInput.Text = canvas.HasImage
                ? (canvas.View.Scale * canvas.DpiScale * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%"
                : "";
            zoomTextDirty = false;
            syncingZoomText = false;
        }

        private bool TryApplyZoom()
        {
            if (!canvas.HasImage) { UpdateZoomText(true); return false; }
            if (!zoomTextDirty) { UpdateZoomText(true); return true; }
            double percent;
            string value = zoomInput.Text.Trim().TrimEnd('%').Trim();
            if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent) || Double.IsNaN(percent) || Double.IsInfinity(percent) || percent <= 0 || percent > 3200)
            {
                UpdateZoomText(true);
                return false;
            }
            canvas.Zoom(percent / (canvas.View.Scale * canvas.DpiScale * 100));
            UpdateZoomText(true);
            return true;
        }

        private async void ShowLoadingDelayed(int version)
        {
            await Task.Delay(150);
            if (closed || requestId != version || loaded != null || metadata.Text != "正在打开…") return;
            ShowMessage("正在打开图片…");
        }

        private void ShowMessage(string text)
        {
            messageText.Text = text;
            message.IsVisible = true;
        }

        private static string FormatSize(long size)
        {
            return size < 1048576 ? (size / 1024.0).ToString("0.#") + " KB" : (size / 1048576.0).ToString("0.#") + " MB";
        }

        private void TogglePanel(string kind)
        {
            if (panel.IsVisible && panelKind == kind) { panel.IsVisible = false; panelKind = null; return; }
            panelKind = kind;
            BuildPanel();
            panel.IsVisible = true;
        }

        private void BuildPanel()
        {
            panelBody.Children.Clear();
            var heading = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            heading.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            heading.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var title = Text(panelKind == "help" ? "快捷键" : "图片信息", 14, "#262626");
            title.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(title);
            var close = ToolButton("close", "关闭面板  Esc", delegate { panel.IsVisible = false; panelKind = null; });
            Grid.SetColumn(close, 1);
            heading.Children.Add(close);
            panelBody.Children.Add(heading);
            if (panelKind == "help")
            {
                HelpRow("滚轮", "围绕鼠标连续缩放");
                HelpRow("左键拖动", "自由平移图片");
                HelpRow("双击", "适应窗口 / 实际像素");
                HelpRow("← → / PgUp PgDn", "上一张 / 下一张");
                HelpRow("Space / Shift+Space", "下一张 / 上一张");
                HelpRow("Shift+滚轮", "上一张 / 下一张");
                HelpRow("缩放输入框", "Enter 应用，Esc 取消");
                HelpRow("+ / −", "以窗口中心缩放");
                HelpRow("F / 0", "适应窗口 / 实际像素");
                HelpRow("Ctrl+方向键", "平移图片");
                HelpRow("Home / End", "第一张 / 最后一张");
                HelpRow("R / Shift+R", "顺时针 / 逆时针旋转");
                HelpRow("F11 / Esc", "进入 / 退出全屏");
                HelpRow("T", "隐藏 / 显示工具栏");
                HelpRow("P", "平滑 / 像素模式，底栏显示当前模式");
                HelpRow("B", "透明棋盘");
                HelpRow("Ctrl+O", "打开图片");
                HelpRow("Ctrl+Shift+O", "打开文件夹");
                HelpRow("I / F1", "图片信息 / 操作指南");
            }
            else if (loaded == null) panelBody.Children.Add(Text("请先打开图片。", 12, "#666666"));
            else
            {
                InfoRow("文件名", Path.GetFileName(loaded.Path));
                InfoRow("尺寸", loaded.PixelWidth + " × " + loaded.PixelHeight + " 像素");
                InfoRow("大小", FormatSize(loaded.FileSize));
                InfoRow("路径", loaded.Path);
                if (loaded.IsPreview) InfoRow("大图预览", "解码尺寸限制为 3200 万像素；放大后细节以预览图为准。");
                string extension = Path.GetExtension(loaded.Path).ToLowerInvariant();
                if (extension == ".gif" || extension == ".tif" || extension == ".tiff" || extension == ".ico")
                    InfoRow("显示帧", "当前版本显示第一帧。");
                string copyPath = loaded.Path;
                var copy = TextButton("复制文件路径", "复制文件路径", delegate { CopyPath(copyPath); }, 130);
                copy.HorizontalAlignment = HorizontalAlignment.Left;
                panelBody.Children.Add(copy);
            }
        }

        private async void CopyPath(string path)
        {
            try
            {
                var top = TopLevel.GetTopLevel(this);
                if (top == null || top.Clipboard == null) return;
                await top.Clipboard.SetTextAsync(path);
            }
            catch (Exception) { ShowMessage("剪贴板正被其他程序使用，请稍后重试。"); }
        }

        private void HelpRow(string key, string description)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(113)));
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var keyText = Text(key, 12, "#262626");
            keyText.TextWrapping = TextWrapping.Wrap;
            keyText.Margin = new Thickness(0, 0, 8, 0);
            var descriptionText = Text(description, 12, "#666666");
            descriptionText.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(keyText);
            Grid.SetColumn(descriptionText, 1);
            row.Children.Add(descriptionText);
            panelBody.Children.Add(row);
        }

        private void InfoRow(string key, string value)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            row.Children.Add(Text(key, 11, "#666666"));
            var text = Text(value, 12, "#262626");
            text.TextWrapping = TextWrapping.Wrap;
            text.Margin = new Thickness(0, 4, 0, 0);
            row.Children.Add(text);
            panelBody.Children.Add(row);
        }

        private void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                savedState = WindowState;
                savedPosition = Position;
                savedWidth = Width;
                savedHeight = Height;
                WindowState = WindowState.FullScreen;
                fullscreen = true;
            }
            else
            {
                WindowState = WindowState.Normal;
                Position = savedPosition;
                Width = savedWidth;
                Height = savedHeight;
                if (savedState == WindowState.Maximized) WindowState = savedState;
                fullscreen = false;
            }
            ApplyChrome();
            canvas.Focus();
        }

        private void ToggleChrome()
        {
            chromeHidden = !chromeHidden;
            ApplyChrome();
        }

        private void ApplyChrome()
        {
            bool hide = fullscreen || chromeHidden;
            toolbar.IsVisible = !hide;
            statusBar.IsVisible = !hide;
        }

        private void OnPreviewKey(object sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
            var top = TopLevel.GetTopLevel(this);
            var focused = top == null || top.FocusManager == null ? null : top.FocusManager.GetFocusedElement();
            Key key = e.Key;
            if (focused is TextBox)
            {
                if (key == Key.Enter) { TryApplyZoom(); canvas.Focus(); e.Handled = true; }
                else if (key == Key.Escape) { UpdateZoomText(true); canvas.Focus(); e.Handled = true; }
                return;
            }
            if (focused is MenuItem) return;
            if (ctrl && key == Key.O) { if (shift) OpenFolder(); else OpenFile(); }
            else if (key == Key.Escape)
            {
                if (panel.IsVisible) { panel.IsVisible = false; panelKind = null; }
                else if (fullscreen) ToggleFullscreen();
                else if (chromeHidden) { chromeHidden = false; ApplyChrome(); }
                else return;
            }
            else if (key == Key.F11) ToggleFullscreen();
            else if (key == Key.F1) TogglePanel("help");
            else if (ctrl && (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down))
                canvas.Pan(key == Key.Left ? 60 : key == Key.Right ? -60 : 0, key == Key.Up ? 60 : key == Key.Down ? -60 : 0);
            else if (ctrl) return;
            else if (key == Key.Left || key == Key.PageUp) Navigate(-1);
            else if (key == Key.Right || key == Key.PageDown || (key == Key.Space && canvas.IsFocused)) Navigate(shift ? -1 : 1);
            else if (key == Key.Home) ShowIndex(0);
            else if (key == Key.End) ShowIndex(files.Count - 1);
            else if (key == Key.Add || key == Key.OemPlus) canvas.Zoom(1.18);
            else if (key == Key.Subtract || key == Key.OemMinus) canvas.Zoom(1 / 1.18);
            else if (key == Key.F) canvas.Fit();
            else if (key == Key.D0 || key == Key.NumPad0) canvas.ActualSize();
            else if (key == Key.R) Rotate(shift ? -1 : 1);
            else if (key == Key.I) TogglePanel("info");
            else if (key == Key.P) SetDisplayMode(!canvas.PixelMode);
            else if (key == Key.B) canvas.ToggleCheckerboard();
            else if (key == Key.T) ToggleChrome();
            else return;
            e.Handled = true;
        }
    }
}
