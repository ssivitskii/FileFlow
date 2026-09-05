namespace FileFlow.Cli;

public sealed class FileFlowApplication
{
    private readonly string? _applicationDataRoot;
    private readonly TextReader _input;
    private readonly TextWriter _output;

    public FileFlowApplication(TextReader input, TextWriter output, string? applicationDataRoot = null)
    {
        _input = input;
        _output = output;
        _applicationDataRoot = applicationDataRoot;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var textOutput = new TextWriterOutput(_output);
        var shell = _applicationDataRoot is null
            ? new FileFlowShell(textOutput)
            : new FileFlowShell(textOutput, _applicationDataRoot);
        if (args is ["--help"] or ["help"])
        {
            shell.Execute("help");
            return 0;
        }

        if (args.Length == 2 && args[0] == "--script")
        {
            try
            {
                string[] lines = await File.ReadAllLinesAsync(args[1], cancellationToken).ConfigureAwait(false);
                return RunLines(shell, lines);
            }
            catch (Exception exception) when (exception is ArgumentException
                or IOException
                or UnauthorizedAccessException)
            {
                await _output.WriteLineAsync($"Error: cannot read script '{args[1]}': {exception.Message}")
                    .ConfigureAwait(false);
                return 2;
            }
        }

        if (args.Length != 0)
        {
            await _output.WriteLineAsync("Usage: fileflow [--script <commands.txt>]").ConfigureAwait(false);
            return 2;
        }

        await _output.WriteLineAsync("FileFlow CLI. Type 'help' for commands.").ConfigureAwait(false);
        while (!shell.ExitRequested)
        {
            await _output.WriteAsync("> ").ConfigureAwait(false);
            string? line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;
            if (!string.IsNullOrWhiteSpace(line))
                ExecuteSafely(shell, line);
        }

        return 0;
    }

    private int RunLines(FileFlowShell shell, IEnumerable<string> lines)
    {
        foreach (string line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            if (!ExecuteSafely(shell, line))
                return 1;
            if (shell.ExitRequested)
                break;
        }

        return 0;
    }

    private bool ExecuteSafely(FileFlowShell shell, string line)
    {
        try
        {
            shell.Execute(line);
            return true;
        }
        catch (InvalidDataException exception)
        {
            _output.WriteLine($"Error: {exception.Message}");
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or InvalidOperationException
            or UnauthorizedAccessException)
        {
            _output.WriteLine($"Error: {exception.Message}");
            return false;
        }
    }

    private sealed class TextWriterOutput : FileFlow.Core.Abstractions.IOutputWriter
    {
        private readonly TextWriter _writer;

        public TextWriterOutput(TextWriter writer)
        {
            _writer = writer;
        }

        public void WriteLine(string value)
        {
            _writer.WriteLine(value);
        }
    }
}
