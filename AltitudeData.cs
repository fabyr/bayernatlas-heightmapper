using Newtonsoft.Json;

namespace BayernatlasHeightmapper;

public class AltitudeData
{
    [JsonProperty("COMB")]
    public float Value { get; set; }
}
