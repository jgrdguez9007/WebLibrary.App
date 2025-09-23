using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using WebLibrary.App.Models;

namespace WebLibrary.App.Controllers
{
    [Route("docs")]
    public class DocsController : Controller
    {
        private readonly IWebHostEnvironment _env;
        public DocsController(IWebHostEnvironment env) { _env = env; }

        public class DocListItem
        {
            public string Title { get; set; } = "";
            public string PdfUrl { get; set; } = "";
            public string JsonUrl { get; set; } = "";
            public string ThumbUrl { get; set; } = "";
            public string Cat { get; set; } = "";
            public string Type { get; set; } = "";
            public DateTime? Date { get; set; }
        }

        public class DocListVm
        {
            public string Cat { get; set; } = "";
            public string Type { get; set; } = "";
            public List<DocListItem> Items { get; set; } = new();
        }

        [HttpGet("")]
        public IActionResult Index([FromQuery] string? cat, [FromQuery] string? type)
        {
            var dataDir = Path.Combine(_env.WebRootPath, "data");
            Directory.CreateDirectory(dataDir);

            var items = new List<DocListItem>();

            foreach (var jf in Directory.GetFiles(dataDir, "*.json"))
            {
                try
                {
                    var txt = System.IO.File.ReadAllText(jf);
                    var dj = JsonConvert.DeserializeObject<DocumentJson>(txt);
                    if (dj == null) continue;

                    items.Add(new DocListItem
                    {
                        Title   = string.IsNullOrWhiteSpace(dj.Title) ? Path.GetFileNameWithoutExtension(jf) : dj.Title,
                        PdfUrl  = dj.Source ?? "",
                        JsonUrl = "/data/" + Path.GetFileName(jf),
                        ThumbUrl= string.IsNullOrEmpty(dj.ThumbUrl) ? "/img/placeholder.svg" : dj.ThumbUrl,
                        Cat     = dj.Category ?? "",
                        Type    = dj.DocType ?? "",
                        Date    = dj.Meta?.DetectedDate ?? System.IO.File.GetLastWriteTime(jf)
                    });
                }
                catch
                {
                    // Ignora JSON inválidos
                }
            }

            if (!string.IsNullOrWhiteSpace(cat))
                items = items.Where(i => string.Equals(i.Cat, cat, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrWhiteSpace(type))
                items = items.Where(i => string.Equals(i.Type, type, StringComparison.OrdinalIgnoreCase)).ToList();

            var vm = new DocListVm
            {
                Cat = cat ?? "",
                Type = type ?? "",
                Items = items.OrderByDescending(i => i.Date).ToList()
            };

            return View(vm);
        }
    }
}
