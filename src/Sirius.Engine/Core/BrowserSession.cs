namespace Sirius.Engine.Core;

public sealed class HistoryEntry
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

/// <summary>Linear session history with back/forward support.</summary>
public sealed class BrowserSession
{
    private readonly List<HistoryEntry> _entries = new();
    private int _index = -1;

    public int Index => _index;
    public int Count => _entries.Count;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;

    public HistoryEntry? Current => _index >= 0 && _index < _entries.Count ? _entries[_index] : null;

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public void Push(string url, string title)
    {
        if (_index >= 0 && _index < _entries.Count - 1)
        {
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        }

        if (_index >= 0 && _entries.Count > 0 &&
            string.Equals(_entries[^1].Url, url, StringComparison.OrdinalIgnoreCase))
        {
            _entries[^1].Title = title;
            _index = _entries.Count - 1;
            return;
        }

        _entries.Add(new HistoryEntry { Url = url, Title = title });
        _index = _entries.Count - 1;

        if (_entries.Count > 200)
        {
            _entries.RemoveAt(0);
            _index--;
        }
    }

    public void ReplaceCurrent(string url, string title)
    {
        if (_index < 0)
        {
            Push(url, title);
            return;
        }

        _entries[_index].Url = url;
        _entries[_index].Title = title;
    }

    public string? Back()
    {
        if (!CanGoBack) return null;
        _index--;
        return _entries[_index].Url;
    }

    public string? Forward()
    {
        if (!CanGoForward) return null;
        _index++;
        return _entries[_index].Url;
    }

    public void Clear()
    {
        _entries.Clear();
        _index = -1;
    }
}
