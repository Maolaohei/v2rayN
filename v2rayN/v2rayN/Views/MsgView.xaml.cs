using System.Windows.Documents;

namespace v2rayN.Views;

public partial class MsgView
{
    public MsgView()
    {
        InitializeComponent();

        ViewModel = new MsgViewModel(UpdateViewHandler);

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.MsgFilter, v => v.txtMsgFilter.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoRefresh, v => v.togAutoRefresh.IsChecked).DisposeWith(disposables);
        });

        btnCopy.Click += menuMsgViewCopyAll_Click;
        btnClear.Click += menuMsgViewClear_Click;
        menuMsgViewSelectAll.Click += menuMsgViewSelectAll_Click;
        menuMsgViewCopy.Click += menuMsgViewCopy_Click;
        menuMsgViewCopyAll.Click += menuMsgViewCopyAll_Click;
        menuMsgViewClear.Click += menuMsgViewClear_Click;
    }

    private void TxtMsgFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        txtMsgFilterWatermark.Visibility = string.IsNullOrEmpty(txtMsgFilter.Text)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private void TxtMsgFilter_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
        }
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.DispatcherShowMsg:
                if (obj is null)
                {
                    return false;
                }

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ShowMsg(obj);
                }, DispatcherPriority.ApplicationIdle);
                break;
        }
        return await Task.FromResult(true);
    }

    private void ShowMsg(object msg)
    {
        var text = msg.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (txtMsg.Document.Blocks.Count > ViewModel?.NumMaxMsg)
        {
            ClearMsg();
        }

        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var color = GetLineColor(line);
            var run = new Run(line);
            run.SetValue(TextElement.ForegroundProperty, color);
            var paragraph = new Paragraph(run)
            {
                Margin = new Thickness(0),
                LineHeight = 16
            };
            txtMsg.Document.Blocks.Add(paragraph);
        }

        if (togScrollToEnd.IsChecked ?? true)
        {
            txtMsg.ScrollToEnd();
        }
    }

    private static System.Windows.Media.Brush GetLineColor(string line)
    {
        var lower = line.ToLowerInvariant();

        // Error / Failure
        if (lower.Contains("[error]") || lower.Contains("failed") || lower.Contains("exception")
            || lower.Contains("fail") || lower.Contains("✗") || lower.Contains("失败")
            || lower.Contains("错误") || lower.Contains("异常") || lower.Contains("connectivity lost")
            || lower.Contains("not found") || lower.Contains("denied"))
        {
            return System.Windows.Media.Brushes.Red;
        }

        // Warning
        if (lower.Contains("[warning]") || lower.Contains("warn") || lower.Contains("⚠")
            || lower.Contains("警告") || lower.Contains("forcing restart"))
        {
            return System.Windows.Media.Brushes.Orange;
        }

        // Success
        if (lower.Contains("success") || lower.Contains("started") || lower.Contains("connected")
            || lower.Contains("✓") || lower.Contains("通过") || lower.Contains("成功")
            || lower.Contains("完成") || lower.Contains("已停止") || lower.Contains("启动服务")
            || lower.Contains("初始化成功") || lower.Contains("reading config"))
        {
            return System.Windows.Media.Brushes.LimeGreen;
        }

        // Info prefix - keep default
        if (lower.Contains("[info]"))
        {
            return System.Windows.Media.Brushes.DodgerBlue;
        }

        return System.Windows.SystemColors.ControlTextBrush;
    }

    public void ClearMsg()
    {
        txtMsg.Document.Blocks.Clear();
        var paragraph = new Paragraph(new Run("----- Message cleared -----"))
        {
            Margin = new Thickness(0)
        };
        txtMsg.Document.Blocks.Add(paragraph);
    }

    private void menuMsgViewSelectAll_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        txtMsg.Focus();
        txtMsg.SelectAll();
    }

    private void menuMsgViewCopy_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selection = txtMsg.Selection;
        if (selection != null && !selection.IsEmpty)
        {
            var text = selection.Text;
            WindowsUtils.SetClipboardData(text);
        }
    }

    private void menuMsgViewCopyAll_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var text = new TextRange(txtMsg.Document.ContentStart, txtMsg.Document.ContentEnd).Text;
        WindowsUtils.SetClipboardData(text);
    }

    private void menuMsgViewClear_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ClearMsg();
    }
}
