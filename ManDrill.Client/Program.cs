using ManDrill.Client.Services;
using Microsoft.Build.Locator;

var builder = WebApplication.CreateBuilder(args);

if (!MSBuildLocator.IsRegistered)
{
    try
    {
        var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
        var instance = instances.FirstOrDefault(i => i.Version.Major >= 16) ?? instances[0];
        MSBuildLocator.RegisterInstance(instance);
        Console.WriteLine($"Registered MSBuild: {instance.Name} ({instance.Version})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"MSBuild registration failed: {ex.Message}");
    }
}

// 1) Add MVC
builder.Services.AddControllersWithViews();

// 2) <<< Register SignalR
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

// 3) <<< Serve your static files (so the SignalR client script can be loaded)
app.UseStaticFiles();

// 4) <<< Map the ProgressHub endpoint
app.MapHub<ProgressHub>("/progressHub");

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
