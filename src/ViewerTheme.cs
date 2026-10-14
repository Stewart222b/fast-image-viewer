using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace FastImageViewer
{
    internal sealed class ViewerTheme
    {
        public readonly ResourceDictionary Resources = new ResourceDictionary();
        public bool IsDark { get; private set; }

        public ViewerTheme() { Apply(false); }

        public void Apply(bool dark)
        {
            IsDark = dark;
            Resources["Theme.Canvas"] = Brush(dark ? "#202020" : "#FAFAFA");
            Resources["Theme.Chrome"] = Brush(dark ? "#292929" : "#F3F3F3");
            Resources["Theme.Text"] = Brush(dark ? "#E8E8E8" : "#262626");
            Resources["Theme.Muted"] = Brush(dark ? "#AAAAAA" : "#666666");
            Resources["Theme.Border"] = Brush(dark ? "#4B4B4B" : "#D1D1D1");
            Resources["Theme.Input"] = Brush(dark ? "#202020" : "#FFFFFF");
            Resources["Theme.Hover"] = Brush(dark ? "#3C3C3C" : "#E1E1E1");
            Resources["Theme.Selected"] = Brush(dark ? "#454545" : "#D8D8D8");
            Resources["Theme.Focus"] = Brush(dark ? "#75B8FF" : "#0078D4");
            Resources["Theme.Checker"] = Checker(dark);
        }

        private static SolidColorBrush Brush(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze(); return brush;
        }

        private static DrawingBrush Checker(bool dark)
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brush(dark ? "#383838" : "#FFFFFF"), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            var alternate = Brush(dark ? "#444444" : "#E6E6E6");
            drawing.Children.Add(new GeometryDrawing(alternate, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            drawing.Children.Add(new GeometryDrawing(alternate, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
            var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            brush.Freeze(); return brush;
        }

        public static ResourceDictionary PopupStyles()
        {
            // Keep popup colors in the same resource scope as the owning window.
            return (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='{x:Type ContextMenu}'>
    <Setter Property='Background' Value='{DynamicResource Theme.Chrome}'/>
    <Setter Property='Foreground' Value='{DynamicResource Theme.Text}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Theme.Border}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ContextMenu}'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='3'>
            <ScrollViewer CanContentScroll='True' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'>
              <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
            </ScrollViewer>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='{x:Type MenuItem}'>
    <Setter Property='Foreground' Value='{DynamicResource Theme.Text}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Padding' Value='7,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type MenuItem}'>
          <Border x:Name='ItemBorder' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='18'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='16'/>
              </Grid.ColumnDefinitions>
              <Path x:Name='Check' Data='M1,5 L4,8 L10,1' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.5' Width='11' Height='10' Visibility='Collapsed' VerticalAlignment='Center'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
              <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Foreground='{DynamicResource Theme.Muted}' Margin='22,0,0,0' VerticalAlignment='Center'/>
              <TextBlock x:Name='Arrow' Grid.Column='3' Text='›' FontSize='16' HorizontalAlignment='Right' VerticalAlignment='Center' Visibility='Collapsed'/>
              <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-1' AllowsTransparency='True' Focusable='False'
                     PlacementTarget='{Binding RelativeSource={RelativeSource TemplatedParent}}'
                     IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}' PopupAnimation='None'>
                <Border Background='{DynamicResource Theme.Chrome}' BorderBrush='{DynamicResource Theme.Border}' BorderThickness='1' Padding='3'>
                  <ScrollViewer CanContentScroll='True' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'>
                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
                  </ScrollViewer>
                </Border>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource Theme.Hover}'/></Trigger>
            <Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='{x:Type Separator}'>
    <Setter Property='Template'>
      <Setter.Value><ControlTemplate TargetType='{x:Type Separator}'><Border Height='1' Margin='22,4,7,4' Background='{DynamicResource Theme.Border}'/></ControlTemplate></Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='{x:Type ScrollBar}'>
    <Setter Property='Background' Value='{DynamicResource Theme.Chrome}'/>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Vertical'>
        <Setter Property='Width' Value='12'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='{x:Type ScrollBar}'>
              <Grid Background='{TemplateBinding Background}'>
                <Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'
                       Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}'
                       Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'>
                      <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
                    </RepeatButton>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Margin='3,1' MinHeight='18'>
                      <Thumb.Template>
                        <ControlTemplate TargetType='{x:Type Thumb}'>
                          <Border x:Name='ThumbBorder' Background='{DynamicResource Theme.Border}' CornerRadius='3'/>
                          <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ThumbBorder' Property='Background' Value='{DynamicResource Theme.Muted}'/></Trigger>
                            <Trigger Property='IsDragging' Value='True'><Setter TargetName='ThumbBorder' Property='Background' Value='{DynamicResource Theme.Muted}'/></Trigger>
                          </ControlTemplate.Triggers>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'>
                      <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
                    </RepeatButton>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
  <Style TargetType='{x:Type ToolTip}'>
    <Setter Property='Foreground' Value='{DynamicResource Theme.Text}'/>
    <Setter Property='Background' Value='{DynamicResource Theme.Chrome}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Theme.Border}'/>
    <Setter Property='Padding' Value='9,6'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ToolTip}'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='{TemplateBinding Padding}'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>");
        }

        public void ApplyCaption(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int enabled = IsDark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    internal static class ThemePreference
    {
        public static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastImageViewer", "theme.txt"); }
        }

        public static bool Load(string path)
        {
            try { return File.Exists(path) && File.ReadAllText(path).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }

        public static bool Save(string path, bool dark)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, dark ? "dark" : "light");
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }
    }
}
