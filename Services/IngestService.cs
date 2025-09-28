// Services/IngestService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UglyToad.PdfPig;
using WebLibrary.App.Models;

namespace WebLibrary.App.Services
{
  public interface IIngestService
  {
    DocumentJson ProcessPdf(string pdfPath, string dataOutDir, string thumbsOutDir, string category, string docType);
  }

  public class IngestService : IIngestService
  {
    private readonly KeywordExtractor _kw;
    private readonly TextSummarizer _sum;

    public IngestService(KeywordExtractor kw, TextSummarizer sum)
    {
      _kw = kw;
      _sum = sum;
    }

    public DocumentJson ProcessPdf(string pdfPath, string dataOutDir, string thumbsOutDir, string category, string docType)
    {
      if (!File.Exists(pdfPath)) throw new FileNotFoundException(pdfPath);
      Directory.CreateDirectory(dataOutDir);
      Directory.CreateDirectory(thumbsOutDir);

      var title = Path.GetFileNameWithoutExtension(pdfPath);

      // 1) Texto por página (o OCR si está vacío)
      var pages = ExtractTextPerPage(pdfPath);
      if (IsMostlyEmpty(pages)) pages = OcrWithExternalTools(pdfPath);

      // 2) Chunking + metadata de texto
      var chunks = Chunk(pages, chunkSize: 900);
      foreach (var c in chunks)
      {
        c.Keywords = _kw.ExtractTop(c.Text, 12);
        c.Summary  = _sum.Summarize(c.Text, 3);
      }

      // 3) Miniatura (1ª página) → wwwroot/thumbs/{title}.png
      var prefix    = Path.Combine(thumbsOutDir, title);
      var thumbPath = prefix + ".png";
      try
      {
        if (File.Exists(thumbPath)) File.Delete(thumbPath);
        // -scale-to 480 = lado mayor 480px, mantiene proporción. -cropbox para evitar bordes.
        Run(ResolveTool("pdftoppm"), $"-png -singlefile -f 1 -l 1 -scale-to 480 -cropbox \"{pdfPath}\" \"{prefix}\"");
      }
      catch { /* no-op */ }
      var thumbUrl = File.Exists(thumbPath) ? $"/thumbs/{title}.png" : "/img/placeholder.svg";

      // 4) JSON final
      var json = new DocumentJson
      {
        Title          = title,
        Source         = "/files/" + Path.GetFileName(pdfPath),
        Pages          = pages.Count,
        Chunks         = chunks,
        GlobalKeywords = _kw.ExtractTop(string.Join("\n", pages), 20),
        GlobalSummary  = _sum.Summarize(string.Join(" ", chunks.Select(c => c.Text)), 10),
        Meta           = new DocumentMeta { DetectedDate = DateTime.UtcNow },
        Category       = category ?? "",
        DocType        = docType ?? "",
        ThumbUrl       = thumbUrl
      };

      var outPath = Path.Combine(dataOutDir, title + ".json");
      File.WriteAllText(outPath, JsonConvert.SerializeObject(json, Formatting.Indented));
      return json;
    }

    // ----------------- helpers -----------------

    private static List<string> ExtractTextPerPage(string pdfPath)
    {
      var list = new List<string>();
      using var doc = PdfDocument.Open(pdfPath);
      foreach (var p in doc.GetPages()) list.Add(p.Text ?? "");
      return list;
    }

    private static bool IsMostlyEmpty(List<string> pages)
    {
      var total = string.Join("", pages ?? new()).Trim();
      return string.IsNullOrWhiteSpace(total) || total.Length < 200;
    }

    private static List<string> OcrWithExternalTools(string pdfPath)
    {
      var tmp = Path.Combine(Path.GetTempPath(), "wl_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tmp);

      // PDF -> PNGs (300 dpi)
      Run(ResolveTool("pdftoppm"), $"-png -r 300 \"{pdfPath}\" \"{Path.Combine(tmp, "p")}\"");
      var pngs = Directory.GetFiles(tmp, "p-*.png").OrderBy(f => f).ToList();

      // OCR por página (spa+eng)
      var pages = new List<string>();
      foreach (var img in pngs)
      {
        var txt = Run(ResolveTool("tesseract"), $"\"{img}\" stdout -l spa+eng");
        pages.Add(txt);
      }

      try { Directory.Delete(tmp, true); } catch { /* ignore */ }
      return pages;
    }

    private static string Run(string fileName, string args)
    {
      var psi = new ProcessStartInfo(fileName, args)
      {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
        CreateNoWindow         = true
      };
      using var p = Process.Start(psi)!;
      var stdout = p.StandardOutput.ReadToEnd();
      var stderr = p.StandardError.ReadToEnd();
      p.WaitForExit();
      return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    // Localiza ejecutables en Windows sin depender del PATH
    private static string ResolveTool(string tool)
    {
      var isWin = OperatingSystem.IsWindows();
      var exe = isWin ? tool + ".exe" : tool;

      // 1) Variables de entorno explícitas
      if (tool.Equals("pdftoppm", StringComparison.OrdinalIgnoreCase))
      {
        var poppler = Environment.GetEnvironmentVariable("POPPLER_BIN");
        if (!string.IsNullOrWhiteSpace(poppler))
        {
          var p = Path.Combine(poppler, exe);
          if (File.Exists(p)) return p;
        }
      }
      if (tool.Equals("tesseract", StringComparison.OrdinalIgnoreCase))
      {
        var tess = Environment.GetEnvironmentVariable("TESSERACT_PATH");
        if (!string.IsNullOrWhiteSpace(tess))
        {
          var p = Path.Combine(tess, exe);
          if (File.Exists(p)) return p;
        }
      }

      // 2) Rutas típicas winget/choco
      var candidates = new List<string>();
      if (isWin)
      {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // Poppler (winget)
        candidates.AddRange(Directory.Exists(local)
          ? Directory.GetDirectories(Path.Combine(local, "Microsoft", "WinGet", "Packages"))
            .Where(d => d.Contains("Poppler", StringComparison.OrdinalIgnoreCase))
            .SelectMany(d => new[]
            {
              Path.Combine(d, "Release-64bit", "Library", "bin", exe),
              Path.Combine(d, "Release-0.68.0", "bin", exe)
            })
          : Enumerable.Empty<string>());

        // Poppler (choco)
        candidates.Add(Path.Combine("C:\\ProgramData\\chocolatey\\lib\\poppler\\tools", exe));

        // Tesseract (standard)
        candidates.Add(Path.Combine("C:\\Program Files\\Tesseract-OCR", exe));
        candidates.Add(Path.Combine("C:\\Program Files (x86)\\Tesseract-OCR", exe));
      }

      foreach (var p in candidates.Distinct())
        if (File.Exists(p)) return p;

      // 3) Fallback: confía en PATH
      return exe;
    }

    private static List<Chunk> Chunk(List<string> pages, int chunkSize)
    {
      var chunks = new List<Chunk>();
      var buf = "";
      var start = 1;

      for (int i = 0; i < pages.Count; i++)
      {
        var page = pages[i] ?? "";
        if ((buf.Length + page.Length) > chunkSize && buf.Length > 0)
        {
          chunks.Add(new Chunk { PageStart = start, PageEnd = i, Text = buf });
          buf = "";
          start = i + 1;
        }
        buf += page + "\n";
      }

      if (!string.IsNullOrWhiteSpace(buf))
        chunks.Add(new Chunk { PageStart = start, PageEnd = pages.Count, Text = buf });

      return chunks;
    }
  }
}
