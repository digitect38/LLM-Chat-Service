using FabCopilot.WebClient.Configuration;
using FabCopilot.WebClient.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.Configure<ModelOptions>(builder.Configuration.GetSection(ModelOptions.SectionName));
builder.Services.Configure<EquipmentOptions>(builder.Configuration.GetSection(EquipmentOptions.SectionName));
builder.Services.AddScoped<ChatService>();
builder.Services.AddSingleton<EmbeddingConfigService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
