using v2rayN.ViewModels;

namespace v2rayN.Views;

/// <summary>
/// ThemeSettingView.xaml — Day / Night / Follow system only.
/// </summary>
public partial class ThemeSettingView
{
    public ThemeSettingView()
    {
        InitializeComponent();
        ViewModel = new ThemeSettingViewModel();

        // Locked dual palette: only FollowSystem / Dark / Light
        cmbCurrentTheme.ItemsSource = new[]
        {
            nameof(ETheme.FollowSystem),
            nameof(ETheme.Light),
            nameof(ETheme.Dark)
        };
        cmbCurrentFontSize.ItemsSource = Enumerable.Range(Global.MinFontSize, Global.MinFontSizeCount).ToList();
        cmbCurrentLanguage.ItemsSource = Global.Languages;

        // Hide free Material swatch picker — palette is locked.
        if (cmbSwatches != null)
        {
            cmbSwatches.Visibility = System.Windows.Visibility.Collapsed;
        }

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.CurrentTheme, v => v.cmbCurrentTheme.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CurrentFontSize, v => v.cmbCurrentFontSize.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CurrentLanguage, v => v.cmbCurrentLanguage.Text).DisposeWith(disposables);
        });
    }
}
