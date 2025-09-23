using Microsoft.AspNetCore.Mvc;
using WebLibrary.App.Services;
using System.Diagnostics;

namespace WebLibrary.App.Controllers
{
  [Route("search")]
  public class SearchController : Controller
  {
    private readonly SearchIndex _index;

    public SearchController(SearchIndex index)
    {
      _index = index;
    }

    public class SearchVm
    {
      public string Q { get; set; } = "";
      public List<SearchIndex.Result> Results { get; set; } = new();
      public int Count => Results?.Count ?? 0;
      public long ElapsedMs { get; set; }
    }

    // GET /search?q=palabras
    [HttpGet("")]
    public IActionResult Index([FromQuery] string? q)
    {
      var vm = new SearchVm { Q = q ?? "" };

      if (!string.IsNullOrWhiteSpace(q))
      {
        var sw = Stopwatch.StartNew();
        vm.Results = _index.Search(q, top: 50);
        sw.Stop();
        vm.ElapsedMs = sw.ElapsedMilliseconds;
      }

      return View(vm);
    }

    // POST /search/rebuild  -> reconstruye el índice desde /wwwroot/data
    [HttpPost("rebuild")]
    public IActionResult Rebuild()
    {
      _index.RebuildFromData();
      return Ok(new { ok = true, rebuilt = true });
    }
  }
}
