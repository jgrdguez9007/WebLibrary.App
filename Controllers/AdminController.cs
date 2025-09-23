using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using WebLibrary.App.Services;
using WebLibrary.App.Models;

namespace WebLibrary.App.Controllers
{
    [Route("admin")]
    public class AdminController : Controller
    {
        private readonly IIngestService _ingest;
        private readonly IWebHostEnvironment _env;
        private readonly SearchIndex _index; // <-- búsqueda

        public AdminController(IIngestService ingest, IWebHostEnvironment env, SearchIndex index)
        {
            _ingest = ingest; _env = env; _index = index;
        }

        // ---------- VMs ----------
        public class UploadResultVm
        {
            public string Title { get; set; } = "";
            public string PdfUrl { get; set; } = "";
            public string JsonUrl { get; set; } = "";
            public DateTime? Date { get; set; }
            public string Category { get; set; } = "";
            public string DocType { get; set; } = "";
            public string ThumbUrl { get; set; } = "";
        }

        public class AdminVm
        {
            public int PdfCount { get; set; }
            public int JsonCount { get; set; }
            public double TotalSizeMb { get; set; }
            public DateTime? LastProcessed { get; set; }
            public List<UploadResultVm> Recent { get; set; } = new();
        }

        // ---------- VISTA ----------
        [HttpGet("")]
        public IActionResult Index()
        {
            var filesDir = Path.Combine(_env.WebRootPath, "files");
            var dataDir  = Path.Combine(_env.WebRootPath, "data");
            Directory.CreateDirectory(filesDir);
            Directory.CreateDirectory(dataDir);

            var pdfs  = Directory.GetFiles(filesDir, "*.pdf");
            var jsons = Directory.GetFiles(dataDir,  "*.json");

            double totalMb = pdfs.Sum(p => new FileInfo(p).Length) / (1024d * 1024d);

            var recent = new List<UploadResultVm>();
            foreach (var jf in jsons)
            {
                try
                {
                    var dj = JsonConvert.DeserializeObject<DocumentJson>(System.IO.File.ReadAllText(jf));
                    if (dj == null) continue;
                    recent.Add(new UploadResultVm
                    {
                        Title   = string.IsNullOrWhiteSpace(dj.Title) ? Path.GetFileNameWithoutExtension(jf) : dj.Title,
                        PdfUrl  = dj.Source ?? "",
                        JsonUrl = "/data/" + Path.GetFileName(jf),
                        Date    = dj.Meta?.DetectedDate ?? System.IO.File.GetLastWriteTime(jf),
                        Category= dj.Category ?? "",
                        DocType = dj.DocType  ?? "",
                        ThumbUrl= string.IsNullOrEmpty(dj.ThumbUrl) ? "/img/placeholder.svg" : dj.ThumbUrl
                    });
                }
                catch
                {
                    recent.Add(new UploadResultVm
                    {
                        Title   = Path.GetFileNameWithoutExtension(jf),
                        PdfUrl  = "",
                        JsonUrl = "/data/" + Path.GetFileName(jf),
                        Date    = System.IO.File.GetLastWriteTime(jf),
                        Category= "",
                        DocType = "",
                        ThumbUrl= "/img/placeholder.svg"
                    });
                }
            }
            recent = recent.OrderByDescending(r => r.Date).Take(6).ToList();

            var vm = new AdminVm
            {
                PdfCount      = pdfs.Length,
                JsonCount     = jsons.Length,
                TotalSizeMb   = Math.Round(totalMb, 2),
                LastProcessed = recent.FirstOrDefault()?.Date,
                Recent        = recent
            };
            return View(vm);
        }

        // ---------- POST HTML ----------
        [HttpPost("upload-form")]
        [RequestSizeLimit(1024L * 1024L * 200L)]
        public IActionResult UploadForm(IFormFile file, [FromForm] string category, [FromForm] string docType)
        {
            if (file is null || file.Length == 0)
                return View("Uploaded", new UploadResultVm { Title = "Archivo vacío" });

            var filesDir  = Path.Combine(_env.WebRootPath, "files");
            var dataDir   = Path.Combine(_env.WebRootPath, "data");
            var thumbsDir = Path.Combine(_env.WebRootPath, "thumbs");
            Directory.CreateDirectory(filesDir);
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(thumbsDir);

            var full = Path.Combine(filesDir, Path.GetFileName(file.FileName));
            using (var fs = System.IO.File.Create(full)) file.CopyTo(fs);

            var json = _ingest.ProcessPdf(full, dataDir, thumbsDir, category, docType);

            // Indexa inmediatamente
            var key     = Path.GetFileNameWithoutExtension(full);
            var jsonUrl = "/data/" + key + ".json";
            _index.Upsert(json, jsonUrl, key);

            return View("Uploaded", new UploadResultVm
            {
                Title    = string.IsNullOrWhiteSpace(json.Title) ? key : json.Title,
                PdfUrl   = json.Source,
                JsonUrl  = jsonUrl,
                Date     = json.Meta?.DetectedDate,
                Category = json.Category,
                DocType  = json.DocType,
                ThumbUrl = string.IsNullOrEmpty(json.ThumbUrl) ? "/img/placeholder.svg" : json.ThumbUrl
            });
        }

        // ---------- API JSON ----------
        [HttpPost("upload")]
        public IActionResult Upload(IFormFile file, [FromQuery] string? category, [FromQuery] string? docType)
        {
            if (file is null || file.Length == 0) return BadRequest("Archivo vacío.");

            var filesDir  = Path.Combine(_env.WebRootPath, "files");
            var dataDir   = Path.Combine(_env.WebRootPath, "data");
            var thumbsDir = Path.Combine(_env.WebRootPath, "thumbs");
            Directory.CreateDirectory(filesDir);
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(thumbsDir);

            var full = Path.Combine(filesDir, Path.GetFileName(file.FileName));
            using (var fs = System.IO.File.Create(full)) file.CopyTo(fs);

            var json = _ingest.ProcessPdf(full, dataDir, thumbsDir, category ?? "", docType ?? "");

            var key     = Path.GetFileNameWithoutExtension(full);
            var jsonUrl = "/data/" + key + ".json";
            _index.Upsert(json, jsonUrl, key);

            return Ok(new { ok = true, json = jsonUrl });
        }

        [HttpPost("process-existing")]
        public IActionResult ProcessExisting([FromQuery] string file, [FromQuery] string? category, [FromQuery] string? docType)
        {
            if (string.IsNullOrWhiteSpace(file)) return BadRequest("Falta ?file=Nombre.pdf");
            var full = Path.Combine(_env.WebRootPath, "files", file);
            if (!System.IO.File.Exists(full)) return NotFound("No existe en /wwwroot/files");

            var dataDir   = Path.Combine(_env.WebRootPath, "data");
            var thumbsDir = Path.Combine(_env.WebRootPath, "thumbs");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(thumbsDir);

            var json = _ingest.ProcessPdf(full, dataDir, thumbsDir, category ?? "", docType ?? "");

            var key     = Path.GetFileNameWithoutExtension(full);
            var jsonUrl = "/data/" + key + ".json";
            _index.Upsert(json, jsonUrl, key);

            return Ok(new { ok = true, json = jsonUrl });
        }

        // ---------- Admin: reconstruir índice ----------
        [HttpPost("rebuild-index")]
        public IActionResult RebuildIndex()
        {
            _index.RebuildFromData();
            return Ok(new { ok = true });
        }
    }
}
