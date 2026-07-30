using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Borderus.Models;
using Borderus.Native;
using Borderus.Rendering;
using Borderus.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Borderus;

public partial class MainWindow : Window
{
    private readonly BorderSettings _settings;
    private readonly WindowBorderService _borderService;
    private readonly LayoutIndicatorService _layoutIndicatorService;
    private readonly KeyboardService _keyboardService;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly DispatcherTimer _saveTimer;
    private readonly BorderRenderer _activePreviewRenderer = new();
    private readonly BorderRenderer _inactivePreviewRenderer = new();
    private Forms.ToolStripMenuItem? _enabledMenuItem;
    private Forms.ContextMenuStrip? _trayMenu;
    private WindowsThemePalette _theme;
    private bool _loading = true;
    private bool _exiting;
    private bool _editingActive = true;
    private System.Windows.Point _previewDragStart;
    private double _previewOffsetX;
    private double _previewOffsetY;

    private BorderProfile CurrentProfile => _editingActive ? _settings.Active : _settings.Inactive;

    public MainWindow()
    {
        InitializeComponent();
        ApplySystemTheme();
        AddPreviewRenderer(ActivePreviewHost, _activePreviewRenderer);
        AddPreviewRenderer(InactivePreviewHost, _inactivePreviewRenderer);
        Version? version = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = version is null ? string.Empty : $"v{version.Major}.{version.Minor}.{version.Build}";
        _settings = SettingsStore.Load();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SettingsStore.Save(_settings);
        };
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        LoadControls();
        _borderService = new WindowBorderService(Dispatcher, _settings);
        _layoutIndicatorService = new LayoutIndicatorService(Dispatcher, _settings);
        _keyboardService = new KeyboardService(_settings);
        _trayIcon = CreateTrayIcon();
        _loading = false;
        ApplySettings();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        _trayMenu = menu;
        ApplyTrayMenuTheme();
        menu.Items.Add("Открыть", null, (_, _) => ShowFromTray());
        _enabledMenuItem = new Forms.ToolStripMenuItem("Рамки включены", null, (_, _) => ToggleEnabled())
        {
            Checked = _settings.Enabled,
            CheckOnClick = false
        };
        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApplication());
        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "Borderus — рамки окон",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_exiting || e.Category is not (UserPreferenceCategory.Color or UserPreferenceCategory.General
            or UserPreferenceCategory.VisualStyle)) return;
        Dispatcher.BeginInvoke(ApplySystemTheme);
    }

    private void ApplySystemTheme()
    {
        _theme = WindowsThemeService.Read();
        SetColor("AccentColor", _theme.Accent);
        SetBrush("AccentBrush", _theme.Accent);
        SetBrush("AccentTextBrush", _theme.AccentText);
        SetBrush("PageBrush", _theme.Page);
        SetBrush("CardBrush", _theme.Card);
        SetBrush("ControlBrush", _theme.Control);
        SetBrush("ControlHoverBrush", _theme.ControlHover);
        SetBrush("ControlPressedBrush", _theme.ControlPressed);
        SetBrush("ControlBorderBrush", _theme.Border);
        SetBrush("TextBrush", _theme.Text);
        SetBrush("SecondaryTextBrush", _theme.SecondaryText);
        SetBrush("DisabledTextBrush", _theme.DisabledText);
        SetBrush("PreviewBrush", _theme.Preview);
        SetBrush("InactiveTitleBrush", _theme.InactiveTitle);
        ApplyTitleBarTheme();
        ApplyTrayMenuTheme();
    }

    private static void SetColor(string key, System.Windows.Media.Color color)
    {
        System.Windows.Application.Current.Resources[key] = color;
    }

    private static void SetBrush(string key, System.Windows.Media.Color color)
    {
        System.Windows.Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private void ApplyTitleBarTheme()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) NativeMethods.SetDarkTitleBar(handle, _theme.IsDark);
    }

    private void ApplyTrayMenuTheme()
    {
        if (_trayMenu is null) return;
        _trayMenu.BackColor = DrawingColor(_theme.Card);
        _trayMenu.ForeColor = DrawingColor(_theme.Text);
        _trayMenu.Renderer = new SystemMenuRenderer(_theme);
        _trayMenu.Invalidate(true);
    }

    private static System.Drawing.Color DrawingColor(System.Windows.Media.Color color) =>
        System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    private void LoadControls()
    {
        EnabledCheckBox.IsChecked = _settings.Enabled;
        LoadSideControls();
        LoadElevatedColorControls();
        LayoutIndicatorCheckBox.IsChecked = _settings.LayoutIndicator.Enabled;
        LayoutContainerCheckBox.IsChecked = _settings.LayoutIndicator.ShowContainer;
        LayoutSizeSlider.Value = Math.Clamp(_settings.LayoutIndicator.Size, 10, 80);
        LayoutOpacitySlider.Value = Math.Clamp(_settings.LayoutIndicator.Opacity, 0.2, 1);
        LayoutOffsetXSlider.Value = Math.Clamp(_settings.LayoutIndicator.OffsetX, -100, 100);
        LayoutOffsetYSlider.Value = Math.Clamp(_settings.LayoutIndicator.OffsetY, -100, 100);
        SelectByTag(LayoutContentCombo, _settings.LayoutIndicator.Content.ToString());
        SelectByTag(LayoutAnchorCombo, _settings.LayoutIndicator.Anchor.ToString());
        LoadLayoutSideControls();
        SelectByTag(RepeatDelayCombo, _settings.Keyboard.RepeatDelay.ToString());
        SelectByTag(RussianSoundCombo, _settings.Keyboard.RussianSound.ToString());
        SelectByTag(EnglishSoundCombo, _settings.Keyboard.EnglishSound.ToString());
        ProfileCombo.SelectedIndex = 0;
        LoadProfileControls();
    }

    private static void AddPreviewRenderer(Grid host, BorderRenderer renderer)
    {
        renderer.IsHitTestVisible = false;
        Grid.SetRowSpan(renderer, 2);
        System.Windows.Controls.Panel.SetZIndex(renderer, 10);
        host.Children.Add(renderer);
    }

    private void LoadSideControls()
    {
        ActiveTopCheckBox.IsChecked = _settings.Active.ShowTop;
        ActiveRightCheckBox.IsChecked = _settings.Active.ShowRight;
        ActiveBottomCheckBox.IsChecked = _settings.Active.ShowBottom;
        ActiveLeftCheckBox.IsChecked = _settings.Active.ShowLeft;
        InactiveTopCheckBox.IsChecked = _settings.Inactive.ShowTop;
        InactiveRightCheckBox.IsChecked = _settings.Inactive.ShowRight;
        InactiveBottomCheckBox.IsChecked = _settings.Inactive.ShowBottom;
        InactiveLeftCheckBox.IsChecked = _settings.Inactive.ShowLeft;
    }

    private void LoadElevatedColorControls()
    {
        ActiveElevatedColorCheckBox.IsChecked = _settings.Active.UseElevatedColor;
        InactiveElevatedColorCheckBox.IsChecked = _settings.Inactive.UseElevatedColor;
    }

    private void LoadLayoutSideControls()
    {
        LayoutTopCheckBox.IsChecked = _settings.LayoutIndicator.Side == LayoutIndicatorSide.Top;
        LayoutRightCheckBox.IsChecked = _settings.LayoutIndicator.Side == LayoutIndicatorSide.Right;
        LayoutBottomCheckBox.IsChecked = _settings.LayoutIndicator.Side == LayoutIndicatorSide.Bottom;
        LayoutLeftCheckBox.IsChecked = _settings.LayoutIndicator.Side == LayoutIndicatorSide.Left;
    }

    private void LoadProfileControls()
    {
        BorderProfile profile = CurrentProfile;
        ThicknessSlider.Value = Math.Clamp(profile.Thickness, 1, 30);
        PaddingSlider.Value = Math.Clamp(profile.Padding, -30, 30);
        RadiusSlider.Value = Math.Clamp(profile.CornerRadius, 0, 40);
        SpeedSlider.Value = Math.Clamp(profile.AnimationSpeed, 0.25, 40);
        AnimateCheckBox.IsChecked = profile.Animate;
        AnimateGradientCheckBox.IsChecked = profile.AnimateGradient;
        SelectByTag(LineStyleCombo, profile.LineStyle.ToString());
        SelectByTag(FillStyleCombo, profile.FillStyle.ToString());
        SelectByTag(DirectionCombo, profile.Direction.ToString());
        UpdateColorButtons();
        UpdateConditionalControls();
    }

    private void ProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        _editingActive = tag == "Active";
        _loading = true;
        LoadProfileControls();
        _loading = false;
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, tag)) ?? combo.Items[0];
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsLoaded) ApplySettings();
    }

    private void KeyboardSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && IsLoaded) ApplySettings();
    }

    private void ApplySettings()
    {
        _settings.Enabled = EnabledCheckBox.IsChecked == true;
        BorderProfile profile = CurrentProfile;
        profile.Thickness = ThicknessSlider.Value;
        profile.Padding = PaddingSlider.Value;
        profile.CornerRadius = RadiusSlider.Value;
        profile.AnimationSpeed = SpeedSlider.Value;
        profile.Animate = AnimateCheckBox.IsChecked == true;
        profile.AnimateGradient = AnimateGradientCheckBox.IsChecked == true;
        profile.LineStyle = ReadTag(LineStyleCombo, BorderLineStyle.Solid);
        profile.FillStyle = ReadTag(FillStyleCombo, BorderFillStyle.Solid);
        profile.Direction = ReadTag(DirectionCombo, AnimationDirection.Clockwise);
        _settings.LayoutIndicator.Enabled = LayoutIndicatorCheckBox.IsChecked == true;
        _settings.LayoutIndicator.Size = LayoutSizeSlider.Value;
        _settings.LayoutIndicator.Opacity = LayoutOpacitySlider.Value;
        _settings.LayoutIndicator.ShowContainer = LayoutContainerCheckBox.IsChecked == true;
        _settings.LayoutIndicator.Content = ReadTag(LayoutContentCombo, LayoutIndicatorContent.FlagAndCode);
        _settings.LayoutIndicator.Anchor = ReadTag(LayoutAnchorCombo, LayoutIndicatorAnchor.Field);
        _settings.LayoutIndicator.OffsetX = LayoutOffsetXSlider.Value;
        _settings.LayoutIndicator.OffsetY = LayoutOffsetYSlider.Value;
        _settings.Keyboard.RepeatDelay = ReadTag(RepeatDelayCombo, KeyboardRepeatDelay.System);
        _settings.Keyboard.RussianSound = ReadTag(RussianSoundCombo, KeySound.None);
        _settings.Keyboard.EnglishSound = ReadTag(EnglishSoundCombo, KeySound.None);
        if (_enabledMenuItem is not null) _enabledMenuItem.Checked = _settings.Enabled;
        ThicknessValue.Text = $"{profile.Thickness:0} px";
        PaddingValue.Text = $"{profile.Padding:+0;-0;0} px";
        RadiusValue.Text = $"{profile.CornerRadius:0} px";
        LayoutSizeValue.Text = $"{_settings.LayoutIndicator.Size:0}";
        LayoutOpacityValue.Text = $"{_settings.LayoutIndicator.Opacity:P0}";
        LayoutOffsetXValue.Text = $"{_settings.LayoutIndicator.OffsetX:0}";
        LayoutOffsetYValue.Text = $"{_settings.LayoutIndicator.OffsetY:0}";
        UpdateConditionalControls();
        _saveTimer.Stop();
        _saveTimer.Start();
        _borderService.Apply(_settings);
        _layoutIndicatorService.Apply(_settings);
        _keyboardService.Apply(_settings);
        _activePreviewRenderer.Update(_settings, 0, true, false);
        _inactivePreviewRenderer.Update(_settings, 0, false, false);
    }

    private void SideSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not System.Windows.Controls.CheckBox { Tag: string tag } checkBox) return;
        string[] parts = tag.Split(',');
        if (parts.Length != 2) return;
        BorderProfile profile = parts[0] == "Active" ? _settings.Active : _settings.Inactive;
        bool value = checkBox.IsChecked == true;
        switch (parts[1])
        {
            case "Top": profile.ShowTop = value; break;
            case "Right": profile.ShowRight = value; break;
            case "Bottom": profile.ShowBottom = value; break;
            case "Left": profile.ShowLeft = value; break;
        }
        ApplySettings();
    }

    private void ElevatedColorSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not System.Windows.Controls.CheckBox { Tag: string tag } checkBox) return;
        BorderProfile profile = tag == "Active" ? _settings.Active : _settings.Inactive;
        profile.UseElevatedColor = checkBox.IsChecked == true;
        ApplySettings();
    }

    private void LayoutSideChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not System.Windows.Controls.CheckBox { Tag: string tag } checkBox) return;
        if (!Enum.TryParse(tag, out LayoutIndicatorSide side)) return;
        _settings.LayoutIndicator.Side = checkBox.IsChecked == true ? side : null;
        _loading = true;
        LoadLayoutSideControls();
        _loading = false;
        ApplySettings();
    }

    private void LayoutPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _previewDragStart = e.GetPosition(LayoutSideSelector);
        _previewOffsetX = LayoutOffsetXSlider.Value;
        _previewOffsetY = LayoutOffsetYSlider.Value;
        LayoutPreviewIndicator.CaptureMouse();
        e.Handled = true;
    }

    private void LayoutPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!LayoutPreviewIndicator.IsMouseCaptured || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        System.Windows.Point current = e.GetPosition(LayoutSideSelector);
        LayoutOffsetXSlider.Value = Math.Clamp(_previewOffsetX + current.X - _previewDragStart.X, -100, 100);
        LayoutOffsetYSlider.Value = Math.Clamp(_previewOffsetY + current.Y - _previewDragStart.Y, -100, 100);
    }

    private void LayoutPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LayoutPreviewIndicator.ReleaseMouseCapture();
        e.Handled = true;
    }

    private static T ReadTag<T>(System.Windows.Controls.ComboBox combo, T fallback) where T : struct
    {
        return combo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse(tag, out T value) ? value : fallback;
    }

    private void ComboBoxMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox combo || combo.Items.Count == 0) return;
        int direction = e.Delta > 0 ? -1 : 1;
        combo.SelectedIndex = Math.Clamp(combo.SelectedIndex + direction, 0, combo.Items.Count - 1);
        e.Handled = true;
    }

    private void UpdateConditionalControls()
    {
        SecondaryColorPanel.Visibility = ReadTag(FillStyleCombo, BorderFillStyle.Solid) == BorderFillStyle.AlongLine
            ? Visibility.Visible : Visibility.Collapsed;
        bool canAnimate = ReadTag(LineStyleCombo, BorderLineStyle.Solid) != BorderLineStyle.Solid;
        bool canAnimateGradient = ReadTag(FillStyleCombo, BorderFillStyle.Solid) == BorderFillStyle.AlongLine;
        AnimateCheckBox.IsEnabled = canAnimate;
        AnimateGradientCheckBox.IsEnabled = canAnimateGradient;
        AnimationOptions.IsEnabled = (canAnimate && AnimateCheckBox.IsChecked == true)
            || (canAnimateGradient && AnimateGradientCheckBox.IsChecked == true);
        ActiveElevatedColorButton.IsEnabled = ActiveElevatedColorCheckBox.IsChecked == true;
        InactiveElevatedColorButton.IsEnabled = InactiveElevatedColorCheckBox.IsChecked == true;
        LayoutIndicatorOptions.IsEnabled = LayoutIndicatorCheckBox.IsChecked == true;
        LayoutPositionOptions.IsEnabled = LayoutIndicatorCheckBox.IsChecked == true;
        LayoutSideSelector.IsEnabled = LayoutIndicatorCheckBox.IsChecked == true;
        LayoutSideSelector.Opacity = 1;
        LayoutPreviewTransform.X = _settings.LayoutIndicator.OffsetX * 0.35;
        LayoutPreviewTransform.Y = _settings.LayoutIndicator.OffsetY * 0.35;
    }

    private void ChoosePrimaryColor(object sender, RoutedEventArgs e)
    {
        if (ChooseColor(CurrentProfile.ParsedColor, out var color))
        {
            CurrentProfile.Color = color.ToString();
            UpdateColorButtons();
            ApplySettings();
        }
    }

    private void ChooseSecondaryColor(object sender, RoutedEventArgs e)
    {
        if (ChooseColor(CurrentProfile.ParsedSecondaryColor, out var color))
        {
            CurrentProfile.SecondaryColor = color.ToString();
            UpdateColorButtons();
            ApplySettings();
        }
    }

    private void ChooseElevatedColor(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag }) return;
        BorderProfile profile = tag == "Active" ? _settings.Active : _settings.Inactive;
        if (ChooseColor(profile.ParsedElevatedColor, out var color))
        {
            profile.ElevatedColor = color.ToString();
            UpdateColorButtons();
            ApplySettings();
        }
    }

    private static bool ChooseColor(System.Windows.Media.Color initial, out System.Windows.Media.Color result)
    {
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B)
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            result = System.Windows.Media.Color.FromArgb(255, dialog.Color.R, dialog.Color.G, dialog.Color.B);
            return true;
        }
        result = initial;
        return false;
    }

    private void UpdateColorButtons()
    {
        BorderProfile profile = CurrentProfile;
        PrimarySwatch.Background = new SolidColorBrush(profile.ParsedColor);
        PrimaryColorText.Text = profile.ParsedColor.ToString()[^6..];
        SecondarySwatch.Background = new SolidColorBrush(profile.ParsedSecondaryColor);
        SecondaryColorText.Text = profile.ParsedSecondaryColor.ToString()[^6..];
        ActiveElevatedSwatch.Background = new SolidColorBrush(_settings.Active.ParsedElevatedColor);
        ActiveElevatedColorText.Text = _settings.Active.ParsedElevatedColor.ToString()[^6..];
        InactiveElevatedSwatch.Background = new SolidColorBrush(_settings.Inactive.ParsedElevatedColor);
        InactiveElevatedColorText.Text = _settings.Inactive.ParsedElevatedColor.ToString()[^6..];
    }

    private void ToggleEnabled()
    {
        Dispatcher.Invoke(() => EnabledCheckBox.IsChecked = !(EnabledCheckBox.IsChecked == true));
    }

    private void HideToTray(object sender, RoutedEventArgs e) => Hide();

    private void ShowFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void ExitApplication()
    {
        Dispatcher.Invoke(() =>
        {
            _exiting = true;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _saveTimer.Stop();
            SettingsStore.Save(_settings);
            _borderService.Dispose();
            _layoutIndicatorService.Dispose();
            _keyboardService.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Close();
            System.Windows.Application.Current.Shutdown();
        });
    }

    private sealed class SystemMenuColorTable(WindowsThemePalette theme) : Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => DrawingColor(theme.Card);
        public override System.Drawing.Color ImageMarginGradientBegin => DrawingColor(theme.Card);
        public override System.Drawing.Color ImageMarginGradientMiddle => DrawingColor(theme.Card);
        public override System.Drawing.Color ImageMarginGradientEnd => DrawingColor(theme.Card);
        public override System.Drawing.Color MenuItemSelected => theme.IsDark
            ? System.Drawing.Color.FromArgb(55, 55, 55)
            : DrawingColor(theme.ControlHover);
        public override System.Drawing.Color MenuItemBorder => DrawingColor(theme.Accent);
        public override System.Drawing.Color MenuBorder => DrawingColor(theme.Border);
        public override System.Drawing.Color SeparatorDark => DrawingColor(theme.Border);
        public override System.Drawing.Color SeparatorLight => DrawingColor(theme.Border);
    }

    private sealed class SystemMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private readonly WindowsThemePalette _theme;

        public SystemMenuRenderer(WindowsThemePalette theme) : base(new SystemMenuColorTable(theme)) => _theme = theme;

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!_theme.IsDark || !e.Item.Selected)
            {
                base.OnRenderMenuItemBackground(e);
                return;
            }

            using var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(55, 55, 55));
            e.Graphics.FillRectangle(background, new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size));
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? DrawingColor(_theme.Text)
                : DrawingColor(_theme.DisabledText);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var box = new System.Drawing.Rectangle(e.ImageRectangle.X + 1, e.ImageRectangle.Y + 1,
                Math.Max(14, e.ImageRectangle.Width - 2), Math.Max(14, e.ImageRectangle.Height - 2));
            using var background = new System.Drawing.SolidBrush(DrawingColor(_theme.Accent));
            using var border = new System.Drawing.Pen(DrawingColor(_theme.AccentPressed));
            using var check = new System.Drawing.Pen(DrawingColor(_theme.AccentText), 2f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            e.Graphics.FillRectangle(background, box);
            e.Graphics.DrawRectangle(border, box);
            e.Graphics.DrawLines(check,
            [
                new System.Drawing.Point(box.Left + 3, box.Top + box.Height / 2),
                new System.Drawing.Point(box.Left + box.Width / 2 - 1, box.Bottom - 4),
                new System.Drawing.Point(box.Right - 3, box.Top + 4)
            ]);
        }
    }
}
