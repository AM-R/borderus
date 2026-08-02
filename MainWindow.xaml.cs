using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly DispatcherTimer _sliderApplyTimer;
    private readonly BorderRenderer _activePreviewRenderer = new();
    private readonly BorderRenderer _inactivePreviewRenderer = new();
    private Forms.ToolStripMenuItem? _openMenuItem;
    private Forms.ToolStripMenuItem? _enabledMenuItem;
    private Forms.ToolStripMenuItem? _bordersMenuItem;
    private Forms.ToolStripMenuItem? _soundMenuItem;
    private Forms.ToolStripMenuItem? _repeatMenuItem;
    private Forms.ToolStripMenuItem? _layoutMenuItem;
    private Forms.ToolStripMenuItem? _startupMenuItem;
    private Forms.ToolStripMenuItem? _exitMenuItem;
    private Forms.ContextMenuStrip? _trayMenu;
    private WindowsThemePalette _theme;
    private bool _loading = true;
    private bool _exiting;
    private bool _editingActive = true;
    private bool _sliderDragging;
    private bool _sliderChangesPending;
    private System.Windows.Point _previewDragStart;
    private double _previewOffsetX;
    private double _previewOffsetY;

    private BorderProfile CurrentProfile => _editingActive ? _settings.Active : _settings.Inactive;

    public MainWindow(BorderSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        ApplySystemTheme();
        AddPreviewRenderer(ActivePreviewHost, _activePreviewRenderer);
        AddPreviewRenderer(InactivePreviewHost, _inactivePreviewRenderer);
        VersionText.Text = GetVersionText();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SettingsStore.Save(_settings);
        };
        _sliderApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _sliderApplyTimer.Tick += (_, _) =>
        {
            _sliderApplyTimer.Stop();
            if (!_loading && !_sliderDragging && _sliderChangesPending) ApplySettings();
        };
        AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(SliderDragStarted));
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(SliderDragCompleted));
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        LoadControls();
        _borderService = new WindowBorderService(Dispatcher, _settings);
        _layoutIndicatorService = new LayoutIndicatorService(Dispatcher, _settings);
        _keyboardService = new KeyboardService(_settings);
        _trayIcon = CreateTrayIcon();
        UpdateLocalizedText();
        _loading = false;
        ApplySettings();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        _trayMenu = menu;
        ApplyTrayMenuTheme();
        _openMenuItem = new Forms.ToolStripMenuItem(LocalizationService.Text("TrayOpen"), null, (_, _) => ShowFromTray());
        menu.Items.Add(_openMenuItem);
        _enabledMenuItem = new Forms.ToolStripMenuItem(LocalizationService.Text("Enabled"), null, (_, _) => ToggleEnabled())
        {
            Checked = _settings.Enabled,
            CheckOnClick = false
        };
        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _bordersMenuItem = new Forms.ToolStripMenuItem(LocalizationService.Text("EnableBorders"), null, (_, _) => ToggleBorders())
        {
            Checked = _settings.BordersEnabled,
            CheckOnClick = false
        };
        menu.Items.Add(_bordersMenuItem);
        _layoutMenuItem = new Forms.ToolStripMenuItem(LocalizationService.Text("EnableFlag"), null, (_, _) => ToggleLayoutIndicator())
        {
            Checked = _settings.LayoutIndicator.Enabled,
            CheckOnClick = false
        };
        menu.Items.Add(_layoutMenuItem);
        _repeatMenuItem = new Forms.ToolStripMenuItem(LocalizationService.Text("EnableRepeat"), null, (_, _) => ToggleRepeat())
        {
            Checked = _settings.Keyboard.RepeatEnabled,
            CheckOnClick = false
        };
        menu.Items.Add(_repeatMenuItem);
        _soundMenuItem = new Forms.ToolStripMenuItem(LocalizationService.Text("EnableSounds"), null, (_, _) => ToggleSound())
        {
            Checked = _settings.Keyboard.SoundEnabled,
            CheckOnClick = false
        };
        menu.Items.Add(_soundMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _startupMenuItem = new Forms.ToolStripMenuItem(LocalizationService.Text("EnableStartup"), null, (_, _) => ToggleStartup())
        {
            Checked = _settings.StartWithWindows,
            CheckOnClick = false
        };
        menu.Items.Add(_startupMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exitMenuItem = new Forms.ToolStripMenuItem(LocalizationService.Text("TrayExit"), null, (_, _) => ExitApplication());
        menu.Items.Add(_exitMenuItem);
        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = $"Border {GetVersionText().TrimStart('v')}",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private static string GetVersionText()
    {
        Version? version = typeof(MainWindow).Assembly.GetName().Version;
        return version is null ? string.Empty : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private static DateTime GetBuildDate()
    {
        string? value = typeof(MainWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildDate")?.Value;
        return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime date) ? date : new DateTime(2026, 8, 2);
    }

    private void UpdateLocalizedText()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(_settings.Language == "ru" ? "ru-RU" : "en-US");
        DateTime buildDate = GetBuildDate();
        string month = culture.TextInfo.ToTitleCase(culture.DateTimeFormat
            .GetAbbreviatedMonthName(buildDate.Month).TrimEnd('.'));
        string suffix = _settings.Language == "ru" ? "." : string.Empty;
        AboutVersionText.Text = $"{LocalizationService.Text("VersionLabel")}: {GetVersionText()} " +
            $"({buildDate:dd} {month}{suffix} {buildDate:yyyy})";
        if (_trayMenu is null) return;
        if (_openMenuItem is not null) _openMenuItem.Text = LocalizationService.Text("TrayOpen");
        if (_enabledMenuItem is not null) _enabledMenuItem.Text = LocalizationService.Text("Enabled");
        if (_bordersMenuItem is not null) _bordersMenuItem.Text = LocalizationService.Text("EnableBorders");
        if (_soundMenuItem is not null) _soundMenuItem.Text = LocalizationService.Text("EnableSounds");
        if (_repeatMenuItem is not null) _repeatMenuItem.Text = LocalizationService.Text("EnableRepeat");
        if (_layoutMenuItem is not null) _layoutMenuItem.Text = LocalizationService.Text("EnableFlag");
        if (_startupMenuItem is not null) _startupMenuItem.Text = LocalizationService.Text("EnableStartup");
        if (_exitMenuItem is not null) _exitMenuItem.Text = LocalizationService.Text("TrayExit");
        _trayIcon.Text = $"Border {GetVersionText().TrimStart('v')}";
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
        BordersEnabledCheckBox.IsChecked = _settings.BordersEnabled;
        ShowInFullscreenCheckBox.IsChecked = _settings.ShowInFullscreen;
        SelectByTag(LanguageCombo, _settings.Language);
        StartupCheckBox.IsChecked = _settings.StartWithWindows;
        LoadSideControls();
        LoadElevatedColorControls();
        LayoutIndicatorCheckBox.IsChecked = _settings.LayoutIndicator.Enabled;
        RepeatEnabledCheckBox.IsChecked = _settings.Keyboard.RepeatEnabled;
        SoundEnabledCheckBox.IsChecked = _settings.Keyboard.SoundEnabled;
        LayoutContainerCheckBox.IsChecked = _settings.LayoutIndicator.ShowContainer;
        SelectByTag(LayoutInputModeCombo, _settings.LayoutIndicator.InputMode.ToString());
        LayoutSizeSlider.Value = Math.Clamp(_settings.LayoutIndicator.Size, 10, 80);
        LayoutOpacitySlider.Value = Math.Clamp(_settings.LayoutIndicator.Opacity, 0.2, 1);
        LayoutOffsetXSlider.Value = Math.Clamp(_settings.LayoutIndicator.OffsetX, -100, 100);
        LayoutOffsetYSlider.Value = Math.Clamp(_settings.LayoutIndicator.OffsetY, -100, 100);
        SelectByTag(LayoutContentCombo, _settings.LayoutIndicator.Content.ToString());
        SelectByTag(LayoutAnchorCombo, _settings.LayoutIndicator.Anchor.ToString());
        SelectByTag(LayoutDefaultSideCombo, _settings.LayoutIndicator.DefaultSide.ToString());
        SelectByTag(LayoutWebSideCombo, _settings.LayoutIndicator.WebSide.ToString());
        LoadLayoutSideControls();
        RepeatDelaySlider.Value = Math.Clamp(_settings.Keyboard.RepeatDelayMs, 10, 1000);
        RepeatRateSlider.Value = Math.Clamp(_settings.Keyboard.RepeatIntervalMs, 5, 250);
        NonCharacterRepeatDelaySlider.Value = Math.Clamp(_settings.Keyboard.NonCharacterRepeatDelayMs, 10, 1000);
        NonCharacterRepeatRateSlider.Value = Math.Clamp(_settings.Keyboard.NonCharacterRepeatIntervalMs, 5, 250);
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
        UpdateSliderValueLabels();
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

    private void BorderEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading) ApplySettings();
    }

    private void SliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || !IsLoaded || sender is not Slider slider) return;

        if (slider == ThicknessSlider) CurrentProfile.Thickness = slider.Value;
        else if (slider == PaddingSlider) CurrentProfile.Padding = slider.Value;
        else if (slider == RadiusSlider) CurrentProfile.CornerRadius = slider.Value;
        else if (slider == SpeedSlider) CurrentProfile.AnimationSpeed = slider.Value;
        else if (slider == LayoutSizeSlider) _settings.LayoutIndicator.Size = slider.Value;
        else if (slider == LayoutOpacitySlider) _settings.LayoutIndicator.Opacity = slider.Value;
        else if (slider == LayoutOffsetXSlider) _settings.LayoutIndicator.OffsetX = slider.Value;
        else if (slider == LayoutOffsetYSlider) _settings.LayoutIndicator.OffsetY = slider.Value;
        else if (slider == RepeatDelaySlider) _settings.Keyboard.RepeatDelayMs = (int)slider.Value;
        else if (slider == RepeatRateSlider) _settings.Keyboard.RepeatIntervalMs = (int)slider.Value;
        else if (slider == NonCharacterRepeatDelaySlider) _settings.Keyboard.NonCharacterRepeatDelayMs = (int)slider.Value;
        else if (slider == NonCharacterRepeatRateSlider) _settings.Keyboard.NonCharacterRepeatIntervalMs = (int)slider.Value;

        UpdateSliderValueLabels();
        LayoutPreviewTransform.X = _settings.LayoutIndicator.OffsetX * 0.35;
        LayoutPreviewTransform.Y = _settings.LayoutIndicator.OffsetY * 0.35;
        _sliderChangesPending = true;
        _sliderApplyTimer.Stop();
        if (!_sliderDragging) _sliderApplyTimer.Start();
    }

    private void SliderDragStarted(object sender, DragStartedEventArgs e)
    {
        _sliderDragging = true;
        _sliderApplyTimer.Stop();
    }

    private void SliderDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _sliderDragging = false;
        if (_loading || !_sliderChangesPending) return;
        _sliderApplyTimer.Stop();
        ApplySettings();
    }

    private void KeyboardSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        ApplySettings();
        if (!_settings.Keyboard.SoundEnabled) return;
        if (sender == RussianSoundCombo)
            _keyboardService.Preview(_settings.Keyboard.RussianSound, _settings.Keyboard.RussianSoundFile);
        else if (sender == EnglishSoundCombo)
            _keyboardService.Preview(_settings.Keyboard.EnglishSound, _settings.Keyboard.EnglishSoundFile);
    }

    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string language }) return;
        _settings.Language = LocalizationService.Normalize(language);
        LocalizationService.Apply(_settings.Language);
        UpdateLocalizedText();
        ApplySettings();
    }

    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool enabled = StartupCheckBox.IsChecked == true;
        if (StartupService.SetEnabled(enabled))
        {
            _settings.StartWithWindows = enabled;
            if (_startupMenuItem is not null) _startupMenuItem.Checked = enabled;
            UpdateConditionalControls();
            SettingsStore.Save(_settings);
            return;
        }

        _loading = true;
        StartupCheckBox.IsChecked = !enabled;
        _loading = false;
        if (_startupMenuItem is not null) _startupMenuItem.Checked = _settings.StartWithWindows;
        UpdateConditionalControls();
        System.Windows.MessageBox.Show(this, LocalizationService.Text("StartupError"),
            LocalizationService.Text("StartupErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ResetRepeatDelay(object sender, RoutedEventArgs e) => RepeatDelaySlider.Value = 250;

    private void ResetRepeatRate(object sender, RoutedEventArgs e) => RepeatRateSlider.Value = 25;

    private void ResetNonCharacterRepeatDelay(object sender, RoutedEventArgs e) => NonCharacterRepeatDelaySlider.Value = 250;

    private void ResetNonCharacterRepeatRate(object sender, RoutedEventArgs e) => NonCharacterRepeatRateSlider.Value = 25;

    private void ChooseRussianSound(object sender, RoutedEventArgs e) => ChooseKeyboardSound(true);

    private void ChooseEnglishSound(object sender, RoutedEventArgs e) => ChooseKeyboardSound(false);

    private void ChooseKeyboardSound(bool russian)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Text(russian ? "RussianSoundDialog" : "EnglishSoundDialog"),
            Filter = LocalizationService.Text("WavFilter"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (russian)
        {
            _settings.Keyboard.RussianSoundFile = dialog.FileName;
            SelectByTag(RussianSoundCombo, KeySound.Custom.ToString());
        }
        else
        {
            _settings.Keyboard.EnglishSoundFile = dialog.FileName;
            SelectByTag(EnglishSoundCombo, KeySound.Custom.ToString());
        }
        ApplySettings();
        _keyboardService.Preview(KeySound.Custom, dialog.FileName);
    }

    private void ApplySettings()
    {
        _sliderApplyTimer.Stop();
        _sliderChangesPending = false;
        _settings.Enabled = EnabledCheckBox.IsChecked == true;
        _settings.BordersEnabled = BordersEnabledCheckBox.IsChecked == true;
        _settings.ShowInFullscreen = ShowInFullscreenCheckBox.IsChecked == true;
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
        _settings.LayoutIndicator.Content = ReadTag(LayoutContentCombo, LayoutIndicatorContent.FlagOnly);
        _settings.LayoutIndicator.InputMode = ReadTag(LayoutInputModeCombo, LayoutIndicatorInputMode.TextInputOnly);
        _settings.LayoutIndicator.Anchor = ReadTag(LayoutAnchorCombo, LayoutIndicatorAnchor.Caret);
        _settings.LayoutIndicator.DefaultSide = ReadTag(LayoutDefaultSideCombo, LayoutIndicatorHorizontalSide.Right);
        _settings.LayoutIndicator.WebSide = ReadTag(LayoutWebSideCombo, LayoutIndicatorHorizontalSide.Right);
        _settings.LayoutIndicator.OffsetX = LayoutOffsetXSlider.Value;
        _settings.LayoutIndicator.OffsetY = LayoutOffsetYSlider.Value;
        _settings.Keyboard.RepeatEnabled = RepeatEnabledCheckBox.IsChecked == true;
        _settings.Keyboard.SoundEnabled = SoundEnabledCheckBox.IsChecked == true;
        _settings.Keyboard.RepeatDelayMs = (int)RepeatDelaySlider.Value;
        _settings.Keyboard.RepeatIntervalMs = (int)RepeatRateSlider.Value;
        _settings.Keyboard.NonCharacterRepeatDelayMs = (int)NonCharacterRepeatDelaySlider.Value;
        _settings.Keyboard.NonCharacterRepeatIntervalMs = (int)NonCharacterRepeatRateSlider.Value;
        _settings.Keyboard.RussianSound = ReadTag(RussianSoundCombo, KeySound.None);
        _settings.Keyboard.EnglishSound = ReadTag(EnglishSoundCombo, KeySound.None);
        if (_enabledMenuItem is not null) _enabledMenuItem.Checked = _settings.Enabled;
        if (_bordersMenuItem is not null)
        {
            _bordersMenuItem.Checked = _settings.BordersEnabled;
            _bordersMenuItem.Enabled = _settings.Enabled;
        }
        if (_soundMenuItem is not null)
        {
            _soundMenuItem.Checked = _settings.Keyboard.SoundEnabled;
            _soundMenuItem.Enabled = _settings.Enabled;
        }
        if (_repeatMenuItem is not null)
        {
            _repeatMenuItem.Checked = _settings.Keyboard.RepeatEnabled;
            _repeatMenuItem.Enabled = _settings.Enabled;
        }
        if (_layoutMenuItem is not null)
        {
            _layoutMenuItem.Checked = _settings.LayoutIndicator.Enabled;
            _layoutMenuItem.Enabled = _settings.Enabled;
        }
        if (_startupMenuItem is not null) _startupMenuItem.Checked = _settings.StartWithWindows;
        UpdateSliderValueLabels();
        UpdateConditionalControls();
        _saveTimer.Stop();
        _saveTimer.Start();
        _borderService.Apply(_settings);
        _layoutIndicatorService.Apply(_settings);
        _keyboardService.Apply(_settings);
        _activePreviewRenderer.Update(_settings, 0, true, false);
        _inactivePreviewRenderer.Update(_settings, 0, false, false);
    }

    private void UpdateSliderValueLabels()
    {
        BorderProfile profile = CurrentProfile;
        ThicknessValue.Text = $"{profile.Thickness:0} px";
        PaddingValue.Text = $"{profile.Padding:+0;-0;0} px";
        RadiusValue.Text = $"{profile.CornerRadius:0} px";
        LayoutSizeValue.Text = $"{_settings.LayoutIndicator.Size:0}";
        LayoutOpacityValue.Text = $"{_settings.LayoutIndicator.Opacity:P0}";
        LayoutOffsetXValue.Text = $"{_settings.LayoutIndicator.OffsetX:0}";
        LayoutOffsetYValue.Text = $"{_settings.LayoutIndicator.OffsetY:0}";
        string milliseconds = LocalizationService.Text("MillisecondsShort");
        RepeatDelayValue.Text = $"{_settings.Keyboard.RepeatDelayMs} {milliseconds}";
        RepeatRateValue.Text = $"{_settings.Keyboard.RepeatIntervalMs} {milliseconds}";
        NonCharacterRepeatDelayValue.Text = $"{_settings.Keyboard.NonCharacterRepeatDelayMs} {milliseconds}";
        NonCharacterRepeatRateValue.Text = $"{_settings.Keyboard.NonCharacterRepeatIntervalMs} {milliseconds}";
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

    private void DefaultLayoutSideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        _settings.LayoutIndicator.Side = null;
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
        bool bordersEnabled = BordersEnabledCheckBox.IsChecked == true;
        bool repeatEnabled = RepeatEnabledCheckBox.IsChecked == true;
        bool soundEnabled = SoundEnabledCheckBox.IsChecked == true;
        bool layoutEnabled = LayoutIndicatorCheckBox.IsChecked == true;
        SetOptionsState(BorderPreviewOptions, bordersEnabled);
        SetOptionsState(BorderAppearanceOptions, bordersEnabled);
        SetOptionsState(RepeatOptions, repeatEnabled);
        SetOptionsState(SoundOptions, soundEnabled);
        SetCardState(LayoutCard, layoutEnabled);
        SetCardState(RepeatCard, repeatEnabled);
        SetCardState(SoundCard, soundEnabled);
        SetFeatureTabState(BordersTab, BordersTabHeader, bordersEnabled);
        SetFeatureTabState(LayoutTab, LayoutTabHeader, layoutEnabled);
        SetFeatureTabState(KeysTab, KeysTabHeader, repeatEnabled);
        SetFeatureTabState(SoundsTab, SoundsTabHeader, soundEnabled);
        SetFeatureTextState(LayoutHeading, layoutEnabled);
        SetFeatureTextState(KeyboardHeading, repeatEnabled);
        SetFeatureTextState(SoundHeading, soundEnabled);
        SecondaryColorPanel.Visibility = ReadTag(FillStyleCombo, BorderFillStyle.Solid) == BorderFillStyle.AlongLine
            ? Visibility.Visible : Visibility.Collapsed;
        bool canAnimate = ReadTag(LineStyleCombo, BorderLineStyle.Solid) != BorderLineStyle.Solid;
        bool canAnimateGradient = ReadTag(FillStyleCombo, BorderFillStyle.Solid) == BorderFillStyle.AlongLine;
        AnimateCheckBox.IsEnabled = canAnimate;
        AnimateGradientCheckBox.IsEnabled = canAnimateGradient;
        bool animationEnabled = (canAnimate && AnimateCheckBox.IsChecked == true)
            || (canAnimateGradient && AnimateGradientCheckBox.IsChecked == true);
        AnimationOptions.IsEnabled = animationEnabled;
        SpeedOptions.Opacity = animationEnabled ? 1 : 0.45;
        bool motionAvailable = bordersEnabled && (canAnimate || canAnimateGradient);
        SetFeatureTextState(MotionHeading, motionAvailable);
        SetCardState(MotionCard, motionAvailable);
        ActiveElevatedColorButton.IsEnabled = ActiveElevatedColorCheckBox.IsChecked == true;
        InactiveElevatedColorButton.IsEnabled = InactiveElevatedColorCheckBox.IsChecked == true;
        SetOptionsState(LayoutIndicatorOptions, layoutEnabled);
        SetOptionsState(LayoutPositionContent, layoutEnabled);
        SetFeatureTextState(LayoutPositionHeading, layoutEnabled);
        SetCardState(LayoutPositionOptions, layoutEnabled);
        LayoutPreviewTransform.X = _settings.LayoutIndicator.OffsetX * 0.35;
        LayoutPreviewTransform.Y = _settings.LayoutIndicator.OffsetY * 0.35;
    }

    private static void SetOptionsState(UIElement element, bool enabled)
    {
        element.IsEnabled = enabled;
        element.Opacity = enabled ? 1 : 0.45;
    }

    private static void SetCardState(System.Windows.Controls.Border card, bool enabled) =>
        card.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,
            enabled ? "CardBrush" : "DisabledCardBrush");

    private static void SetFeatureTextState(TextBlock heading, bool enabled) =>
        heading.SetResourceReference(TextBlock.ForegroundProperty, enabled ? "TextBrush" : "DisabledTextBrush");

    private static void SetFeatureTabState(TabItem tab, TextBlock header, bool enabled)
    {
        tab.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,
            enabled ? "TextBrush" : "SecondaryTextBrush");
        header.FontWeight = enabled ? FontWeights.Bold : FontWeights.Normal;
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
        PrimaryColorText.Text = $"#{profile.ParsedColor.ToString()[^6..]}";
        SecondarySwatch.Background = new SolidColorBrush(profile.ParsedSecondaryColor);
        SecondaryColorText.Text = $"#{profile.ParsedSecondaryColor.ToString()[^6..]}";
        ActiveElevatedSwatch.Background = new SolidColorBrush(_settings.Active.ParsedElevatedColor);
        ActiveElevatedColorText.Text = $"#{_settings.Active.ParsedElevatedColor.ToString()[^6..]}";
        InactiveElevatedSwatch.Background = new SolidColorBrush(_settings.Inactive.ParsedElevatedColor);
        InactiveElevatedColorText.Text = $"#{_settings.Inactive.ParsedElevatedColor.ToString()[^6..]}";
    }

    private void ToggleEnabled()
    {
        Dispatcher.Invoke(() => EnabledCheckBox.IsChecked = !(EnabledCheckBox.IsChecked == true));
    }

    private void ToggleBorders() => Dispatcher.Invoke(() =>
        BordersEnabledCheckBox.IsChecked = !(BordersEnabledCheckBox.IsChecked == true));

    private void ToggleSound() => Dispatcher.Invoke(() =>
        SoundEnabledCheckBox.IsChecked = !(SoundEnabledCheckBox.IsChecked == true));

    private void ToggleRepeat() => Dispatcher.Invoke(() =>
        RepeatEnabledCheckBox.IsChecked = !(RepeatEnabledCheckBox.IsChecked == true));

    private void ToggleLayoutIndicator() => Dispatcher.Invoke(() =>
        LayoutIndicatorCheckBox.IsChecked = !(LayoutIndicatorCheckBox.IsChecked == true));

    private void ToggleStartup() => Dispatcher.Invoke(() =>
        StartupCheckBox.IsChecked = !(StartupCheckBox.IsChecked == true));

    private void HideToTray(object sender, RoutedEventArgs e) => Hide();

    private void OpenRepository(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/AM-R/borderus") { UseShellExecute = true });
    }

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
            _sliderApplyTimer.Stop();
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
