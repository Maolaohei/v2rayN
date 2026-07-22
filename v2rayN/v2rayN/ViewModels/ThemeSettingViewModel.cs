using MaterialDesignColors;
using MaterialDesignColors.ColorManipulation;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace v2rayN.ViewModels;

public class ThemeSettingViewModel : MyReactiveObject
{
    private readonly PaletteHelper _paletteHelper = new();
    private UserPreferenceChangedEventHandler? _systemColorHandler;

    // Locked dual palette (apple-design-mockups/v3)
    private static readonly Color DaySelect = Color.FromRgb(0x2F, 0x6F, 0xED);
    private static readonly Color DaySelectSoft = Color.FromRgb(0xEB, 0xF1, 0xFF);
    private static readonly Color DaySignal = Color.FromRgb(0x0F, 0x9F, 0x6E);
    private static readonly Color DaySignalSoft = Color.FromRgb(0xE6, 0xF7, 0xF0);
    private static readonly Color DayInk = Color.FromRgb(0x0B, 0x0C, 0x0F);
    private static readonly Color DayInk2 = Color.FromRgb(0x3A, 0x3D, 0x45);
    private static readonly Color DayInk3 = Color.FromRgb(0x6B, 0x70, 0x80);
    private static readonly Color DayCanvas = Color.FromRgb(0xF2, 0xF3, 0xF7);
    private static readonly Color DayCard = Colors.White;
    private static readonly Color DayChip = Color.FromArgb(0x1F, 0x76, 0x76, 0x80);
    private static readonly Color DayLine = Color.FromArgb(0x14, 0x0F, 0x12, 0x1C);
    private static readonly Color DayLineStrong = Color.FromArgb(0x24, 0x0F, 0x12, 0x1C);
    private static readonly Color DayWarn = Color.FromRgb(0xC4, 0x7B, 0x12);
    private static readonly Color DayWarnSoft = Color.FromRgb(0xFF, 0xF4, 0xE5);
    private static readonly Color DayDanger = Color.FromRgb(0xD9, 0x2D, 0x20);
    private static readonly Color DayDangerSoft = Color.FromRgb(0xFD, 0xEC, 0xEB);
    private static readonly Color DayOrbOff = Color.FromRgb(0x8B, 0x90, 0xA0);

    private static readonly Color NightSelect = Color.FromRgb(0x5B, 0x9D, 0xFF);
    private static readonly Color NightSelectSoft = Color.FromArgb(0x2E, 0x5B, 0x9D, 0xFF);
    private static readonly Color NightSignal = Color.FromRgb(0x30, 0xD1, 0x58);
    private static readonly Color NightSignalSoft = Color.FromArgb(0x29, 0x30, 0xD1, 0x58);
    private static readonly Color NightInk = Color.FromRgb(0xF5, 0xF5, 0xF7);
    private static readonly Color NightInk2 = Color.FromRgb(0xC7, 0xC7, 0xCC);
    private static readonly Color NightInk3 = Color.FromRgb(0x8E, 0x8E, 0x93);
    private static readonly Color NightCanvas = Color.FromRgb(0x1C, 0x1C, 0x1E);
    private static readonly Color NightCard = Color.FromRgb(0x2C, 0x2C, 0x2E);
    private static readonly Color NightChip = Color.FromArgb(0x52, 0x78, 0x78, 0x80);
    private static readonly Color NightLine = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
    private static readonly Color NightLineStrong = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
    private static readonly Color NightWarn = Color.FromRgb(0xFF, 0x9F, 0x0A);
    private static readonly Color NightWarnSoft = Color.FromArgb(0x29, 0xFF, 0x9F, 0x0A);
    private static readonly Color NightDanger = Color.FromRgb(0xFF, 0x45, 0x3A);
    private static readonly Color NightDangerSoft = Color.FromArgb(0x29, 0xFF, 0x45, 0x3A);
    private static readonly Color NightOrbOff = Color.FromRgb(0x63, 0x63, 0x66);

    private IObservableCollection<Swatch> _swatches = new ObservableCollectionExtended<Swatch>();
    public IObservableCollection<Swatch> Swatches => _swatches;

    [Reactive]
    public Swatch SelectedSwatch { get; set; }

    [Reactive] public string CurrentTheme { get; set; }

    [Reactive] public int CurrentFontSize { get; set; }

    [Reactive] public string CurrentLanguage { get; set; }

    public ThemeSettingViewModel()
    {
        _config = AppManager.Instance.Config;
        RegisterSystemColorSet(_config, ModifyTheme);
        BindingUI();
        RestoreUI();
    }

