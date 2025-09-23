using System;
using System.Collections.Generic;

namespace WebLibrary.App.Models
{
  public class Chunk
  {
    public string ChunkId { get; set; } = Guid.NewGuid().ToString("N");
    public int PageStart { get; set; }
    public int PageEnd { get; set; }
    public string Text { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public string Summary { get; set; } = "";
  }

  public class DocumentMeta
  {
    public DateTime? DetectedDate { get; set; }
    public List<string> Sections { get; set; } = new();
  }

  public class DocumentJson
  {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public int Pages { get; set; }
    public List<Chunk> Chunks { get; set; } = new();
    public List<string> GlobalKeywords { get; set; } = new();
    public string GlobalSummary { get; set; } = "";
    public DocumentMeta Meta { get; set; } = new();
    public string Category { get; set; } = "";   // leyes, documentos-internos, memorias-anuales, estudios, procedimientos
    public string DocType { get; set; } = "";   // ley, decreto-ley, decreto, reglamento, resolucion, etc.
    public string ThumbUrl { get; set; } = "";   // /thumbs/archivo.png o /img/placeholder.svg

  }

  public class SearchHit
  {
    public string Title { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int PageStart { get; set; }
    public int PageEnd { get; set; }
    public string Excerpt { get; set; } = "";
    public float Score { get; set; }
  }
}
