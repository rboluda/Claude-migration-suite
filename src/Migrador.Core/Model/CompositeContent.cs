namespace Migrador.Core.Model;

/// <summary>Content spread over several sources (an export, a .clo, a local ~/.claude): the first one that has it wins.</summary>
public sealed class CompositeContent(params IEnumerable<IArchiveContent> sources) : IArchiveContent
{
    private readonly List<IArchiveContent> _sources = sources.SelectMany(Flatten).ToList();

    public CompositeContent With(IArchiveContent? source) => source is null ? this : new([.. _sources, source]);

    public Stream? OpenOriginalExport() => _sources.Select(s => s.OpenOriginalExport()).FirstOrDefault(s => s is not null);

    public Stream? OpenBlob(Blob blob) => _sources.Select(s => s.OpenBlob(blob)).FirstOrDefault(s => s is not null);

    public Stream? OpenClaudeCodeFile(ClaudeCodeSnapshot snapshot, ClaudeCodeFile file) =>
        _sources.Select(s => s.OpenClaudeCodeFile(snapshot, file)).FirstOrDefault(s => s is not null);

    private static IEnumerable<IArchiveContent> Flatten(IArchiveContent c) => c is CompositeContent cc ? cc._sources : [c];
}