    private void RestoreUI()
    {
        var configChanged = false;

        // Locked dual palette — discard free Material swatch picks.
        if (!_config.UiItem.ColorPrimaryName.IsNullOrEmpty())
        {
            _config.UiItem.ColorPrimaryName = string.Empty;
            configChanged = true;
        }

        // Normalize legacy fancy themes into FollowSystem.
        if (_config.UiItem.CurrentTheme is not (nameof(ETheme.FollowSystem) or nameof(ETheme.Light) or nameof(ETheme.Dark)))
        {
            _config.UiItem.CurrentTheme = nameof(ETheme.FollowSystem);
            CurrentTheme = nameof(ETheme.FollowSystem);
            configChanged = true;
        }

        ModifyTheme();
        ModifyFontSize();

        // This constructor runs on the WPF dispatcher. Blocking on the async
        // file write here deadlocks when its continuation resumes on the dispatcher.
        if (configChanged)
        {
            _ = ConfigHandler.SaveConfig(_config);
        }
    }

    private void BindingUI()
    {
        _swatches.AddRange(new SwatchesProvider().Swatches);
        CurrentTheme = _config.UiItem.CurrentTheme;
        CurrentFontSize = _config.UiItem.CurrentFontSize;
        CurrentLanguage = _config.UiItem.CurrentLanguage;

        this.WhenAnyValue(x => x.CurrentTheme, y => y != null && !y.IsNullOrEmpty())
            .Subscribe(async _ =>
            {
                if (_config.UiItem.CurrentTheme != CurrentTheme)
                {
                    _config.UiItem.CurrentTheme = CurrentTheme;
                    ModifyTheme();
                    await ConfigHandler.SaveConfig(_config);
                }
            });

        this.WhenAnyValue(x => x.CurrentFontSize, y => y > 0)
            .Subscribe(async _ =>
            {
                if (_config.UiItem.CurrentFontSize != CurrentFontSize)
                {
                    _config.UiItem.CurrentFontSize = CurrentFontSize;
                    ModifyFontSize();
                    await ConfigHandler.SaveConfig(_config);
                }
            });

        this.WhenAnyValue(x => x.CurrentLanguage, y => y != null && !y.IsNullOrEmpty())
            .Subscribe(async _ =>
            {
                if (CurrentLanguage.IsNotEmpty() && _config.UiItem.CurrentLanguage != CurrentLanguage)
                {
                    _config.UiItem.CurrentLanguage = CurrentLanguage;
                    Thread.CurrentThread.CurrentUICulture = new(CurrentLanguage);
                    await ConfigHandler.SaveConfig(_config);
                    NoticeManager.Instance.Enqueue(ResUI.NeedRebootTips);
                }
            });
    }

    public void ModifyTheme()
    {
        var isDark = ResolveIsDark();
        var theme = _paletteHelper.GetTheme();
        theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);

        var select = isDark ? NightSelect : DaySelect;
        theme.PrimaryLight = new ColorPair(select.Lighten());
        theme.PrimaryMid = new ColorPair(select);
        theme.PrimaryDark = new ColorPair(select.Darken());

