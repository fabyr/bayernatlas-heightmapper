using Newtonsoft.Json;

namespace BayernatlasHeightmapper;

public class HeightPointData
{
    [JsonProperty("dist")]
    public float Distance { get; set; }

    [JsonProperty("alts")]
    public AltitudeData? Altitude { get; set; }

    [JsonProperty("easting")]
    public float Easting { get; set; }

    [JsonProperty("northing")]
    public float Northing { get; set; }
}
