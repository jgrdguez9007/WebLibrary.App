using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

using Newtonsoft.Json;
using WebLibrary.App.Models;
using Microsoft.AspNetCore.Hosting;

// Evita conflicto con Lucene.Net.Store.Directory
using DirectoryIO = System.IO.Directory;

namespace WebLibrary.App.Services
{
  /// <summary>
  /// Servicio de indexación y búsqueda con Lucene.NET 4.8
  /// - Indexa cada JSON (DocumentJson) por CHUNKS (rango de páginas).
  /// - Campos: title, text, keywords, category, type, pdf, json, thumb, pageStart, pageEnd, date.
  /// - RebuildFromData() reindexa todo /wwwroot/data; Search() consulta y devuelve resultados con excerpt.
  /// </summary>
  public class SearchIndex
  {
    public class Result
    {
      public string Title { get; set; } = "";
      public string PdfUrl { get; set; } = "";
      public string JsonUrl { get; set; } = "";
      public string ThumbUrl { get; set; } = "";
      public string Category { get; set; } = "";
      public string DocType { get; set; } = "";
      public int PageStart { get; set; }
      public int PageEnd { get; set; }
      public float Score { get; set; }
      public DateTime? Date { get; set; }
      public string Excerpt { get; set; } = "";
    }

    private static readonly LuceneVersion LV = LuceneVersion.LUCENE_48;
    private readonly IWebHostEnvironment _env;
    private readonly Analyzer _analyzer;
    private readonly object _writerLock = new();

    public SearchIndex(IWebHostEnvironment env, TextUtils utils)
    {
      _env = env;

      var stop = new Lucene.Net.Analysis.Util.CharArraySet(LV, utils.Stopwords, true);
      _analyzer = new StandardAnalyzer(LV, stop);

      DirectoryIO.CreateDirectory(GetIndexPath());
    }

    private string GetIndexPath()
      => Path.Combine(_env.ContentRootPath, "App_Data", "index");

    private Lucene.Net.Store.Directory OpenDirectory()
      => FSDirectory.Open(GetIndexPath());

    /// <summary>Elimina y reconstruye el índice a partir de los JSON en /wwwroot/data.</summary>
    public void RebuildFromData()
    {
      var dataDir = Path.Combine(_env.WebRootPath, "data");
      DirectoryIO.CreateDirectory(dataDir);

      using var dir = OpenDirectory();
      var iwc = new IndexWriterConfig(LV, _analyzer) { OpenMode = OpenMode.CREATE };
      lock (_writerLock)
      using (var writer = new IndexWriter(dir, iwc))
      {
        foreach (var jf in DirectoryIO.GetFiles(dataDir, "*.json"))
        {
          try
          {
            var txt = File.ReadAllText(jf);
            var dj = JsonConvert.DeserializeObject<DocumentJson>(txt);
            if (dj == null) continue;

            var key = Path.GetFileNameWithoutExtension(jf); // clave estable
            IndexDocumentJson(writer, dj, key, "/data/" + Path.GetFileName(jf));
          }
          catch
          {
            // ignorar corruptos
          }
        }
        writer.Commit();
      }
    }

    /// <summary>Indexa/actualiza un único DocumentJson.</summary>
    public void Upsert(DocumentJson doc, string jsonUrl, string? stableKey = null)
    {
      var key = string.IsNullOrWhiteSpace(stableKey)
        ? (Path.GetFileNameWithoutExtension(jsonUrl) ?? doc.Id ?? Guid.NewGuid().ToString("N"))
        : stableKey;

      using var dir = OpenDirectory();
      var iwc = new IndexWriterConfig(LV, _analyzer) { OpenMode = OpenMode.CREATE_OR_APPEND };
      lock (_writerLock)
      using (var writer = new IndexWriter(dir, iwc))
      {
        writer.DeleteDocuments(new Term("docKey", key));
        IndexDocumentJson(writer, doc, key, jsonUrl);
        writer.Commit();
      }
    }