        var signal = isDark ? NightSignal : DaySignal;
        theme.SecondaryLight = new ColorPair(signal.Lighten());
        theme.SecondaryMid = new ColorPair(signal);
        theme.SecondaryDark = new ColorPair(signal.Darken());
        _paletteHelper.SetTheme(theme);
        ApplyDesignTokens(isDark);
        WindowsUtils.SetDarkBorder(Application.Current?.MainWindow, isDark ? nameof(ETheme.Dark) : nameof(ETheme.Light));
    }

    private bool ResolveIsDark()
    {
        var mode = CurrentTheme.IsNullOrEmpty() ? _config.UiItem.CurrentTheme : CurrentTheme;
        return mode switch
        {
            nameof(ETheme.Dark) => true,
            nameof(ETheme.Light) => false,
            _ => IsSystemDark()
        };
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int i)
            {
                return i == 0;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static void ApplyDesignTokens(bool isDark)
    {
        var app = Application.Current;
        if (app?.Resources is null)
        {
            return;
        }

        void SetBrush(string key, Color color) => app.Resources[key] = new SolidColorBrush(color);
        void SetColor(string key, Color color) => app.Resources[key] = color;

        if (isDark)
        {
            SetColor("DesignInkColor", NightInk);
            SetColor("DesignInk2Color", NightInk2);
            SetColor("DesignInk3Color", NightInk3);
            SetColor("DesignSurfaceColor", NightCanvas);
            SetColor("DesignCardColor", NightCard);
            SetColor("DesignSelectColor", NightSelect);
            SetColor("DesignSelectSoftColor", NightSelectSoft);
            SetColor("DesignSignalColor", NightSignal);
            SetColor("DesignSignalSoftColor", NightSignalSoft);
            SetColor("DesignWarnColor", NightWarn);
            SetColor("DesignDangerColor", NightDanger);
            SetColor("DesignChipWellColor", NightChip);
            SetColor("DesignOrbOffColor", NightOrbOff);

            SetBrush("DesignInkBrush", NightInk);
            SetBrush("DesignInk2Brush", NightInk2);
            SetBrush("DesignInk3Brush", NightInk3);
            SetBrush("DesignSurfaceBrush", NightCanvas);
            SetBrush("DesignCardBrush", NightCard);
            SetBrush("DesignMenuBarBrush", Color.FromArgb(0xEB, NightCard.R, NightCard.G, NightCard.B));
            SetBrush("DesignLineBrush", NightLine);
            SetBrush("DesignLineStrongBrush", NightLineStrong);
            SetBrush("DesignSelectBrush", NightSelect);
            SetBrush("DesignSelectSoftBrush", NightSelectSoft);
            SetBrush("DesignSignalBrush", NightSignal);
            SetBrush("DesignSignalSoftBrush", NightSignalSoft);
            SetBrush("DesignWarnBrush", NightWarn);
            SetBrush("DesignDangerBrush", NightDanger);
            SetBrush("DesignChipWellBrush", NightChip);
            SetBrush("DesignOrbOffBrush", NightOrbOff);
            SetBrush("DesignWarnSoftBrush", NightWarnSoft);
            SetBrush("DesignDangerSoftBrush", NightDangerSoft);
            SetColor("DesignWarnSoftColor", NightWarnSoft);
            SetColor("DesignDangerSoftColor", NightDangerSoft);
            SetColor("DesignShadowColor", Color.FromRgb(0x00, 0x00, 0x00));
            SetBrush("DesignShadowBrush", Color.FromRgb(0x00, 0x00, 0x00));
            SetBrush("AppCanvasBrush", NightCanvas);

            SetBrush("MaterialDesign.Brush.Primary", NightSelect);
            SetBrush("MaterialDesign.Brush.Primary.Light", NightSelectSoft);
            SetBrush("MaterialDesign.Brush.Primary.Dark", NightSelect.Darken());
            SetBrush("MaterialDesign.Brush.Primary.Foreground", Colors.White);
            SetBrush("MaterialDesign.Brush.Primary.Light.Foreground", NightInk);
            SetBrush("MaterialDesign.Brush.Primary.Dark.Foreground", Colors.White);
            SetBrush("MaterialDesign.Brush.Secondary", NightSignal);
            SetBrush("MaterialDesign.Brush.Background", NightCanvas);
            SetBrush("MaterialDesign.Brush.Card.Background", NightCard);
            SetBrush("MaterialDesign.Brush.Chip.Background", NightChip);
            SetBrush("MaterialDesign.Brush.Foreground", NightInk);
            SetBrush("MaterialDesign.Brush.Foreground.Light", NightInk3);
            SetBrush("MaterialDesign.Brush.TextBox.OutlineBorder", NightLineStrong);
            SetBrush("MaterialDesign.Brush.ToggleButton.Switch.TrackOffBackground", NightChip);
            SetBrush("PrimaryHueMidBrush", NightSelect);
            SetBrush("PrimaryHueLightBrush", NightSelect.Lighten());
            SetBrush("PrimaryHueDarkBrush", NightSelect.Darken());
            SetBrush("PrimaryHueMidForegroundBrush", Colors.White);
            SetBrush("PrimaryHueLightForegroundBrush", NightInk);
            SetBrush("PrimaryHueDarkForegroundBrush", Colors.White);
            SetBrush("SecondaryHueMidBrush", NightSignal);
            SetBrush("SecondaryHueMidForegroundBrush", Colors.White);
            SetBrush("MaterialDesignPaper", NightCard);
            SetBrush("MaterialDesignBody", NightInk);
            SetBrush("MaterialDesignBodyLight", NightInk3);
            SetBrush("MaterialDesignDivider", NightLine);
            SetBrush("MaterialDesignToolBarBackground", NightCard);
        }
        else
        {
            SetColor("DesignInkColor", DayInk);
            SetColor("DesignInk2Color", DayInk2);
            SetColor("DesignInk3Color", DayInk3);
            SetColor("DesignSurfaceColor", DayCanvas);
            SetColor("DesignCardColor", DayCard);
            SetColor("DesignSelectColor", DaySelect);
            SetColor("DesignSelectSoftColor", DaySelectSoft);
            SetColor("DesignSignalColor", DaySignal);
            SetColor("DesignSignalSoftColor", DaySignalSoft);
            SetColor("DesignWarnColor", DayWarn);
            SetColor("DesignDangerColor", DayDanger);
            SetColor("DesignChipWellColor", DayChip);
            SetColor("DesignOrbOffColor", DayOrbOff);

            SetBrush("DesignInkBrush", DayInk);
            SetBrush("DesignInk2Brush", DayInk2);
            SetBrush("DesignInk3Brush", DayInk3);
            SetBrush("DesignSurfaceBrush", DayCanvas);
            SetBrush("DesignCardBrush", DayCard);
            SetBrush("DesignMenuBarBrush", Color.FromArgb(0xB8, DayCard.R, DayCard.G, DayCard.B));
            SetBrush("DesignLineBrush", DayLine);
            SetBrush("DesignLineStrongBrush", DayLineStrong);
            SetBrush("DesignSelectBrush", DaySelect);
            SetBrush("DesignSelectSoftBrush", DaySelectSoft);
            SetBrush("DesignSignalBrush", DaySignal);
            SetBrush("DesignSignalSoftBrush", DaySignalSoft);
            SetBrush("DesignWarnBrush", DayWarn);
            SetBrush("DesignDangerBrush", DayDanger);
            SetBrush("DesignChipWellBrush", DayChip);
            SetBrush("DesignOrbOffBrush", DayOrbOff);
            SetBrush("DesignWarnSoftBrush", DayWarnSoft);
            SetBrush("DesignDangerSoftBrush", DayDangerSoft);
            SetColor("DesignWarnSoftColor", DayWarnSoft);
            SetColor("DesignDangerSoftColor", DayDangerSoft);
            SetColor("DesignShadowColor", Color.FromRgb(0x0C, 0x10, 0x1C));
            SetBrush("DesignShadowBrush", Color.FromRgb(0x0C, 0x10, 0x1C));
            SetBrush("AppCanvasBrush", DayCanvas);

            SetBrush("MaterialDesign.Brush.Primary", DaySelect);
            SetBrush("MaterialDesign.Brush.Primary.Light", DaySelectSoft);
            SetBrush("MaterialDesign.Brush.Primary.Dark", DaySelect.Darken());
            SetBrush("MaterialDesign.Brush.Primary.Foreground", Colors.White);
            SetBrush("MaterialDesign.Brush.Primary.Light.Foreground", DayInk);
            SetBrush("MaterialDesign.Brush.Primary.Dark.Foreground", Colors.White);
            SetBrush("MaterialDesign.Brush.Secondary", DaySignal);
            SetBrush("MaterialDesign.Brush.Background", DayCanvas);
            SetBrush("MaterialDesign.Brush.Card.Background", DayCard);
            SetBrush("MaterialDesign.Brush.Chip.Background", DayChip);
            SetBrush("MaterialDesign.Brush.Foreground", DayInk);
            SetBrush("MaterialDesign.Brush.Foreground.Light", DayInk3);
            SetBrush("MaterialDesign.Brush.TextBox.OutlineBorder", DayLineStrong);
            SetBrush("MaterialDesign.Brush.ToggleButton.Switch.TrackOffBackground", DayChip);
            SetBrush("PrimaryHueMidBrush", DaySelect);
            SetBrush("PrimaryHueLightBrush", DaySelect.Lighten());
            SetBrush("PrimaryHueDarkBrush", DaySelect.Darken());
            SetBrush("PrimaryHueMidForegroundBrush", Colors.White);
            SetBrush("PrimaryHueLightForegroundBrush", DayInk);
            SetBrush("PrimaryHueDarkForegroundBrush", Colors.White);
            SetBrush("SecondaryHueMidBrush", DaySignal);
            SetBrush("SecondaryHueMidForegroundBrush", Colors.White);
            SetBrush("MaterialDesignPaper", DayCard);
            SetBrush("MaterialDesignBody", DayInk);
            SetBrush("MaterialDesignBodyLight", DayInk3);
            SetBrush("MaterialDesignDivider", DayLine);
            SetBrush("MaterialDesignToolBarBackground", DayCard);
        }
    }

    private void ModifyFontSize()
    {
        double size = CurrentFontSize;
        if (size < Global.MinFontSize)
        {
            return;
        }

        Application.Current.Resources["StdFontSize"] = size;
        Application.Current.Resources["StdFontSize1"] = size + 1;
        Application.Current.Resources["StdFontSize-1"] = size - 1;
    }

    public void ChangePrimaryColor(Color color)
    {
        // Locked palette: ignore free Material swatches.
        ModifyTheme();
    }

    public void RegisterSystemColorSet(Config config, Action updateFunc)
    {
        _systemColorHandler = (_, e) =>
        {
            if ((e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.General)
                && config.UiItem.CurrentTheme == nameof(ETheme.FollowSystem))
            {
                Application.Current?.Dispatcher.BeginInvoke(updateFunc);
            }
        };
        SystemEvents.UserPreferenceChanged += _systemColorHandler;
    }

    public void UnregisterSystemColorSet()
    {
        if (_systemColorHandler != null)
        {
            SystemEvents.UserPreferenceChanged -= _systemColorHandler;
            _systemColorHandler = null;
        }
    }
}
