using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace JobHunter.AI.Abstractions;

public static class JobAnalysisJson
{
    public static JsonSerializerOptions CreateSerializerOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };
}
