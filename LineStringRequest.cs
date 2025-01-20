using Newtonsoft.Json;

namespace BayernatlasHeightmapper;

public class LineStringRequest
{
    [JsonProperty("type")]
    public static string Type => "LineString";

    [JsonProperty("coordinates")]
    public int[][] Coordinates { get; set; } = [];

    // The server wants an array of two-element arrays, so we need to convert
    // our ValueTuples to this format.
    [JsonIgnore]
    public (int, int)[] CoordinateTuples
    {
        get => [.. Coordinates.Select(entry => (entry[0], entry[1]))];
        set => Coordinates = [.. value.Select<(int, int), int[]>(entry => [entry.Item1, entry.Item2])];
    }
}
