namespace ServiceLib.Services;

public class ProcessService : IDisposable
{
    private readonly Process _process;
    private readonly Func<bool, string, Task>? _updateFunc;
    private DataReceivedEventHandler? _outputHandler;
    private DataReceivedEventHandler? _errorHandler;
    private bool _isDisposed;

    public int Id => _process.Id;
    public IntPtr Handle => _process.Handle;
    public bool HasExited => _process.HasExited;

    public ProcessService(
        string fileName,
        string arguments,
        string workingDirectory,
        bool displayLog,
        bool redirectInput,
        Dictionary<string, string>? environmentVars,
        Func<bool, string, Task>? updateFunc)
    {
        _updateFunc = updateFunc;

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = redirectInput,
                RedirectStandardOutput = displayLog,
                RedirectStandardError = displayLog,
                CreateNoWindow = true,
                StandardOutputEncoding = displayLog ? Encoding.UTF8 : null,
                StandardErrorEncoding = displayLog ? Encoding.UTF8 : null,
            },
            EnableRaisingEvents = true
        };

        if (environmentVars != null)
        {
            foreach (var kv in environmentVars)
            {
                _process.StartInfo.Environment[kv.Key] = kv.Value;
            }
        }

        if (displayLog)
        {
            RegisterEventHandlers();
        }
    }

    public async Task StartAsync(string pwd = null)
    {
        _process.Start();

        if (_process.StartInfo.RedirectStandardOutput)
        {
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        if (_process.StartInfo.RedirectStandardInput)
        {
            await Task.Delay(10);
            await _process.StandardInput.WriteLineAsync(pwd);
        }
    }

    public async Task StopAsync()
    {
        if (_process.HasExited)
        {
            return;
        }

        try
        {
            if (_process.StartInfo.RedirectStandardOutput)
            {
                try { _process.CancelOutputRead(); }
                catch (Exception ex) { Logging.SaveLog($"CancelOutputRead: {ex.Message}"); }
                try { _process.CancelErrorRead(); }
                catch (Exception ex) { Logging.SaveLog($"CancelErrorRead: {ex.Message}"); }
            }

            // Try graceful kill with tree termination on non-Windows
            try
            {
                if (Utils.IsNonWindows())
                {
                    _process.Kill(true);
                }
                else
                {
                    _process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited — fine
                return;
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"Kill process failed: {ex.Message}");
                await _updateFunc?.Invoke(true, $"Kill process failed: {ex.Message}");
            }

            // Wait up to 3s for process to exit after Kill
            for (var i = 0; i < 30 && !_process.HasExited; i++)
            {
                await Task.Delay(100);
            }

            if (!_process.HasExited)
            {
                await _updateFunc?.Invoke(true, "Process did not exit after Kill, force proceeding");
            }
        }
        catch (Exception ex)
        {
            await _updateFunc?.Invoke(true, ex.Message);
        }
    }

    private void RegisterEventHandlers()
    {
        _outputHandler = (sender, e) =>
        {
            if (e.Data.IsNotEmpty())
            {
                _ = _updateFunc?.Invoke(false, e.Data + Environment.NewLine);
            }
        };
        _errorHandler = (sender, e) =>
        {
            if (e.Data.IsNotEmpty())
            {
                _ = _updateFunc?.Invoke(false, e.Data + Environment.NewLine);
            }
        };

        _process.OutputDataReceived += _outputHandler;
        _process.ErrorDataReceived += _errorHandler;
    }

    private void UnregisterEventHandlers()
    {
        try
        {
            if (_outputHandler != null)
            {
                _process.OutputDataReceived -= _outputHandler;
                _outputHandler = null;
            }
            if (_errorHandler != null)
            {
                _process.ErrorDataReceived -= _errorHandler;
                _errorHandler = null;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        UnregisterEventHandlers();

        try
        {
            if (!_process.HasExited)
            {
                try { _process.CancelOutputRead(); }
                catch { /* Already shutting down, ignore */ }
                try { _process.CancelErrorRead(); }
                catch { /* Already shutting down, ignore */ }

                _process.Kill();
            }

            _process.Dispose();
        }
        catch (Exception ex)
        {
            _updateFunc?.Invoke(true, ex.Message);
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
