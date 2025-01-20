using Newtonsoft.Json;

namespace BayernatlasHeightmapper;

public class LineStringResponse
{
    [JsonProperty("heights")]
    public HeightPointData[] Heights { get; set; } = [];
}
