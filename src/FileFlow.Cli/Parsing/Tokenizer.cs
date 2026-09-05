using System.Text;

namespace FileFlow.Cli.Parsing;

public static class Tokenizer
{
    public static IReadOnlyList<string> Tokenize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quote = null;

        for (int index = 0; index < input.Length; index++)
        {
            char character = input[index];
            if (quote is not null && character == '\\' && index + 1 < input.Length && input[index + 1] == quote)
            {
                current.Append(quote.Value);
                index++;
                continue;
            }

            if (character is '\'' or '"')
            {
                if (quote is null)
                {
                    quote = character;
                    continue;
                }

                if (quote == character)
                {
                    quote = null;
                    continue;
                }
            }

            if (char.IsWhiteSpace(character) && quote is null)
            {
                AddToken(tokens, current);
            }
            else
            {
                current.Append(character);
            }
        }

        if (quote is not null)
            throw new ArgumentException($"Unclosed {quote} quote.");
        AddToken(tokens, current);
        return tokens;
    }

    private static void AddToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
            return;
        tokens.Add(current.ToString());
        current.Clear();
    }
}
