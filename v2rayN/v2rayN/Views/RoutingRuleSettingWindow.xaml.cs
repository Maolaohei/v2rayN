using System.Windows.Media;

namespace v2rayN.Views;

public partial class RoutingRuleSettingWindow
{
    public RoutingRuleSettingWindow(RoutingItem routingItem)
    {
        InitializeComponent();

        Owner = Application.Current.MainWindow;
        Loaded += Window_Loaded;
        PreviewKeyDown += RoutingRuleSettingWindow_PreviewKeyDown;
        lstRules.SelectionChanged += lstRules_SelectionChanged;
        lstRules.MouseDoubleClick += LstRules_MouseDoubleClick;
        menuRuleSelectAll.Click += menuRuleSelectAll_Click;
        btnBrowseCustomIcon.Click += btnBrowseCustomIcon_Click;
        btnBrowseCustomRulesetPath4Singbox.Click += btnBrowseCustomRulesetPath4Singbox_Click;
        btnTestRule.Click += BtnTestRule_Click;
        btnCopyTestResult.Click += BtnCopyTestResult_Click;
        txtTestInput.TextChanged += TxtTestInput_TextChanged;

        ViewModel = new RoutingRuleSettingViewModel(routingItem, UpdateViewHandler);

        cmbdomainStrategy.ItemsSource = Global.DomainStrategies.AppendEmpty();
        cmbdomainStrategy4Singbox.ItemsSource = Global.DomainStrategies4Sbox;

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.RulesItems, v => v.lstRules.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSource, v => v.lstRules.SelectedItem).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SelectedRouting.Remarks, v => v.txtRemarks.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting.DomainStrategy, v => v.cmbdomainStrategy.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting.DomainStrategy4Singbox, v => v.cmbdomainStrategy4Singbox.Text).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SelectedRouting.Url, v => v.txtUrl.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting.CustomIcon, v => v.txtCustomIcon.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting.CustomRulesetPath4Singbox, v => v.txtCustomRulesetPath4Singbox.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting.Sort, v => v.txtSort.Text).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.RuleAddCmd, v => v.menuRuleAdd).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ImportRulesFromFileCmd, v => v.menuImportRulesFromFile).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ImportRulesFromClipboardCmd, v => v.menuImportRulesFromClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ImportRulesFromUrlCmd, v => v.menuImportRulesFromUrl).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.RuleAddCmd, v => v.menuRuleAdd2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RuleRemoveCmd, v => v.menuRuleRemove).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RuleExportSelectedCmd, v => v.menuRuleExportSelected).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveTopCmd, v => v.menuMoveTop).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveUpCmd, v => v.menuMoveUp).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveDownCmd, v => v.menuMoveDown).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveBottomCmd, v => v.menuMoveBottom).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.CloseWindow:
                DialogResult = true;
                break;

            case EViewAction.ShowYesNo:

                if (UI.ShowYesNo(ResUI.RemoveServer) == MessageBoxResult.No)
                {
                    return false;
                }
                break;

            case EViewAction.AddBatchRoutingRulesYesNo:

                if (UI.ShowYesNo(ResUI.AddBatchRoutingRulesYesNo) == MessageBoxResult.No)
                {
                    return false;
                }
                break;

            case EViewAction.RoutingRuleDetailsWindow:

                if (obj is null)
                {
                    return false;
                }

                return new RoutingRuleDetailsWindow((RulesItem)obj).ShowDialog() ?? false;

            case EViewAction.ImportRulesFromFile:

                if (UI.OpenFileDialog(out var fileName, "Rules|*.json|All|*.*") != true)
                {
                    return false;
                }
                ViewModel?.ImportRulesFromFileAsync(fileName);
                break;

            case EViewAction.SetClipboardData:
                if (obj is null)
                {
                    return false;
                }

                WindowsUtils.SetClipboardData((string)obj);
                break;

            case EViewAction.ImportRulesFromClipboard:
                var clipboardData = WindowsUtils.GetClipboardData();
                if (clipboardData.IsNotEmpty())
                {
                    ViewModel?.ImportRulesFromClipboardAsync(clipboardData);
                }
                break;
        }

        return await Task.FromResult(true);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        txtRemarks.Focus();
    }

    private void RoutingRuleSettingWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!lstRules.IsKeyboardFocusWithin)
        {
            return;
        }

        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            if (e.Key == Key.A)
            {
                lstRules.SelectAll();
            }
            else if (e.Key == Key.C)
            {
                ViewModel?.RuleExportSelectedAsync();
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.T:
                    ViewModel?.MoveRule(EMove.Top);
                    break;

                case Key.U:
                    ViewModel?.MoveRule(EMove.Up);
                    break;

                case Key.D:
                    ViewModel?.MoveRule(EMove.Down);
                    break;

                case Key.B:
                    ViewModel?.MoveRule(EMove.Bottom);
                    break;

                case Key.Delete:
                case Key.Back:
                    ViewModel?.RuleRemoveAsync();
                    break;
            }
        }
    }

    private void lstRules_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedSources = lstRules.SelectedItems.Cast<RulesItemModel>().ToList();
        }
    }

    private void LstRules_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.RuleEditAsync(false);
    }

    private void menuRuleSelectAll_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        lstRules.SelectAll();
    }

    private void btnBrowseCustomIcon_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (UI.OpenFileDialog(out var fileName,
            "PNG,ICO|*.png;*.ico") != true)
        {
            return;
        }

        txtCustomIcon.Text = fileName;
    }

    private void btnBrowseCustomRulesetPath4Singbox_Click(object sender, RoutedEventArgs e)
    {
        if (UI.OpenFileDialog(out var fileName,
              "Config|*.json|All|*.*") != true)
        {
            return;
        }

        txtCustomRulesetPath4Singbox.Text = fileName;
    }

    private void linkCustomRulesetPath4Singbox(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart("https://github.com/2dust/v2rayCustomRoutingList/blob/master/singbox_custom_ruleset_example.json");
    }

    #region Rule Test

    private void TxtTestInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        txtTestInputWatermark.Visibility = string.IsNullOrEmpty(txtTestInput.Text)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        lstTestResults.ItemsSource = null;
        txtTestSummary.Text = "";
    }

    private async void BtnTestRule_Click(object sender, RoutedEventArgs e)
    {
        var input = txtTestInput.Text?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        if (ViewModel?.CurrentRules == null || ViewModel.CurrentRules.Count == 0)
        {
            txtTestSummary.Text = "没有规则可测试";
            txtTestSummary.Foreground = Brushes.Gray;
            return;
        }

        var rules = ViewModel.CurrentRules;

        var results = RuleTestMatcher.TestAllRules(input, rules);
        var firstMatch = results.FirstOrDefault(r => r.IsFirstMatch);
        var isIp = System.Net.IPAddress.TryParse(input, out _);

        // If no match and input looks like a domain, resolve IP and test again
        if (firstMatch == null && !isIp && !input.Contains('/') && !input.Contains(':'))
        {
            txtTestSummary.Text = "域名未命中，正在解析 IP...";
            txtTestSummary.Foreground = Brushes.Gray;

            try
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync(input);
                if (addresses.Length > 0)
                {
                    var ip = addresses[0].ToString();
                    var ipResults = RuleTestMatcher.TestAllRules(ip, rules);
                    var ipFirstMatch = ipResults.FirstOrDefault(r => r.IsFirstMatch);

                    ShowTestResults(input, results, firstMatch, $" (DNS → {ip})", ipResults, ipFirstMatch);
                    return;
                }
            }
            catch { }
        }

        ShowTestResults(input, results, firstMatch, "", null, null);
    }

    private void BtnCopyTestResult_Click(object sender, RoutedEventArgs e)
    {
        var items = lstTestResults.ItemsSource as System.Collections.IList;
        if (items == null || items.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        foreach (RuleTestDisplayItem item in items)
        {
            sb.AppendLine($"{item.LineNum} {item.RuleName} | {item.RuleFields} | {item.Outbound} | {item.StatusIcon}");
        }
        System.Windows.Clipboard.SetText(sb.ToString());
        NoticeManager.Instance.Enqueue("已复制到剪贴板");
    }

    private void ShowTestResults(string input, List<RuleTestResult> results, RuleTestResult? firstMatch,
        string suffix, List<RuleTestResult>? ipResults, RuleTestResult? ipFirstMatch)
    {
        var displayResults = new List<RuleTestDisplayItem>();
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var ruleName = r.Rule.Remarks?.IsNotEmpty() == true ? r.Rule.Remarks : $"规则{i + 1}";
            var outbound = FormatOutbound(r.Rule.OutboundTag);
            var ruleFields = FormatRuleFields(r.Rule);

            displayResults.Add(new RuleTestDisplayItem
            {
                LineNum = $"P{i + 1}",
                RuleName = ruleName,
                RuleFields = ruleFields,
                Outbound = outbound,
                Matched = r.Matched,
                MatchField = r.MatchField,
                IsFirstMatch = r.IsFirstMatch,
                RowBg = r.IsFirstMatch
                    ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9))
                    : r.Matched
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0))
                        : Brushes.Transparent,
                StatusIcon = r.IsFirstMatch ? "✓ 命中" : r.Matched ? "✓ 命中" : "",
                StatusColor = r.Matched ? Brushes.Green : Brushes.Gray
            });
        }

        // Add IP results if available
        if (ipResults != null)
        {
            displayResults.Add(new RuleTestDisplayItem
            {
                LineNum = "",
                RuleName = "── DNS 解析 IP 测试 ──",
                RuleFields = "",
                Outbound = "",
                Matched = false,
                RowBg = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                StatusIcon = "",
                StatusColor = Brushes.Gray
            });

            for (var i = 0; i < ipResults.Count; i++)
            {
                var r = ipResults[i];
                var ruleName = r.Rule.Remarks?.IsNotEmpty() == true ? r.Rule.Remarks : $"规则{i + 1}";
                var outbound = FormatOutbound(r.Rule.OutboundTag);
                var ruleFields = FormatRuleFields(r.Rule);

                displayResults.Add(new RuleTestDisplayItem
                {
                    LineNum = $"P{i + 1}",
                    RuleName = ruleName,
                    RuleFields = ruleFields,
                    Outbound = outbound,
                    Matched = r.Matched,
                    MatchField = r.MatchField,
                    IsFirstMatch = r.IsFirstMatch,
                    RowBg = r.IsFirstMatch
                        ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9))
                        : r.Matched
                            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0))
                            : Brushes.Transparent,
                    StatusIcon = r.IsFirstMatch ? "✓ 命中" : r.Matched ? "✓ 命中" : "",
                    StatusColor = r.Matched ? Brushes.Green : Brushes.Gray
                });
            }
        }

        lstTestResults.ItemsSource = displayResults;

        // Determine final result - check both domain and IP matches
        var bestMatch = firstMatch ?? ipFirstMatch;
        if (bestMatch != null)
        {
            var ruleName = bestMatch.Rule.Remarks?.IsNotEmpty() == true ? bestMatch.Rule.Remarks : "未命名";
            var idx = (firstMatch != null ? results.IndexOf(firstMatch) : -1);
            var location = firstMatch != null
                ? $"P{idx + 1} {ruleName} 首次匹配"
                : $"DNS IP 首次匹配";
            txtTestSummary.Text = $"最终结果: {FormatOutbound(bestMatch.Rule.OutboundTag)} ({location}){suffix}";
            txtTestSummary.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        }
        else
        {
            txtTestSummary.Text = $"✗ 无规则匹配，走兜底出站{suffix}";
            txtTestSummary.Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        }
    }

    private static string FormatOutbound(string? outboundTag)
    {
        return outboundTag?.ToLowerInvariant() switch
        {
            "proxy" => "代理",
            "direct" => "直连",
            "block" => "拦截",
            _ => outboundTag ?? "未知"
        };
    }

    private static string FormatRuleFields(RulesItem rule)
    {
        var parts = new List<string>();
        if (rule.Domain?.Count > 0) parts.Add($"domain:{FormatFieldValues(rule.Domain)}");
        if (rule.Ip?.Count > 0) parts.Add($"ip:{FormatFieldValues(rule.Ip)}");
        if (rule.Port.IsNotEmpty()) parts.Add($"port:{rule.Port}");
        if (rule.Protocol?.Count > 0) parts.Add($"proto:{string.Join(",", rule.Protocol)}");
        if (rule.Process?.Count > 0) parts.Add($"proc:{FormatFieldValues(rule.Process)}");
        return parts.Count > 0 ? string.Join(" ", parts) : "(空规则)";
    }

    private static string FormatFieldValues(List<string> values)
    {
        if (values.Count <= 3)
            return string.Join(", ", values);
        return $"{values[0]}, {values[1]}, +{values.Count - 2}项";
    }

    #endregion
}

public class RuleTestDisplayItem
{
    public string LineNum { get; set; } = "";
    public string RuleName { get; set; } = "";
    public string RuleFields { get; set; } = "";
    public string Outbound { get; set; } = "";
    public bool Matched { get; set; }
    public string MatchField { get; set; } = "";
    public bool IsFirstMatch { get; set; }
    public Brush RowBg { get; set; } = Brushes.Transparent;
    public string StatusIcon { get; set; } = "";
    public Brush StatusColor { get; set; } = Brushes.Gray;
}
