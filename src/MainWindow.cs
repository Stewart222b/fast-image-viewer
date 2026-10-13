using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FastImageViewer
{
    public sealed class MainWindow : Window
    {
        private readonly ImageSurface surface = new ImageSurface();
        private readonly Grid root = new Grid();
        private readonly Grid stage = new Grid();
        private readonly TextBlock filename = Text("", 12, "#404040");
        private readonly TextBlock folder = Text("", 12, "#666666");
        private readonly TextBlock metadata = Text("", 12, "#666666");
        private readonly TextBlock counter = Text("", 12, "#404040");
        private readonly TextBox zoomInput = new TextBox();
        private readonly Border welcome;
        private readonly Border message;
        private readonly TextBlock messageText = Text("", 13, "#303030");
        private readonly Border panel;
        private readonly StackPanel panelBody = new StackPanel();
        private readonly Button previous;
        private readonly Button next;
        private readonly Button fit;
        private readonly Button actual;
        private readonly SemaphoreSlim decodeGate = new SemaphoreSlim(1, 1);
        private List<string> files = new List<string>();
        private LoadedImage loaded;
        private int index = -1;
        private int requestId;
        private int userTurns;
        private bool syncingZoomText;
        private bool zoomTextDirty;
        private bool closed;
        private bool fullscreen;
        private bool chromeHidden;
        private string panelKind;
        private WindowState savedState;
        private Rect savedBounds;
        private WindowStyle savedStyle;
        private ResizeMode savedResize;

        public MainWindow(string[] args)
        {
            Title = "拾图";
            Width = 1100; Height = 760; MinWidth = 700; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brush("#202020"); Foreground = Brush("#262626");
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            AllowDrop = true;
            Resources.Add(typeof(Button), ButtonStyle());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            var toolbar = new Grid { Background = Brush("#F3F3F3"), Height = 40 };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(IconButton("open", "打开图片  Ctrl+O", OpenFile));
            var fileOptions = IconButton("down", "文件菜单", delegate { });
            fileOptions.MinWidth = 20;
            var fileMenu = new ContextMenu();
            AddMenu(fileMenu, "打开图片…", "Ctrl+O", OpenFile);
            AddMenu(fileMenu, "打开文件夹…", "Ctrl+Shift+O", OpenFolder);
            fileOptions.Click += delegate { OpenMenu(fileMenu, fileOptions); };
            left.Children.Add(fileOptions);
            left.Children.Add(Divider());
            previous = IconButton("left", "上一张  ← / Page Up", delegate { Navigate(-1); });
            next = IconButton("right", "下一张  → / Space / Page Down", delegate { Navigate(1); });
            left.Children.Add(previous); left.Children.Add(next);
            left.Children.Add(Divider());
            fit = IconButton("fit", "适应窗口  F", surface.Fit);
            actual = Button("1:1", "实际像素  0", surface.ActualSize, 38);
            left.Children.Add(fit); left.Children.Add(actual);
            left.Children.Add(Divider());
            left.Children.Add(IconButton("minus", "缩小  −", delegate { surface.Zoom(1 / 1.18); }));
            zoomInput.Width = 74; zoomInput.Height = 26;
            zoomInput.FontSize = 12; zoomInput.FontFamily = FontFamily;
            zoomInput.TextAlignment = TextAlignment.Center;
            zoomInput.VerticalContentAlignment = VerticalAlignment.Center;
            zoomInput.Background = Brushes.White; zoomInput.Foreground = Brush("#262626");
            zoomInput.BorderBrush = Brush("#B8B8B8"); zoomInput.BorderThickness = new Thickness(1);
            zoomInput.Padding = new Thickness(2, 0, 2, 0); zoomInput.Margin = new Thickness(4, 0, 4, 0);
            zoomInput.ToolTip = "输入缩放百分比，按 Enter 应用";
            AutomationProperties.SetName(zoomInput, "缩放百分比");
            zoomInput.GotKeyboardFocus += delegate { zoomInput.SelectAll(); };
            zoomInput.TextChanged += delegate { if (!syncingZoomText) zoomTextDirty = true; };
            zoomInput.LostKeyboardFocus += delegate { TryApplyZoom(); };
            left.Children.Add(zoomInput);
            left.Children.Add(IconButton("plus", "放大  +", delegate { surface.Zoom(1.18); }));
            left.Children.Add(Divider());
            left.Children.Add(IconButton("rotate", "顺时针旋转  R；反向旋转  Shift+R", delegate { Rotate(1); }));
            toolbar.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            right.Children.Add(IconButton("info", "图片信息  I", delegate { TogglePanel("info"); }));
            right.Children.Add(IconButton("fullscreen", "全屏 / 返回窗口  F11", ToggleFullscreen));
            var more = IconButton("more", "更多操作", delegate { });
            var moreMenu = BuildContextMenu();
            more.Click += delegate { OpenMenu(moreMenu, more); };
            right.Children.Add(more);
            Grid.SetColumn(right, 2); toolbar.Children.Add(right);
            root.Children.Add(toolbar);

            stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stage.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(stage, 1); root.Children.Add(stage);
            stage.Children.Add(surface);
            welcome = BuildWelcome(); stage.Children.Add(welcome);
            message = new Border { Background = Brush("#F3F3F3"), Padding = new Thickness(18, 12, 18, 12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, MaxWidth = 500, Margin = new Thickness(16) };
            messageText.TextWrapping = TextWrapping.Wrap; messageText.TextAlignment = TextAlignment.Center;
            message.Child = messageText; stage.Children.Add(message);
            panel = new Border { Background = Brush("#F3F3F3"), BorderBrush = Brush("#D1D1D1"), BorderThickness = new Thickness(1, 0, 0, 0), Width = 300, Padding = new Thickness(16, 12, 16, 12), Visibility = Visibility.Collapsed };
            panel.Child = new ScrollViewer { Content = panelBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(panel, 1);
            stage.Children.Add(panel);

            var bottom = new Grid { Height = 26, Background = Brush("#F3F3F3") };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            filename.VerticalAlignment = VerticalAlignment.Center; filename.Margin = new Thickness(10, 0, 10, 0); filename.TextTrimming = TextTrimming.CharacterEllipsis;
            bottom.Children.Add(filename);
            metadata.VerticalAlignment = VerticalAlignment.Center; metadata.Margin = new Thickness(8, 0, 10, 0); metadata.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(metadata, 1); bottom.Children.Add(metadata);
            counter.VerticalAlignment = VerticalAlignment.Center; counter.Margin = new Thickness(10, 0, 12, 0); Grid.SetColumn(counter, 2); bottom.Children.Add(counter);
            Grid.SetRow(bottom, 2); root.Children.Add(bottom);

            surface.ViewChanged += UpdateViewStatus;
            surface.Navigate += Navigate;
            surface.ToggleActualSize += delegate { if (surface.View.IsFit) surface.ActualSize(); else surface.Fit(); };
            surface.ContextMenu = BuildContextMenu();
            PreviewKeyDown += OnKey;
            DragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += delegate(object sender, DragEventArgs e)
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null) OpenPaths(paths);
                e.Handled = true;
            };
            Closed += delegate { closed = true; requestId++; };
            Loaded += delegate { surface.Focus(); if (args.Length > 0) OpenPaths(args); };
            UpdateNavigation(); UpdateViewStatus();
        }

        private static SolidColorBrush Brush(string value) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(value); b.Freeze(); return b; }
        private static TextBlock Text(string value, double size, string color) { return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color) }; }
        private static Border Divider() { return new Border { Width = 1, Height = 18, Background = Brush("#D1D1D1"), Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }; }

        private Style ButtonStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#262626")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0, 6, 0)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1, 0, 1, 0)));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, SystemColors.HighlightBrush);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#E1E1E1"), "ButtonBorder"));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1), "ButtonBorder"));
            template.Triggers.Add(focus);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
            template.Triggers.Add(disabled);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private Button Button(string label, string hint, Action action, double width)
        {
            var button = new Button { Content = label, ToolTip = hint, MinWidth = width, Height = 32 };
            AutomationProperties.SetName(button, hint);
            button.Click += delegate { action(); };
            ToolTipService.SetInitialShowDelay(button, 300);
            return button;
        }

        private Button IconButton(string icon, string hint, Action action)
        {
            var button = Button("", hint, action, 32);
            button.Content = new ViewerIcon(icon);
            button.Padding = new Thickness(0);
            return button;
        }

        private static void OpenMenu(ContextMenu menu, FrameworkElement target)
        {
            menu.PlacementTarget = target;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private Border BuildWelcome()
        {
            var box = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var body = new StackPanel();
            body.Children.Add(Text("拖入图片，或打开文件", 14, "#BBBBBB"));
            var open = new Button { Content = "打开图片…", Height = 30, MinWidth = 106, Foreground = Brush("#262626"), Background = Brush("#F3F3F3"), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "打开图片  Ctrl+O" };
            open.Click += delegate { OpenFile(); };
            AutomationProperties.SetName(open, "打开图片"); body.Children.Add(open);
            box.Child = body; return box;
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();
            AddMenu(menu, "打开图片", "Ctrl+O", OpenFile);
            AddMenu(menu, "打开文件夹", "Ctrl+Shift+O", OpenFolder);
            menu.Items.Add(new Separator());
            AddMenu(menu, "适应窗口", "F", surface.Fit);
            AddMenu(menu, "实际像素", "0", surface.ActualSize);
            AddMenu(menu, "顺时针旋转", "R", delegate { Rotate(1); });
            AddMenu(menu, "像素模式", "P", delegate { surface.PixelMode = !surface.PixelMode; surface.Changed(); });
            AddMenu(menu, "透明背景棋盘", "B", surface.ToggleCheckerboard);
            menu.Items.Add(new Separator());
            AddMenu(menu, "全屏", "F11", ToggleFullscreen);
            AddMenu(menu, "隐藏 / 显示工具栏", "T", ToggleChrome);
            AddMenu(menu, "操作指南", "F1", delegate { TogglePanel("help"); });
            return menu;
        }

        private static void AddMenu(ContextMenu menu, string label, string key, Action action)
        {
            var item = new MenuItem { Header = label, InputGestureText = key };
            item.Click += delegate { action(); }; menu.Items.Add(item);
        }

        private void OpenFile()
        {
            var dialog = new OpenFileDialog { Title = "选择图片", Filter = ImageFiles.DialogFilter, Multiselect = true, CheckFileExists = true };
            if (loaded != null) dialog.InitialDirectory = Path.GetDirectoryName(loaded.Path);
            if (dialog.ShowDialog(this) == true) OpenPaths(dialog.FileNames);
        }

        private void OpenFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "选择图片所在的文件夹", ShowNewFolderButton = false })
            {
                if (loaded != null) dialog.SelectedPath = Path.GetDirectoryName(loaded.Path);
                var owner = new NativeOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK) OpenPaths(new[] { dialog.SelectedPath });
            }
        }

        private sealed class NativeOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public NativeOwner(IntPtr handle) { Handle = handle; }
        }

        public async void OpenPaths(string[] paths)
        {
            int version = ++requestId;
            files = new List<string>(); index = -1; UpdateNavigation();
            ResetDisplay();
            welcome.Visibility = Visibility.Collapsed;
            ShowMessage("正在读取图片列表…");
            try
            {
                var list = await Task.Run(delegate
                {
                    if (paths.Length == 1 && File.Exists(paths[0]) && ImageFiles.IsSupported(paths[0])) return ImageFiles.InDirectory(paths[0]);
                    return ImageFiles.FromPaths(paths);
                });
                if (closed || version != requestId) return;
                files = list;
                if (files.Count == 0) { ResetDisplay(); ShowMessage("没有找到可查看的图片。\n请打开 JPG、PNG、BMP、GIF、TIFF 或 ICO 文件。"); return; }
                string selected = paths.Length == 1 && File.Exists(paths[0]) ? Path.GetFullPath(paths[0]) : null;
                int selectedIndex = selected == null ? 0 : files.FindIndex(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase));
                ShowIndex(Math.Max(0, selectedIndex));
            }
            catch (Exception ex) { if (!closed && version == requestId) { ResetDisplay(); ShowMessage("无法打开：" + ex.Message + "\n可以重新选择图片或文件夹。"); } }
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
            loaded = null;
            if (panelKind == "info") BuildPanel();
            surface.CancelWheelZoom();
            userTurns = 0;
            filename.Text = Path.GetFileName(path); filename.ToolTip = path;
            folder.Text = Path.GetDirectoryName(path);
            Title = Path.GetFileName(path) + " - 拾图";
            metadata.Text = "正在打开…";
            UpdateNavigation();
            message.Visibility = Visibility.Collapsed;
            ShowLoadingDelayed(version);
            try
            {
                LoadedImage result = null;
                await decodeGate.WaitAsync();
                try
                {
                    if (closed || version != requestId) return;
                    result = await Task.Run(delegate { return ImageLoader.Load(path); });
                }
                finally { decodeGate.Release(); }
                if (closed || version != requestId) return;
                loaded = result;
                ApplyOrientation();
                surface.Opacity = 1;
                message.Visibility = Visibility.Collapsed;
                metadata.Text = result.PixelWidth + " × " + result.PixelHeight + "   " + FormatSize(result.FileSize) + (result.IsPreview ? "   已缩小预览" : "");
                if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase)) metadata.Text += "   首帧";
                if (panelKind == "info") BuildPanel();
            }
            catch (Exception ex)
            {
                if (!closed && version == requestId)
                {
                    surface.ClearImage(); surface.Opacity = 1;
                    metadata.Text = "打开失败";
                    ShowMessage("无法显示「" + Path.GetFileName(path) + "」\n" + ex.Message + "\n可用方向键继续浏览，或重新打开图片。");
                }
            }
        }

        private void ApplyOrientation()
        {
            if (loaded == null) return;
            BitmapSource bitmap = loaded.Bitmap;
            var transforms = new TransformGroup();
            if (loaded.ExifQuarterTurns != 0) transforms.Children.Add(new RotateTransform(loaded.ExifQuarterTurns * 90));
            if (loaded.ExifFlipHorizontal) transforms.Children.Add(new ScaleTransform(-1, 1));
            if (userTurns != 0) transforms.Children.Add(new RotateTransform(userTurns * 90));
            if (transforms.Children.Count > 0)
            {
                transforms.Freeze();
                bitmap = new TransformedBitmap(bitmap, transforms); bitmap.Freeze();
            }
            bool swapped = ((loaded.ExifQuarterTurns + userTurns) % 2) != 0;
            surface.SetImage(bitmap, swapped ? loaded.PixelHeight : loaded.PixelWidth, swapped ? loaded.PixelWidth : loaded.PixelHeight);
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
            loaded = null;
            surface.ClearImage(); surface.Opacity = 1;
            filename.Text = ""; filename.ToolTip = null;
            folder.Text = "";
            metadata.Text = "";
            Title = "拾图";
            if (panelKind == "info") BuildPanel();
        }

        private void UpdateViewStatus()
        {
            UpdateZoomText(false);
            zoomInput.IsEnabled = fit.IsEnabled = actual.IsEnabled = surface.Bitmap != null;
            fit.Background = surface.Bitmap != null && surface.View.IsFit ? SystemColors.ControlLightBrush : Brushes.Transparent;
            actual.Background = surface.Bitmap != null && !surface.View.IsFit && Math.Abs(surface.View.Scale * surface.DpiScale - 1) < 0.001 ? SystemColors.ControlLightBrush : Brushes.Transparent;
        }

        private void UpdateZoomText(bool force)
        {
            if (!force && zoomInput.IsKeyboardFocusWithin && zoomTextDirty) return;
            syncingZoomText = true;
            zoomInput.Text = surface.Bitmap == null ? "" : (surface.View.Scale * surface.DpiScale * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
            zoomTextDirty = false;
            syncingZoomText = false;
        }

        private bool TryApplyZoom()
        {
            if (surface.Bitmap == null) { UpdateZoomText(true); return false; }
            if (!zoomTextDirty) { UpdateZoomText(true); return true; }
            double percent;
            string value = zoomInput.Text.Trim().TrimEnd('%').Trim();
            if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent) || Double.IsNaN(percent) || Double.IsInfinity(percent) || percent <= 0 || percent > 3200)
            {
                UpdateZoomText(true);
                return false;
            }
            surface.Zoom(percent / (surface.View.Scale * surface.DpiScale * 100));
            UpdateZoomText(true);
            return true;
        }

        private async void ShowLoadingDelayed(int version)
        {
            await Task.Delay(150);
            if (closed || requestId != version || loaded != null || metadata.Text != "正在打开…") return;
            ShowMessage("正在打开图片…");
        }

        private void ShowMessage(string text) { messageText.Text = text; message.Visibility = Visibility.Visible; }
        private static string FormatSize(long size) { return size < 1048576 ? (size / 1024.0).ToString("0.#") + " KB" : (size / 1048576.0).ToString("0.#") + " MB"; }

        private void TogglePanel(string kind)
        {
            if (panel.Visibility == Visibility.Visible && panelKind == kind) { panel.Visibility = Visibility.Collapsed; panelKind = null; return; }
            panelKind = kind; BuildPanel(); panel.Visibility = Visibility.Visible;
        }

        private void BuildPanel()
        {
            panelBody.Children.Clear();
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var close = IconButton("close", "关闭面板  Esc", delegate { panel.Visibility = Visibility.Collapsed; panelKind = null; });
            DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
            var title = Text(panelKind == "help" ? "快捷键" : "图片信息", 14, "#262626"); title.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(title); panelBody.Children.Add(heading);
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
                HelpRow("P / B", "像素缩放 / 透明棋盘");
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
                if (extension == ".gif" || extension == ".tif" || extension == ".tiff" || extension == ".ico") InfoRow("显示帧", "当前版本显示第一帧。");
                string copyPath = loaded.Path;
                var copy = Button("复制文件路径", "复制文件路径", delegate
                {
                    try { Clipboard.SetText(copyPath); }
                    catch (System.Runtime.InteropServices.ExternalException) { ShowMessage("剪贴板正被其他程序使用，请稍后重试。"); }
                }, 130);
                copy.HorizontalAlignment = HorizontalAlignment.Left; panelBody.Children.Add(copy);
            }
        }

        private void HelpRow(string key, string description)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(113) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var keyText = Text(key, 12, "#262626"); keyText.TextWrapping = TextWrapping.Wrap; keyText.Margin = new Thickness(0, 0, 8, 0);
            var descriptionText = Text(description, 12, "#666666"); descriptionText.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(keyText); Grid.SetColumn(descriptionText, 1); row.Children.Add(descriptionText); panelBody.Children.Add(row);
        }
        private void InfoRow(string key, string value)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            row.Children.Add(Text(key, 11, "#666666"));
            var text = Text(value, 12, "#262626"); text.TextWrapping = TextWrapping.Wrap; text.Margin = new Thickness(0, 4, 0, 0); row.Children.Add(text); panelBody.Children.Add(row);
        }

        public void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                savedState = WindowState; savedStyle = WindowStyle; savedResize = ResizeMode; savedBounds = RestoreBounds;
                WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                fullscreen = true;
            }
            else
            {
                WindowState = WindowState.Normal; WindowStyle = savedStyle; ResizeMode = savedResize;
                if (!savedBounds.IsEmpty) { Left = savedBounds.Left; Top = savedBounds.Top; Width = savedBounds.Width; Height = savedBounds.Height; }
                WindowState = savedState; fullscreen = false;
            }
            ApplyChrome(); surface.Focus();
        }

        private void ToggleChrome() { chromeHidden = !chromeHidden; ApplyChrome(); }
        private void ApplyChrome()
        {
            root.Children[0].Visibility = root.Children[2].Visibility = fullscreen || chromeHidden ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (e.OriginalSource is TextBox || e.Source is TextBox || zoomInput.IsKeyboardFocusWithin)
            {
                if (key == Key.Enter) { TryApplyZoom(); surface.Focus(); e.Handled = true; }
                else if (key == Key.Escape) { UpdateZoomText(true); surface.Focus(); e.Handled = true; }
                return;
            }
            if (e.OriginalSource is MenuItem || e.Source is MenuItem) return;
            if (ctrl && key == Key.O) { if (shift) OpenFolder(); else OpenFile(); }
            else if (key == Key.Escape)
            {
                if (panel.Visibility == Visibility.Visible) { panel.Visibility = Visibility.Collapsed; panelKind = null; }
                else if (fullscreen) ToggleFullscreen();
                else if (chromeHidden) { chromeHidden = false; ApplyChrome(); }
                else return;
            }
            else if (key == Key.F11) ToggleFullscreen();
            else if (key == Key.F1) TogglePanel("help");
            else if (ctrl && (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down)) surface.Pan(key == Key.Left ? 60 : key == Key.Right ? -60 : 0, key == Key.Up ? 60 : key == Key.Down ? -60 : 0);
            else if (ctrl) return;
            else if (key == Key.Left || key == Key.PageUp) Navigate(-1);
            else if (key == Key.Right || key == Key.PageDown || (key == Key.Space && surface.IsKeyboardFocused)) Navigate(shift ? -1 : 1);
            else if (key == Key.Home) ShowIndex(0);
            else if (key == Key.End) ShowIndex(files.Count - 1);
            else if (key == Key.Add || key == Key.OemPlus) surface.Zoom(1.18);
            else if (key == Key.Subtract || key == Key.OemMinus) surface.Zoom(1 / 1.18);
            else if (key == Key.F) surface.Fit();
            else if (key == Key.D0 || key == Key.NumPad0) surface.ActualSize();
            else if (key == Key.R) Rotate(shift ? -1 : 1);
            else if (key == Key.I) TogglePanel("info");
            else if (key == Key.P) { surface.PixelMode = !surface.PixelMode; surface.Changed(); }
            else if (key == Key.B) surface.ToggleCheckerboard();
            else if (key == Key.T) ToggleChrome();
            else return;
            e.Handled = true;
        }
    }
}
