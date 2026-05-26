using System.Collections.Generic;

namespace NWO.Core;

// Manages the ordered list of pending end-of-turn items the player must address.
// Items are pruned lazily — the head is re-checked whenever the queue is asked
// for the current item, so external state changes (a unit being fortified,
// a city receiving production) automatically remove items from the queue.
public class EndTurnQueue
{
    private readonly List<IEndTurnItem> _items = new();

    public int Count => _items.Count;

    public void Clear() => _items.Clear();

    public void Add(IEndTurnItem item) => _items.Add(item);

    public void AddRange(IEnumerable<IEndTurnItem> items) => _items.AddRange(items);

    // Drops invalid items from the head and returns the next valid one, or null.
    public IEndTurnItem? PeekValid()
    {
        while (_items.Count > 0 && !_items[0].NeedsAttention)
            _items.RemoveAt(0);
        return _items.Count > 0 ? _items[0] : null;
    }

    // Discard the current head (e.g. player pressed Space to skip).
    public void Pop()
    {
        if (_items.Count > 0)
            _items.RemoveAt(0);
    }
}
