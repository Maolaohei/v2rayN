namespace ServiceLib.ViewModels;

public class MsgViewModel : MyReactiveObject
{
    private readonly ConcurrentQueue<string> _queueMsg = new();
    private readonly List<string> _allMessages = new();
    private volatile bool _lastMsgFilterNotAvailable;
    private int _showLock = 0;
    public int NumMaxMsg { get; } = 500;

    [Reactive]
    public string MsgFilter { get; set; }

    [Reactive]
    public bool AutoRefresh { get; set; }

    public MsgViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        _updateView = updateView;
        MsgFilter = _config.MsgUIItem.MainMsgFilter ?? string.Empty;
        AutoRefresh = _config.MsgUIItem.AutoRefresh ?? true;

        this.WhenAnyValue(
           x => x.MsgFilter)
               .Subscribe(c => DoMsgFilter());

        this.WhenAnyValue(
          x => x.AutoRefresh,
          y => y == true)
              .Subscribe(c => _config.MsgUIItem.AutoRefresh = AutoRefresh);

        AppEvents.SendMsgViewRequested
         .AsObservable()
         .Subscribe(content => _ = AppendQueueMsg(content));
    }

    private async Task AppendQueueMsg(string msg)
    {
        if (AutoRefresh == false)
        {
            return;
        }

        EnqueueQueueMsg(msg);

        if (!AppManager.Instance.ShowInTaskbar)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _showLock, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await Task.Delay(500).ConfigureAwait(false);

            var sb = new StringBuilder();
            while (_queueMsg.TryDequeue(out var line))
            {
                sb.Append(line);
            }

            await _updateView?.Invoke(EViewAction.DispatcherShowMsg, sb.ToString());
        }
        finally
        {
            Interlocked.Exchange(ref _showLock, 0);
        }
    }

    private void EnqueueQueueMsg(string msg)
    {
        lock (_allMessages)
        {
            _allMessages.Add(msg);
            while (_allMessages.Count > NumMaxMsg)
            {
                _allMessages.RemoveAt(0);
            }
        }

        if (MsgFilter.IsNotEmpty() && !_lastMsgFilterNotAvailable)
        {
            try
            {
                if (!Regex.IsMatch(msg, MsgFilter))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _queueMsg.Enqueue(ex.Message);
                _lastMsgFilterNotAvailable = true;
            }
        }

        _queueMsg.Enqueue(msg);
        if (!msg.EndsWith(Environment.NewLine))
        {
            _queueMsg.Enqueue(Environment.NewLine);
        }
    }

    public void RefreshFilteredMessages()
    {
        _lastMsgFilterNotAvailable = false;

        var filtered = new StringBuilder();
        lock (_allMessages)
        {
            foreach (var msg in _allMessages)
            {
                if (MsgFilter.IsNotEmpty())
                {
                    try
                    {
                        if (!Regex.IsMatch(msg, MsgFilter))
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
                filtered.Append(msg);
                if (!msg.EndsWith(Environment.NewLine))
                {
                    filtered.Append(Environment.NewLine);
                }
            }
        }

        _ = _updateView?.Invoke(EViewAction.DispatcherShowMsg, filtered.ToString());
    }

    private void DoMsgFilter()
    {
        _config.MsgUIItem.MainMsgFilter = MsgFilter;
        _lastMsgFilterNotAvailable = false;
        RefreshFilteredMessages();
    }
}
