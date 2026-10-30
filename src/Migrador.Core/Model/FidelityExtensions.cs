namespace Migrador.Core.Model;

public static class FidelityExtensions
{
    /// <summary>Every item of the conversation that may be incomplete (attachments and referenced files).</summary>
    public static IEnumerable<ResolvableItem> Items(this Conversation c) =>
        c.Messages.SelectMany(m => m.Attachments.Cast<ResolvableItem>().Concat(m.Files));

    /// <summary>
    /// Fidelity of the conversation: worst level plus a count per level of its items. The text of the messages comes
    /// straight from the export, so a conversation without attachments or files is <see cref="Fidelity.Original"/>.
    /// </summary>
    public static FidelitySummary Fidelity(this Conversation c) => FidelitySummary.Of(c.Items().Select(i => i.Fidelity));

    public static FidelitySummary Fidelity(this Project p) => FidelitySummary.Of(p.Documents.Select(d => d.Fidelity));

    public static FidelitySummary Fidelity(this Archive a) =>
        FidelitySummary.Of(a.Conversations.SelectMany(Items).Concat(a.Projects.SelectMany(p => p.Documents)).Select(i => i.Fidelity));
}
