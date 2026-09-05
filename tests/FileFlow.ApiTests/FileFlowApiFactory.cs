using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FileFlow.ApiTests;

public sealed class FileFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fileflow-api-tests", Guid.NewGuid().ToString("N"));

    public string WorkspaceRoot => Path.Combine(_root, "workspace");

    public string ApplicationDataRoot => Path.Combine(_root, "data");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(WorkspaceRoot);
        Directory.CreateDirectory(ApplicationDataRoot);
        Directory.CreateDirectory(Path.Combine(WorkspaceRoot, "folder"));
        File.WriteAllText(Path.Combine(WorkspaceRoot, "hello.txt"), "hello <script>alert(1)</script>");
        File.WriteAllText(Path.Combine(WorkspaceRoot, "folder", "other.txt"), "other");
        return Task.CompletedTask;
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("FileFlow:WorkspaceRoot", WorkspaceRoot);
        builder.UseSetting("FileFlow:ApplicationDataRoot", ApplicationDataRoot);
        builder.UseSetting("AllowedHosts", "localhost;127.0.0.1;[::1]");
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("X-FileFlow-Client", "web");
    }
}
