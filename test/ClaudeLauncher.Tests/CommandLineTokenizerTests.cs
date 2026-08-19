using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class CommandLineTokenizerTests
{
    [Fact]
    public void Tokenize_EmptyInput_ReturnsNoTokens()
    {
        Assert.Empty(CommandLineTokenizer.Tokenize(""));
        Assert.Empty(CommandLineTokenizer.Tokenize("   "));
    }

    [Fact]
    public void Tokenize_SimpleFlags_SplitsOnWhitespace()
    {
        var tokens = CommandLineTokenizer.Tokenize("--resume --model sonnet");
        Assert.Equal(["--resume", "--model", "sonnet"], tokens);
    }

    [Fact]
    public void Tokenize_CollapsesExtraWhitespace()
    {
        var tokens = CommandLineTokenizer.Tokenize("  --flag    value  ");
        Assert.Equal(["--flag", "value"], tokens);
    }

    [Fact]
    public void Tokenize_QuotedSegment_KeepsSpacesTogether()
    {
        var tokens = CommandLineTokenizer.Tokenize("--message \"hello world\" --flag");
        Assert.Equal(["--message", "hello world", "--flag"], tokens);
    }

    [Fact]
    public void Tokenize_DoubledQuoteInsideQuotes_IsLiteralQuote()
    {
        var tokens = CommandLineTokenizer.Tokenize("--say \"she said \"\"hi\"\"\"");
        Assert.Equal(["--say", "she said \"hi\""], tokens);
    }
}
