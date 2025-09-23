using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WebLibrary.App.Services {
  public class TextUtils {
    public HashSet<string> Stopwords = new(new [] {
      "de","la","que","el","en","y","a","los","del","se","las","por","un","para","con","no","una","su","al","lo","como","más","pero","sus","le","ya","o",
      "fue","ha","sí","porque","entre","cuando","muy","sin","sobre","también","me","hasta","hay","donde","quien","desde","todo","nos","durante","todos",
      "uno","les","ni","contra","otros","ese","eso","ante","ellos","e","esto","mí","antes","algunos","qué","unos","yo","otro","otras","otra","él",
    }, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Tokenize(string text) =>
      Regex.Matches(text ?? string.Empty, "[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{4,}")
           .Select(m => m.Value.ToLowerInvariant())
           .Where(t => !Stopwords.Contains(t));
  }

  public class KeywordExtractor {
    private readonly TextUtils _utils;
    public KeywordExtractor() : this(new TextUtils()) {}
    public KeywordExtractor(TextUtils utils) { _utils = utils; }

    public List<string> ExtractTop(string text, int topN = 12) {
      var freq = _utils.Tokenize(text).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
      return freq.OrderByDescending(kv => kv.Value).Take(topN).Select(kv => kv.Key).ToList();
    }
  }

  public class TextSummarizer {
    public string Summarize(string text, int maxSentences = 3) {
      if (string.IsNullOrWhiteSpace(text)) return "";
      var parts = Regex.Split(text.Trim(), @"(?<=[\.\!\?])\s+").Where(s => s.Length > 0).Take(maxSentences);
      return string.Join(" ", parts);
    }
  }
}
