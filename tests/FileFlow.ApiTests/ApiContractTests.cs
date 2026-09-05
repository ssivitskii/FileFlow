using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FileFlow.ApiTests;

public sealed class ApiContractTests(FileFlowApiFactory factory) : IClassFixture<FileFlowApiFactory>
{
    [Fact]
    public async Task WorkspaceIsBoundedAndDoesNotLeakAbsolutePaths()
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/workspace?path=.");
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(factory.WorkspaceRoot, body, StringComparison.Ordinal);
        Assert.Contains("hello.txt", body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("folder\\other.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows")]
    [InlineData("folder//other.txt")]
    public async Task PathsRejectTraversalAndAmbiguity(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync($"/api/files/preview?path={Uri.EscapeDataString(path)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task PreviewReturnsHostileMarkupAsLiteralJsonText()
    {
        using HttpClient client = factory.CreateClient();
        JsonElement result = await client.GetFromJsonAsync<JsonElement>("/api/files/preview?path=hello.txt");
        Assert.Equal("hello <script>alert(1)</script>", result.GetProperty("text").GetString());
        Assert.False(result.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task PreviewRejectsBinaryAndHandlesUtf8Boundary()
    {
        await File.WriteAllBytesAsync(Path.Combine(factory.WorkspaceRoot, "binary.bin"), [1, 0, 2]);
        string prefix = new('a', FileFlow.Api.WorkspaceReader.MaxPreviewBytes - 1);
        await File.WriteAllTextAsync(Path.Combine(factory.WorkspaceRoot, "large.txt"), string.Concat(prefix, "€tail"));
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage binary = await client.GetAsync("/api/files/preview?path=binary.bin");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, binary.StatusCode);
        using HttpResponseMessage large = await client.GetAsync("/api/files/preview?path=large.txt");
        Assert.Equal(HttpStatusCode.OK, large.StatusCode);
        JsonElement payload = await large.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("truncated").GetBoolean());
        Assert.DoesNotContain('\uFFFD', payload.GetProperty("text").GetString()!);
    }

    [Fact]
    public async Task OperationPreviewNeverChangesWorkspace()
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/operations/preview", new
        {
            operation = "rename",
            source = "hello.txt",
            destination = "renamed.txt",
        });
        JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.GetProperty("isValid").GetBoolean());
        Assert.True(File.Exists(Path.Combine(factory.WorkspaceRoot, "hello.txt")));
        Assert.False(File.Exists(Path.Combine(factory.WorkspaceRoot, "renamed.txt")));
        Assert.False(File.Exists(Path.Combine(factory.ApplicationDataRoot, "journal.jsonl")));
    }

    [Fact]
    public async Task ListingRejectsItsConfiguredEntryCap()
    {
        string crowded = Path.Combine(factory.WorkspaceRoot, "crowded");
        Directory.CreateDirectory(crowded);
        try
        {
            for (int index = 0; index <= FileFlow.Api.WorkspaceReader.MaxEntries; index++)
                await File.WriteAllTextAsync(Path.Combine(crowded, $"{index:D4}.txt"), string.Empty);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync("/api/workspace?path=crowded");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
        finally
        {
            Directory.Delete(crowded, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateScanRejectsWideDirectoryBeforeSortingBeyondItsCap()
    {
        string crowded = Path.Combine(factory.WorkspaceRoot, "wide-scan");
        Directory.CreateDirectory(crowded);
        try
        {
            for (int index = 0; index <= FileFlow.Api.DuplicateScanner.MaxEntries; index++)
                Directory.CreateDirectory(Path.Combine(crowded, $"{index:D4}"));
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync("/api/duplicates?path=wide-scan");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
        finally
        {
            Directory.Delete(crowded, recursive: true);
        }
    }

    [Fact]
    public async Task BoundedHasherRejectsGrowthShrinkAndSameSizeChanges()
    {
        string file = Path.Combine(factory.WorkspaceRoot, "changing.bin");
        await File.WriteAllTextAsync(file, "aaaa");
        var snapshot = new FileInfo(file);
        DateTime expectedLastWrite = snapshot.LastWriteTimeUtc;
        try
        {
            string actual = await FileFlow.Api.BoundedFileHasher.HashAsync(file, 4, expectedLastWrite, CancellationToken.None);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("aaaa"))), actual);

            await File.WriteAllTextAsync(file, "aaaaa");
            await Assert.ThrowsAsync<FileFlow.Api.ApiProblemException>(() =>
                FileFlow.Api.BoundedFileHasher.HashAsync(file, 4, expectedLastWrite, CancellationToken.None));

            await File.WriteAllTextAsync(file, "aaa");
            await Assert.ThrowsAsync<FileFlow.Api.ApiProblemException>(() =>
                FileFlow.Api.BoundedFileHasher.HashAsync(file, 4, expectedLastWrite, CancellationToken.None));

            await File.WriteAllTextAsync(file, "bbbb");
            File.SetLastWriteTimeUtc(file, expectedLastWrite.AddMinutes(1));
            await Assert.ThrowsAsync<FileFlow.Api.ApiProblemException>(() =>
                FileFlow.Api.BoundedFileHasher.HashAsync(file, 4, expectedLastWrite, CancellationToken.None));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ApiRejectsSimpleCrossOriginRequestsBeforeExpensiveHandlers()
    {
        using HttpClient client = factory.Server.CreateClient();
        using HttpResponseMessage missing = await client.GetAsync("/API/DuPlIcAtEs?path=.");
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);

        using var wrongRequest = new HttpRequestMessage(HttpMethod.Get, "/api/duplicates?path=.");
        wrongRequest.Headers.Add("X-FileFlow-Client", "wrong");
        using HttpResponseMessage wrong = await client.SendAsync(wrongRequest);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/duplicates?path=.");
        preflight.Headers.Add("Origin", "https://hostile.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "X-FileFlow-Client");
        using HttpResponseMessage options = await client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.Forbidden, options.StatusCode);
        Assert.False(options.Headers.Contains("Access-Control-Allow-Origin"));

        using HttpClient allowed = factory.CreateClient();
        using HttpResponseMessage scan = await allowed.GetAsync("/API/DuPlIcAtEs?path=.");
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
    }

    [Fact]
    public async Task OversizedOperationPreviewIsRejectedEarlyWithSafeHeaders()
    {
        using HttpClient client = factory.CreateClient();
        using var content = new StringContent(new string('a', FileFlow.Api.ApiApplicationExtensions.MaxRequestBodyBytes + 1), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/operations/preview", content);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("8192 bytes", body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task PreviewRejectsFinalAndIntermediateSymbolicLinks()
    {
        string external = Path.Combine(Path.GetDirectoryName(factory.WorkspaceRoot)!, "external.txt");
        string link = Path.Combine(factory.WorkspaceRoot, "linked.txt");
        string linkedDirectory = Path.Combine(factory.WorkspaceRoot, "linked-folder");
        string brokenLink = Path.Combine(factory.WorkspaceRoot, "broken-link.txt");
        await File.WriteAllTextAsync(external, "secret");
        try
        {
            File.CreateSymbolicLink(link, external);
            Directory.CreateSymbolicLink(linkedDirectory, Path.Combine(factory.WorkspaceRoot, "folder"));
            File.CreateSymbolicLink(brokenLink, Path.Combine(factory.WorkspaceRoot, "missing.txt"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        try
        {
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage final = await client.GetAsync("/api/files/preview?path=linked.txt");
            using HttpResponseMessage intermediate = await client.GetAsync("/api/files/preview?path=linked-folder%2Fother.txt");
            using HttpResponseMessage broken = await client.PostAsJsonAsync("/api/operations/preview", new
            {
                operation = "copy",
                source = "hello.txt",
                destination = "broken-link.txt",
            });
            Assert.Equal(HttpStatusCode.BadRequest, final.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, intermediate.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        }
        finally
        {
            File.Delete(link);
            File.Delete(brokenLink);
            Directory.Delete(linkedDirectory);
            File.Delete(external);
        }
    }

    [Fact]
    public async Task HistoryRejectsMalformedDataWithoutLeakingIt()
    {
        string journal = Path.Combine(factory.ApplicationDataRoot, "journal.jsonl");
        await File.WriteAllTextAsync(journal, "{not-json}\n");
        try
        {
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync("/api/history");
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain(factory.ApplicationDataRoot, body, StringComparison.Ordinal);
            Assert.DoesNotContain("not-json", body, StringComparison.Ordinal);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        }
        finally
        {
            File.Delete(journal);
        }
    }

    [Fact]
    public async Task PreviewReportsExistingDestinationAsConflict()
    {
        using HttpClient client = factory.CreateClient();
        JsonElement result = await (await client.PostAsJsonAsync("/api/operations/preview", new
        {
            operation = "copy",
            source = "hello.txt",
            destination = "folder/other.txt",
        })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(result.GetProperty("isValid").GetBoolean());
        Assert.True(result.GetProperty("isConflict").GetBoolean());
    }

    [Fact]
    public void RouteInventoryContainsNoMutationOrUndoEndpoint()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        string[] routes = scope.ServiceProvider.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Where(route => route.StartsWith("/api", StringComparison.Ordinal) || route.StartsWith("/health", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["/api/duplicates", "/api/files/preview", "/api/history", "/api/operations/preview", "/api/workspace", "/health/live"],
            routes);
    }

    [Fact]
    public async Task UnapprovedHostIsRejected()
    {
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Host = "evil.example";
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
