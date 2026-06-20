namespace ServiceLib.Manager;

public class ProfileExManager
{
    private static readonly Lazy<ProfileExManager> _instance = new(() => new());
    private ConcurrentDictionary<string, ProfileExItem> _lstProfileEx = new();
    private readonly ConcurrentQueue<string> _queIndexIds = new();
    private readonly ConcurrentDictionary<string, bool> _setQueIndexIds = new();
    public static ProfileExManager Instance => _instance.Value;
    private static readonly string _tag = "ProfileExHandler";

    public ProfileExManager()
    {
    }

    public async Task Init()
    {
        await InitData();
    }

    public async Task<ICollection<ProfileExItem>> GetProfileExs()
    {
        return await Task.FromResult<ICollection<ProfileExItem>>(_lstProfileEx.Values);
    }

    private async Task InitData()
    {
        await SQLiteHelper.Instance.ExecuteAsync($"delete from ProfileExItem where indexId not in ( select indexId from ProfileItem )");

        var items = await SQLiteHelper.Instance.TableAsync<ProfileExItem>().ToListAsync();
        _lstProfileEx = new ConcurrentDictionary<string, ProfileExItem>(
            items.Select(t => new KeyValuePair<string, ProfileExItem>(t.IndexId, t)));
    }

    private void IndexIdEnqueue(string indexId)
    {
        if (indexId.IsNotEmpty() && _setQueIndexIds.TryAdd(indexId, true))
        {
            _queIndexIds.Enqueue(indexId);
        }
    }

    private async Task SaveQueueIndexIds()
    {
        var lstToSave = new List<ProfileExItem>();
        while (_queIndexIds.TryDequeue(out var id))
        {
            _setQueIndexIds.TryRemove(id, out _);
            if (_lstProfileEx.TryGetValue(id, out var itemNew))
            {
                lstToSave.Add(itemNew);
            }
        }

        if (lstToSave.Count > 0)
        {
            try
            {
                await SQLiteHelper.Instance.InsertOrReplaceAllAsync(lstToSave);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        }
    }

    private ProfileExItem AddProfileEx(string indexId)
    {
        var profileEx = new ProfileExItem()
        {
            IndexId = indexId,
            Delay = 0,
            Speed = 0,
            Sort = 0,
            Message = string.Empty
        };
        _lstProfileEx.TryAdd(indexId, profileEx);
        IndexIdEnqueue(indexId);
        return profileEx;
    }

    private ProfileExItem GetProfileExItem(string? indexId)
    {
        if (indexId != null && _lstProfileEx.TryGetValue(indexId, out var item))
        {
            return item;
        }
        return AddProfileEx(indexId!);
    }

    public async Task ClearAll()
    {
        await SQLiteHelper.Instance.ExecuteAsync($"delete from ProfileExItem ");
        _lstProfileEx.Clear();
    }

    public async Task SaveTo()
    {
        try
        {
            await SaveQueueIndexIds();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public void SetTestDelay(string indexId, int delay)
    {
        var profileEx = GetProfileExItem(indexId);

        profileEx.Delay = delay;
        IndexIdEnqueue(indexId);
    }

    public void SetTestSpeed(string indexId, decimal speed)
    {
        var profileEx = GetProfileExItem(indexId);

        profileEx.Speed = speed;
        IndexIdEnqueue(indexId);
    }

    public void SetTestMessage(string indexId, string message)
    {
        var profileEx = GetProfileExItem(indexId);

        profileEx.Message = message;
        IndexIdEnqueue(indexId);
    }

    public void SetTestIpInfo(string indexId, string ipInfo)
    {
        var profileEx = GetProfileExItem(indexId);

        profileEx.IpInfo = ipInfo;
        IndexIdEnqueue(indexId);
    }

    public void SetSort(string indexId, int sort)
    {
        var profileEx = GetProfileExItem(indexId);

        profileEx.Sort = sort;
        IndexIdEnqueue(indexId);
    }

    public int GetSort(string indexId)
    {
        if (indexId != null && _lstProfileEx.TryGetValue(indexId, out var profileEx))
        {
            return profileEx.Sort;
        }
        return 0;
    }

    public int GetMaxSort()
    {
        if (_lstProfileEx.IsEmpty)
        {
            return 0;
        }
        return _lstProfileEx.Values.Max(t => t.Sort);
    }
}
