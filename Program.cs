using WebLibrary.App.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

// Servicios de utilidades e ingesta
builder.Services.AddSingleton<TextUtils>();
builder.Services.AddSingleton<KeywordExtractor>();
builder.Services.AddSingleton<TextSummarizer>();
builder.Services.AddSingleton<IIngestService, IngestService>();

// Servicio de búsqueda (Lucene)
builder.Services.AddSingleton<SearchIndex>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

// Rutas por atributo (p.ej. /admin, /docs, /search)
app.MapControllers();

// Ruta convencional por defecto
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

