using System.Collections.ObjectModel;

namespace Naziki_Editor.Shared.Input;

public sealed record EditorSelectionChangedEventArgs(
    IReadOnlyList<object> Items,
    object? Primary,
    string Source);

public interface ISelectionService
{
    IReadOnlyList<object> Items { get; }
    object? Primary { get; }
    event EventHandler<EditorSelectionChangedEventArgs>? SelectionChanged;
    void Set(object? item, string source);
    void SetMany(IEnumerable<object> items, object? primary, string source);
    void Toggle(object item, string source);
    void Clear(string source);
}

public sealed class SelectionService : ISelectionService
{
    private readonly ObservableCollection<object> _items = [];
    public IReadOnlyList<object> Items => _items;
    public object? Primary { get; private set; }
    public event EventHandler<EditorSelectionChangedEventArgs>? SelectionChanged;

    public void Set(object? item, string source)
    {
        _items.Clear();
        if (item is not null) _items.Add(item);
        Primary = item;
        Raise(source);
    }

    public void SetMany(IEnumerable<object> items, object? primary, string source)
    {
        _items.Clear();
        foreach (var item in items.Distinct()) _items.Add(item);
        Primary = primary ?? _items.FirstOrDefault();
        Raise(source);
    }

    public void Toggle(object item, string source)
    {
        if (_items.Contains(item)) _items.Remove(item);
        else _items.Add(item);
        Primary = _items.Contains(item) ? item : _items.LastOrDefault();
        Raise(source);
    }

    public void Clear(string source) => Set(null, source);

    private void Raise(string source) =>
        SelectionChanged?.Invoke(this, new EditorSelectionChangedEventArgs(_items.ToArray(), Primary, source));
}
