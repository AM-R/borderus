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
using Forms = System.Windows.Forms;

namespace Borderus;

public partial class MainWindow : Window
{
    private readonly BorderSettings _settings;
    private readonly WindowBorderService _borderService;
    private readonly LayoutIndicatorService _layoutIndicatorService;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly DispatcherTimer _saveTimer;
    private readonly BorderRenderer _activePreviewRenderer = new();
    private readonly BorderRenderer _inactivePreviewRenderer = new();
    private Forms.ToolStripMenuItem? _enabledMenuItem;
    private bool _loading = true;
    private bool _exiting;
    private bool _editingActive = true;

    private BorderProfile CurrentProfile => _editingActive ? _settings.Active : _settings.Inactive;

    public MainWindow()
    {
        InitializeComponent();
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
        SourceInitialized += (_, _) => NativeMethods.EnableDarkTitleBar(new WindowInteropHelper(this).Handle);
        LoadControls();
        _borderService = new WindowBorderService(_settings);
        _layoutIndicatorService = new LayoutIndicatorService(_settings);
        _trayIcon = CreateTrayIcon();
        _loading = false;
        ApplySettings();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(16, 42, 58),
            ForeColor = System.Drawing.Color.FromArgb(244, 244, 244),
            Renderer = new DarkMenuRenderer()
        };
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

    private void ApplySettings()
    {
        _settings.Enabled = EnabledCheckBox.IsChecked == true;
        BorderProfile profile = CurrentProfile;
        profile.Thickness = ThicknessSlider.Value;
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
        if (_enabledMenuItem is not null) _enabledMenuItem.Checked = _settings.Enabled;
        ThicknessValue.Text = $"{profile.Thickness:0} px";
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
        if (checkBox.IsChecked != true)
        {
            _loading = true;
            checkBox.IsChecked = true;
            _loading = false;
            return;
        }
        if (!Enum.TryParse(tag, out LayoutIndicatorSide side)) return;
        _settings.LayoutIndicator.Side = side;
        _loading = true;
        LoadLayoutSideControls();
        _loading = false;
        ApplySettings();
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
        bool caretAnchor = ReadTag(LayoutAnchorCombo, LayoutIndicatorAnchor.Field) == LayoutIndicatorAnchor.Caret;
        LayoutSideSelector.IsEnabled = LayoutIndicatorCheckBox.IsChecked == true && !caretAnchor;
        LayoutSideSelector.Opacity = caretAnchor ? 0.55 : 1;
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
            _saveTimer.Stop();
            SettingsStore.Save(_settings);
            _borderService.Dispose();
            _layoutIndicatorService.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Close();
            System.Windows.Application.Current.Shutdown();
        });
    }

    private sealed class DarkMenuColorTable : Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(16, 42, 58);
        public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(16, 42, 58);
        public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(16, 42, 58);
        public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(16, 42, 58);
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(22, 79, 120);
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(38, 104, 148);
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(75, 75, 75);
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(75, 75, 75);
        public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(75, 75, 75);
    }

    private sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColorTable()) { }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? System.Drawing.Color.FromArgb(244, 244, 244)
                : System.Drawing.Color.FromArgb(120, 120, 120);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var box = new System.Drawing.Rectangle(e.ImageRectangle.X + 1, e.ImageRectangle.Y + 1,
                Math.Max(14, e.ImageRectangle.Width - 2), Math.Max(14, e.ImageRectangle.Height - 2));
            using var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(22, 79, 120));
            using var border = new System.Drawing.Pen(System.Drawing.Color.FromArgb(38, 104, 148));
            using var check = new System.Drawing.Pen(System.Drawing.Color.White, 2f)
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
