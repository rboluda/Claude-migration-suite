namespace Migrador.Core.Model;

/// <summary>Navigation over the message tree of a conversation.</summary>
public sealed class ConversationTree
{
    private readonly Dictionary<string, Message> _byId = new();
    private readonly Dictionary<string, List<Message>> _children = new();
    private readonly List<Message> _roots = [];

    public ConversationTree(Conversation conversation)
    {
        Conversation = conversation;
        foreach (var m in conversation.Messages)
            _byId.TryAdd(m.Id, m);
        foreach (var m in conversation.Messages)
        {
            if (m.ParentId is { } parent && _byId.ContainsKey(parent))
            {
                if (!_children.TryGetValue(parent, out var list))
                    _children[parent] = list = [];
                list.Add(m);
            }
            else
            {
                _roots.Add(m);
            }
        }
        foreach (var list in _children.Values.Append(_roots))
            list.Sort(ByDate);
    }

    public Conversation Conversation { get; }

    public IReadOnlyList<Message> Roots => _roots;

    public IReadOnlyList<Message> ChildrenOf(Message message) =>
        _children.TryGetValue(message.Id, out var list) ? list : [];

    public Message? this[string id] => _byId.GetValueOrDefault(id);

    /// <summary>Messages with more than one answer (edits, regenerations).</summary>
    public int BranchPointCount => _children.Values.Count(c => c.Count > 1) + (_roots.Count > 1 ? 1 : 0);

    public bool HasBranches => BranchPointCount > 0;

    /// <summary>Messages whose parent is set but not present in the conversation.</summary>
    public IEnumerable<Message> Orphans =>
        _roots.Where(m => m.ParentId is not null && !_byId.ContainsKey(m.ParentId));

    /// <summary>
    /// The active branch, from the first message to <see cref="Conversation.CurrentLeafId"/>; if that is missing, to
    /// the most recent leaf.
    /// </summary>
    public IReadOnlyList<Message> ActivePath()
    {
        if (Conversation.CurrentLeafId is { } leafId && _byId.TryGetValue(leafId, out var leaf))
            return PathTo(leaf);
        var last = _roots.Select(LatestLeaf).OrderBy(m => m.CreatedAt).ThenBy(m => Depth(m)).LastOrDefault();
        return last is null ? [] : PathTo(last);
    }

    /// <summary>From the first message down to <paramref name="message"/>.</summary>
    public IReadOnlyList<Message> PathTo(Message message)
    {
        var path = new List<Message>();
        var seen = new HashSet<string>();
        for (Message? m = message; m is not null && seen.Add(m.Id); m = m.ParentId is { } p ? this[p] : null)
            path.Add(m);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// The branch that goes through <paramref name="message"/> and then follows the most recent answer at each step.
    /// </summary>
    public IReadOnlyList<Message> PathThrough(Message message) => PathTo(LatestLeaf(message));

    public Message LatestLeaf(Message message)
    {
        var seen = new HashSet<string>();
        var m = message;
        while (seen.Add(m.Id) && ChildrenOf(m) is { Count: > 0 } children)
            m = children[^1];
        return m;
    }

    /// <summary>The message and its alternatives (siblings with the same parent), in date order.</summary>
    public IReadOnlyList<Message> Siblings(Message message) =>
        message.ParentId is { } p && this[p] is { } parent ? ChildrenOf(parent) : _roots;

    private int Depth(Message m) => PathTo(m).Count;

    private static int ByDate(Message a, Message b) =>
        Nullable.Compare(a.CreatedAt, b.CreatedAt) is var c && c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
}
