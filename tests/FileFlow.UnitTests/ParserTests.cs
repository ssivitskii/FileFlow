using FileFlow.Cli;
using FileFlow.Cli.Parsing;
using FileFlow.Core.Abstractions;

namespace FileFlow.UnitTests;

public sealed class ParserTests
{
    private static readonly string[] ExpectedQuotedTokens = ["show", "folder/my file.txt"];

    [Fact]
    public void MoveWithTwoPathsParses()
    {
        ICommand command = new CommandParser().Parse("move source.txt archive/destination.txt");

        Assert.NotNull(command);
    }

    [Fact]
    public void QuotedPathWithSpacesIsSingleToken()
    {
        IReadOnlyList<string> tokens = Tokenizer.Tokenize("show \"folder/my file.txt\"");

        Assert.Equal(ExpectedQuotedTokens, tokens);
    }

    [Fact]
    public void UnclosedQuoteIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Tokenizer.Tokenize("show \"unfinished"));
    }

    [Fact]
    public void UnknownFlagIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new CommandParser().Parse("tree --levels 2"));
    }

    [Fact]
    public void SurplusPositionalArgumentIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new CommandParser().Parse("move one two three"));
    }

    [Fact]
    public void FlagWithoutValueIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new CommandParser().Parse("tree --depth"));
    }

    [Fact]
    public void DryRunIsParsedAsAValuelessMutationFlag()
    {
        ICommand command = new CommandParser().Parse("copy source.txt target.txt --dry-run");

        Assert.NotNull(command);
        Assert.Throws<ArgumentException>(() => new CommandParser().Parse("tree --dry-run"));
    }

    [Fact]
    public void HistoryUndoAndDuplicateCommandsValidateArguments()
    {
        var parser = new CommandParser();

        Assert.NotNull(parser.Parse("history"));
        Assert.NotNull(parser.Parse($"undo {Guid.NewGuid()}"));
        Assert.NotNull(parser.Parse("duplicates . --format json"));
        Assert.Throws<ArgumentException>(() => parser.Parse("undo not-a-guid"));
        Assert.Throws<ArgumentException>(() => parser.Parse("duplicates . --format"));
    }

    [Fact]
    public async Task MissingScriptReturnsConfigurationErrorWithoutThrowing()
    {
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();

        int exitCode = await new FileFlowApplication(input, output).RunAsync(
            ["--script", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt")],
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Contains("Error: cannot read script", output.ToString(), StringComparison.Ordinal);
    }
}
