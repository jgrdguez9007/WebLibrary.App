using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebLibrary.App.Data;
using WebLibrary.App.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

// EF Core + Identity (SQLite)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 8;
    o.User.RequireUniqueEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/account/login";
    o.AccessDeniedPath = "/account/denied";
});

// Autorización
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PuedeSubirEliminar", p => p.RequireRole("Admin"));
    options.AddPolicy("PuedeVerInternos",   p => p.RequireRole("Admin", "Secretario"));
});

// Servicios propios
builder.Services.AddSingleton<TextUtils>();
builder.Services.AddSingleton<KeywordExtractor>();
builder.Services.AddSingleton<TextSummarizer>();
builder.Services.AddSingleton<IIngestService, IngestService>();
builder.Services.AddSingleton<SearchIndex>();

var app = builder.Build();

// Seed después de Build
using (var scope = app.Services.CreateScope())
{
    await IdentitySeed.EnsureSeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
