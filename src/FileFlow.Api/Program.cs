using FileFlow.Api;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ApiApplicationExtensions.MaxRequestBodyBytes);
builder.Services.AddFileFlowApi();
WebApplication app = builder.Build();
_ = app.Services.GetRequiredService<RootedWorkspace>();
app.UseFileFlowApi();
app.Run();

public partial class Program;
