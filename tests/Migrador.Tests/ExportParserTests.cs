using Migrador.Core;
using Migrador.Core.Model;
using Migrador.Parsers.Export;

namespace Migrador.Tests;

public class ExportParserTests
{
    private static async Task<Archive> ParseSynthetic(TempDir temp) =>
        await ExportParser.ParseAsync(Fixtures.ExportZip("export-synthetic", temp));

    [Fact]
    public async Task Reads_conversations_projects_and_account()
    {
        using var temp = new TempDir();
        var a = await ParseSynthetic(temp);

        Assert.Equal(2, a.Conversations.Count);
        Assert.Single(a.Projects);
        Assert.Equal("Persona de Prueba", a.Account?.Name);
        Assert.Equal("prueba@example.com", a.Account?.Email);
        Assert.NotNull(a.Report?.ExportSha256);
    }

    [Fact]
    public async Task Without_parents_the_tree_is_inferred_and_marked_as_such()
    {
        using var temp = new TempDir();
        var c = (await ParseSynthetic(temp)).Conversations.Single(c => c.Title == "Receta de tortilla");

        Assert.Equal(TreeSource.InferredLinear, c.TreeSource);
        Assert.Null(c.Messages[0].ParentId);
        Assert.Equal(c.Messages[0].Id, c.Messages[1].ParentId);
    }

    [Fact]
    public async Task With_parents_branches_are_kept_and_the_active_branch_is_the_current_leaf()
    {
        using var temp = new TempDir();
        var c = (await ParseSynthetic(temp)).Conversations.Single(c => c.Title == "Plan de viaje");
        var tree = new ConversationTree(c);

        Assert.Equal(TreeSource.Explicit, c.TreeSource);
        Assert.Equal(6, c.Messages.Count);
        Assert.True(tree.HasBranches);
        Assert.Empty(tree.Orphans);
        Assert.Equal(["Quiero ir a Valencia un fin de semana.", "Buena idea. ¿En qué fechas?", "En junio, mejor.", "En junio ya hay fiestas en la playa."],
            tree.ActivePath().Select(m => m.Text));

        var edited = tree["m0000000-0000-4000-8000-000000000016"]!;
        Assert.Equal(["En mayo.", "En junio, mejor."], tree.Siblings(edited).Select(m => m.Text));
        Assert.Equal("Mayo es buena época: hace calor sin agobiar.", tree.PathThrough(tree.Siblings(edited)[0])[^1].Text);
    }

    [Fact]
    public async Task Attachments_and_files_get_honest_fidelity()
    {
        using var temp = new TempDir();
        var c = (await ParseSynthetic(temp)).Conversations.Single(c => c.Title == "Receta de tortilla");
        var first = c.Messages[0];

        Assert.Equal(Fidelity.ExtractedText, first.Attachments.Single(a => a.FileName == "receta.pdf").Fidelity);
        Assert.Equal(Fidelity.Equivalent, first.Attachments.Single(a => a.FileName == "notas.txt").Fidelity);
        Assert.Equal(Fidelity.Lost, first.Files.Single().Fidelity);

        var summary = c.Fidelity();
        Assert.Equal(Fidelity.Lost, summary.Worst);
        Assert.Equal(3, summary.Total);
    }

    [Fact]
    public async Task Unknown_fields_and_entries_are_kept_not_dropped()
    {
        using var temp = new TempDir();
        var a = await ParseSynthetic(temp);
        var c = a.Conversations.Single(c => c.Title == "Receta de tortilla");

        Assert.True(c.Extra!.ContainsKey("is_starred"));
        Assert.True(c.Extra.ContainsKey("account"));
        Assert.True(c.Messages[0].Content[0].Raw.TryGetProperty("citations", out _));
        Assert.True(a.Projects[0].Extra!.ContainsKey("is_private"));
        Assert.Contains("unknown-feature.json", a.Report!.UnknownEntries);
    }

    [Fact]
    public async Task Thinking_blocks_keep_their_text()
    {
        using var temp = new TempDir();
        var c = (await ParseSynthetic(temp)).Conversations.Single(c => c.Title == "Plan de viaje");
        var block = c.Messages.Single(m => m.Id.EndsWith("12")).Content[0];

        Assert.Equal("thinking", block.Type);
        Assert.Equal("Preguntar fechas.", block.Text);
    }
}
