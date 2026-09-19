namespace TradingPlatform.Backtesting;

internal sealed class SliceList<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly int _start;
    private readonly int _count;

    public SliceList(IReadOnlyList<T> items, int start, int count)
    {
        _items = items;
        _start = Math.Clamp(start, 0, items.Count);
        _count = Math.Clamp(count, 0, items.Count - _start);
    }

    public T this[int index] => index >= 0 && index < _count
        ? _items[_start + index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    public int Count => _count;

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return _items[_start + i];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
