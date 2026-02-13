using System.Text;

namespace STIGForge.Core.Utilities;

public static class CsvUtility
{
  public static string[] ParseLine(string line)
  {
    if (line == null)
      return Array.Empty<string>();

    var fields = new List<string>();
    var current = new StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < line.Length; i++)
    {
      var ch = line[i];
      if (ch == '"')
      {
        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
        {
          current.Append('"');
          i++;
        }
        else
        {
          inQuotes = !inQuotes;
        }
      }
      else if (ch == ',' && !inQuotes)
      {
        fields.Add(current.ToString());
        current.Clear();
      }
      else
      {
        current.Append(ch);
      }
    }

    fields.Add(current.ToString());
    return fields.ToArray();
  }
}
