namespace FileFlow.Cli;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        return new FileFlowApplication(Console.In, Console.Out).RunAsync(args, CancellationToken.None);
    }
}
