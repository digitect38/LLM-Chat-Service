var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddSingleton<FabCopilot.ServiceDashboard.Services.HealthCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FabCopilot.ServiceDashboard.Services.HealthCheckService>());

builder.Services.AddSingleton<FabCopilot.ServiceDashboard.Services.DockerStatusService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FabCopilot.ServiceDashboard.Services.DockerStatusService>());

builder.Services.AddSingleton<FabCopilot.ServiceDashboard.Services.ProcessControlService>();
builder.Services.AddSingleton<FabCopilot.ServiceDashboard.Services.LogReaderService>();
builder.Services.AddSingleton<FabCopilot.ServiceDashboard.Services.LogAnalyzerService>();
builder.Services.AddSingleton<FabCopilot.ServiceDashboard.Services.TtsConfigService>();
builder.Services.AddSingleton<FabCopilot.ServiceDashboard.Services.SttConfigService>();
builder.Services.AddSingleton<FabCopilot.ServiceDashboard.Services.ModelConfigService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