    /// <summary>Busca por texto libre. Devuelve los mejores hits con extracto.</summary>
    public List<Result> Search(string query, int top = 20)
{
  query = (query ?? "").Trim();
  if (string.IsNullOrWhiteSpace(query)) return new();

  using var dir = OpenDirectory();

  // Si el índice no existe aún, intenta reconstruirlo y vuelve a comprobar
  if (!DirectoryReader.IndexExists(dir))
  {
    RebuildFromData();
    if (!DirectoryReader.IndexExists(dir)) return new(); // sigue vacío: no hay JSON que indexar
  }

  using var reader = DirectoryReader.Open(dir);
  var searcher = new IndexSearcher(reader);

  var fields = new[] { "title", "text", "keywords" };
  var boosts = new Dictionary<string, float> { { "title", 2.2f }, { "keywords", 1.5f }, { "text", 1.0f } };
  var parser = new MultiFieldQueryParser(LV, fields, _analyzer, boosts);

  Query q;
  try { q = parser.Parse(query); }
  catch { q = parser.Parse(QueryParser.Escape(query)); }

  var hits = searcher.Search(q, top).ScoreDocs;
  var results = new List<Result>(hits.Length);

  foreach (var sd in hits)
  {
    var d = searcher.Doc(sd.Doc);
    var res = new Result
    {
      Title     = d.Get("title") ?? "",
      PdfUrl    = d.Get("pdf") ?? "",
      JsonUrl   = d.Get("json") ?? "",
      ThumbUrl  = d.Get("thumb") ?? "/img/placeholder.svg",
      Category  = d.Get("category") ?? "",
      DocType   = d.Get("type") ?? "",
      PageStart = int.TryParse(d.Get("pageStart"), out var ps) ? ps : 0,
      PageEnd   = int.TryParse(d.Get("pageEnd"), out var pe) ? pe : 0,
      Score     = sd.Score,
      Date      = long.TryParse(d.Get("dateTicks"), out var t) ? new DateTime(t, DateTimeKind.Utc) : (DateTime?)null,
      Excerpt   = (d.Get("textStored") ?? "").Length > 0 ? BuildExcerpt(d.Get("textStored"), query, 200) : ""
    };
    results.Add(res);
  }

  return results;
}


    // ------------------ Internos ------------------

    private static void IndexDocumentJson(IndexWriter writer, DocumentJson dj, string docKey, string jsonUrl)
    {
      var dateTicks = dj.Meta?.DetectedDate?.ToUniversalTime().Ticks ?? 0L;
      var globalKw  = string.Join(" ", dj.GlobalKeywords ?? new());

      foreach (var ch in dj.Chunks ?? new())
      {
        var doc = new Lucene.Net.Documents.Document
        {
          new Lucene.Net.Documents.StringField("docKey", docKey, Lucene.Net.Documents.Field.Store.NO),
          new Lucene.Net.Documents.Int32Field("pageStart", ch.PageStart, Lucene.Net.Documents.Field.Store.YES),
          new Lucene.Net.Documents.Int32Field("pageEnd",   ch.PageEnd,   Lucene.Net.Documents.Field.Store.YES),

          new Lucene.Net.Documents.StringField("pdf",   dj.Source ?? "", Lucene.Net.Documents.Field.Store.YES),
          new Lucene.Net.Documents.StringField("json",  jsonUrl,         Lucene.Net.Documents.Field.Store.YES),
          new Lucene.Net.Documents.StringField("thumb", string.IsNullOrEmpty(dj.ThumbUrl) ? "/img/placeholder.svg" : dj.ThumbUrl, Lucene.Net.Documents.Field.Store.YES),
          new Lucene.Net.Documents.StringField("category", dj.Category ?? "", Lucene.Net.Documents.Field.Store.YES),
          new Lucene.Net.Documents.StringField("type",     dj.DocType  ?? "", Lucene.Net.Documents.Field.Store.YES),
          new Lucene.Net.Documents.StringField("title",    dj.Title    ?? "", Lucene.Net.Documents.Field.Store.YES),
          new Lucene.Net.Documents.StringField("dateTicks", dateTicks.ToString(), Lucene.Net.Documents.Field.Store.YES),

          new Lucene.Net.Documents.TextField("text", (ch.Text ?? ""), Lucene.Net.Documents.Field.Store.NO),
          new Lucene.Net.Documents.TextField("keywords", string.Join(" ", ch.Keywords ?? new()) + " " + globalKw, Lucene.Net.Documents.Field.Store.NO),

          new Lucene.Net.Documents.StoredField("textStored", (ch.Text ?? ""))
        };

        writer.AddDocument(doc);
      }
    }

    private static string BuildExcerpt(string text, string query, int maxLen)
    {
      if (string.IsNullOrWhiteSpace(text)) return "";

      var qTerms = Regex.Matches(query.ToLowerInvariant(), "[\\p{L}\\p{Nd}]{3,}")
                        .Cast<Match>()
                        .Select(m => m.Value)
                        .Distinct()
                        .ToArray();

      var idx = -1;
      foreach (var qt in qTerms)
      {
        idx = text.IndexOf(qt, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) break;
      }
      if (idx < 0) idx = 0;

      var start = Math.Max(0, idx - maxLen / 3);
      var len   = Math.Min(maxLen, Math.Max(0, text.Length - start));
      var slice = text.Substring(start, len).Trim();

      if (start > 0) slice = "…" + slice;
      if (start + len < text.Length) slice += "…";

      return slice.Replace("\r", " ").Replace("\n", " ");
    }
  }
}

