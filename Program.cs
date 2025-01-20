using System.Text;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace BayernatlasHeightmapper;

public class Program
{
    private static float Lerp(float firstFloat, float secondFloat, float by)
    {
        return firstFloat * (1 - by) + secondFloat * by;
    }

    private static Rgb24 LerpRgb24(Rgb24 a, Rgb24 b, float by)
    {
        return new Rgb24(
            (byte)(int)Lerp(a.R, b.R, by),
            (byte)(int)Lerp(a.G, b.G, by),
            (byte)(int)Lerp(a.B, b.B, by)
        );
    }

    private static float Map(float x, float inMin, float inMax, float outMin, float outMax)
    {
        return (x - inMin) * (outMax - outMin) / (inMax - inMin) + outMin;
    }

    public static async Task Main(string[] args)
    {
        async Task PrintHelp()
        {
            const string programName = "bayernatlas-heightmapper";
            await Console.Out.WriteLineAsync($"Usage: {programName} [-h] [-S] [-r] [-u <units>] [-s <size>] [-t <step>] centerX centerY [outputFile]");
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync("Download heightmap images or heightmap values from Bayernatlas.");
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync("Options:");
            await Console.Out.WriteLineAsync(" -h, --help\tDisplay this help");
            await Console.Out.WriteLineAsync(" -u, --units\tUnits per pixel (meters). Default is 20");
            await Console.Out.WriteLineAsync(" -S, --simple\tUse a simplified downloading algorithm (not necessary in most cases)");
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync(" -s <size>,\tSpecify size (two-value tuple) in GK4 units in each direction from the center.");
            await Console.Out.WriteLineAsync(" --size <size>\tExample: 12000,12000");
            await Console.Out.WriteLineAsync("\t\tDefault: 5000,5000");
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync(" -x <by>,\tScale the resulting image by that factor");
            await Console.Out.WriteLineAsync(" --scale <by>\tExample: 5");
            await Console.Out.WriteLineAsync("\t\tDefault: 1");
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync(" -r, --raw\tDon't render an image; output raw numeric height values instead");
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync(" -t <step>,\tInstead of saving the image as a heightmap, draw a simplified topographical map");
            await Console.Out.WriteLineAsync(" --topo <step>\tThe lines will be separated by 'step' meters of height.");
            await Console.Out.WriteLineAsync("\t\tExample: 22.5");
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync("outputFile:");
            await Console.Out.WriteLineAsync("\tWrite the output to a file.");
            await Console.Out.WriteLineAsync("\tWhen not using '-r' or '--raw', this must be set.");
        }

        if (args.Length == 0)
        {
            await PrintHelp();
            return;
        }

        const int requestComplexPointCount = 5000;
        const string url = "https://geoportal.bayern.de/ba-backend/dgm/profile/";

        bool onlySaveRaw = false;
        bool topographical = false;
        float topographicalLineDistance = float.NaN;
        float imageScale = 1f;
        bool requestComplex = true;

        // GK4
        int centerX = 0, centerY = 0;

        // GK4-Points in every direction
        int sizeX = 5000, sizeY = 5000;

        // GK4-Units per pixel
        int step = 20;

        string? outputFile = null;

        // Parse arguments
        int positionalArgumentPosition = 0;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith('-'))
            {
                string[] innerArgList;
                if (!arg.StartsWith("--"))
                    // a single dash can have any amount of single character arguments
                    innerArgList = [.. arg[1..].Select(x => x.ToString())];
                else
                    innerArgList = [arg[2..]];

                foreach (string innerArg in innerArgList)
                    switch (innerArg)
                    {
                        case "help" or "h":
                            await PrintHelp();
                            return;
                        case "simple" or "S":
                            requestComplex = false;
                            break;
                        case "raw" or "r":
                            onlySaveRaw = true;
                            break;
                        case "topo" or "t":
                            if (i == args.Length - 1)
                            {
                                await Console.Out.WriteLineAsync("'topo' requires a value afterwards. Example: --topo 15");
                                return;
                            }
                            else
                            {
                                string topoArg = args[++i];
                                if (!float.TryParse(topoArg, out topographicalLineDistance))
                                {
                                    await Console.Out.WriteLineAsync($"Invalid value '{topoArg}'. Valid Example: 15");
                                    return;
                                }
                                topographical = true;
                            }
                            break;
                        case "size" or "s":
                            if (i == args.Length - 1)
                            {
                                await Console.Out.WriteLineAsync("'size' requires a size-value afterwards. Example: --size 12000,12000");
                                return;
                            }
                            else
                            {
                                string sizeArg = args[++i];
                                string[] parts = sizeArg.Split(',');
                                if (parts.Length != 2 || !int.TryParse(parts[0], out sizeX)
                                    || !int.TryParse(parts[1], out sizeY))
                                {
                                    await Console.Out.WriteLineAsync($"Invalid size value '{sizeArg}'. Valid Example: 12000,12000");
                                    return;
                                }
                            }
                            break;
                        case "units" or "u":
                            if (i == args.Length - 1)
                            {
                                await Console.Out.WriteLineAsync("'units' requires a value afterwards. Example: --units 50");
                                return;
                            }
                            else
                            {
                                string unitsArg = args[++i];
                                if (!int.TryParse(unitsArg, out step))
                                {
                                    await Console.Out.WriteLineAsync($"Invalid units value '{unitsArg}'. It must be a whole number.");
                                    return;
                                }
                            }
                            break;
                        case "scale" or "x":
                            if (i == args.Length - 1)
                            {
                                await Console.Out.WriteLineAsync("'scale' requires a scaling-value afterwards. Example: --scale 5");
                                return;
                            }
                            else
                            {
                                string scaleArg = args[++i];
                                if (!float.TryParse(scaleArg, out imageScale))
                                {
                                    await Console.Out.WriteLineAsync($"Invalid scale value '{scaleArg}'. Valid Example: 5");
                                    return;
                                }
                            }
                            break;
                        default:
                            await Console.Out.WriteLineAsync($"Unknown argument '{innerArg}'.");
                            return;
                    }
            }
            else // Positional arguments
            {
                switch (positionalArgumentPosition)
                {
                    case 0:
                        if (!int.TryParse(arg, out centerX))
                        {
                            await Console.Out.WriteLineAsync($"Invalid value '{arg}' for centerX. It must be a whole number.");
                            return;
                        }
                        break;
                    case 1:
                        if (!int.TryParse(arg, out centerY))
                        {
                            await Console.Out.WriteLineAsync($"Invalid value '{arg}' for centerY. It must be a whole number.");
                            return;
                        }
                        break;
                    case 2:
                        outputFile = arg;
                        break;
                    default:
                        await Console.Out.WriteLineAsync($"Too many positional arguments. (at '{arg}')");
                        return;
                }
                positionalArgumentPosition++;
            }
        }

        // Validate arguments
        if (positionalArgumentPosition < 2)
        {
            await Console.Out.WriteLineAsync("Missing required 'centerX' and 'centerY' values.");
            return;
        }

        if (onlySaveRaw && topographical)
        {
            await Console.Out.WriteLineAsync("Raw mode is incompatible with topographical mode.");
            return;
        }

        if (!onlySaveRaw && outputFile == null)
        {
            await Console.Out.WriteLineAsync("You must specify an output file at the end when not using '-r' or '--raw'.");
            return;
        }

        await Console.Out.WriteLineAsync($"Using {(requestComplex ? "complex" : "simple")} request algorithm");
        await Console.Out.WriteLineAsync($"Output will be saved to {outputFile ?? "stdout"}");
        await Console.Out.WriteLineAsync($"Output is {(onlySaveRaw ? "a list of raw height values" : topographical ? $"a topographical map with steps of {topographicalLineDistance}" : "an image")}");
        await Console.Out.WriteLineAsync($"Size: {sizeX}, {sizeY}");
        await Console.Out.WriteLineAsync($"Additional scaling afterwards: {imageScale}");
        await Console.Out.WriteLineAsync($"Units per height-point: {step}");
        await Console.Out.WriteLineAsync();

        int w = sizeX * 2 / step, h = sizeY * 2 / step;

        float[,] heights = new float[w, h];

        // Converting a floating point number to string should use '.' as the decimal point.
        // If a different culture is set by the system, this might not be the case.
        // So we set the culture explicitly.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        int XIndexToGK4(int value)
        {
            return value * step + (centerX - sizeX);
        }

        int YIndexToGK4(int value)
        {
            return value * step + (centerY - sizeY);
        }

        using (HttpClient client = new())
        {
            if (requestComplex)
            {
                /* "Complex" algorithm does not fetch the image line by line.
                 * Rather, it builds a path consisting of a maximum of requestComplexPointCount
                 * points in a "snake"-like pattern. Until the entire image is fetched.
                 * Fewer requests will be made with smaller images.
                 * For bigger images it is mandatory as a single line could hit a server-side limit
                 * with the other, more simple line-by-line algorithm.
                 */
                StringBuilder json = new();

                int x = 0, y = 0, yDirection = 1, xarr = 0, yarr = 0, yarrDirection = 1;
                int blockCount = (int)Math.Ceiling(w * h / (float)requestComplexPointCount);
                bool notPrepend = true;
                int lastRequest = 0, blockAt = 0;
                for (int i = 0; i <= w * h; i++)
                {
                    // Once the threshold is reached (requestComplexPointCount) or we reached the end
                    // we send a request to the server
                    if (i % requestComplexPointCount == 0 || i == w * h)
                    {
                        if (i != 0)
                        {
                            await Console.Out.WriteLineAsync($"Processing Block {blockAt + 1} of {blockCount}");
                            json.Append("]}");

                            StringContent content = new(json.ToString(), Encoding.UTF8, "application/json");
                            HttpRequestMessage request = new(HttpMethod.Post, url)
                            {
                                Content = content
                            };
                            HttpResponseMessage response = await client.SendAsync(request);
                            string responseContent = await response.Content.ReadAsStringAsync();
                            using (JsonTextReader reader = new(new StringReader(responseContent)))
                            {
                                JObject responseObject;
                                JToken? heightArray = null;

                                // Ignore a broken response
                                try
                                {
                                    responseObject = (JObject)JToken.ReadFrom(reader);
                                    heightArray = responseObject["heights"];
                                }
                                catch { }

                                var values = heightArray?.Values<JToken>();
                                for (int k = 0; k < i - lastRequest; k++)
                                {
                                    float v = 0;
                                    if (values != null && k < values.Count())
                                    {
                                        v = values.ElementAt(k)?.Value<JToken>("alts")?.Value<float>("COMB") ?? 0;
                                    }

                                    heights[xarr, yarr] = v;
                                    yarr += yarrDirection;
                                    if (yarr == h || yarr == -1)
                                    {
                                        yarr += -yarrDirection;
                                        yarrDirection = -yarrDirection;
                                        xarr++;
                                    }
                                }
                            }
                            lastRequest = i;
                            blockAt++;
                        }

                        json.Clear();
                        json.Append("{\"type\":\"LineString\",\"coordinates\":[");
                        notPrepend = true;
                    }

                    // Continually append points to the json request
                    if (i < w * h)
                    {
                        int gkX = XIndexToGK4(x), gkY = YIndexToGK4(y);
                        if (notPrepend)
                        {
                            notPrepend = false;
                        }
                        else
                            json.Append(',');
                        json.Append($"[{gkX}, {gkY}]");
                        y += yDirection;
                        if (y == h || y == -1)
                        {
                            y += -yDirection;
                            yDirection = -yDirection;
                            x++;
                        }
                    }
                }
            }
            else
            {
                for (int x = 0; x < heights.GetLength(0); x++)
                {
                    await Console.Out.WriteLineAsync($"Processing Block {x + 1} of {heights.GetLength(0)}");

                    StringBuilder json = new();
                    json.Append("{\"type\":\"LineString\",\"coordinates\":[");
                    for (int y = 0; y < heights.GetLength(1); y++)
                    {
                        int gkX = XIndexToGK4(x), gkY = YIndexToGK4(y);
                        if (y != 0)
                            json.Append(',');
                        json.Append($"[{gkX}, {gkY}]");
                    }
                    json.Append("]}");

                    StringContent content = new(json.ToString(), Encoding.UTF8, "application/json");
                    HttpRequestMessage request = new(HttpMethod.Post, url)
                    {
                        Content = content
                    };

                    // Send the constructed json request
                    HttpResponseMessage response = await client.SendAsync(request);
                    string responseContent = await response.Content.ReadAsStringAsync();
                    using JsonTextReader reader = new(new StringReader(responseContent));

                    JObject responseObject = (JObject)JToken.ReadFrom(reader);
                    JToken? heightArray = responseObject["heights"];
                    if (heightArray == null)
                        continue;
                    int c = 0;
                    int count = heightArray.Count();

                    foreach (JToken? pt in heightArray.Values<JToken>())
                    {
                        float value = pt?.Value<JToken>("alts")?.Value<float>("COMB") ?? 0;
                        heights[x, (int)(c / (float)count * heights.GetLength(1))] = value;

                        c++;
                    }
                }
            }
        }

        // Find min and max height values.
        // Note: when an image has parts outside of bavaria,
        // the server will return a height of zero, which will mess up the result.
        // Since in reality there is no point in bavaria at sea level, we can just
        // filter that out
        float min = heights.Cast<float>().Where(x => x > 1).Min();
        float max = heights.Cast<float>().Max();

        // Output some useful information
        await Console.Out.WriteLineAsync("Finished!");
        await Console.Out.WriteLineAsync();
        await Console.Out.WriteLineAsync("Final Parameters:");
        await Console.Out.WriteLineAsync($"Minimum Height: {min}");
        await Console.Out.WriteLineAsync($"Maximum Height: {max}");
        await Console.Out.WriteLineAsync($"Size-X: {sizeX}");
        await Console.Out.WriteLineAsync($"Size-Y: {sizeY}");
        await Console.Out.WriteLineAsync($"Center-X: {centerX}");
        await Console.Out.WriteLineAsync($"Center-Y: {centerY}");
        await Console.Out.WriteLineAsync($"Units per pixel: {step / imageScale}");
        await Console.Out.WriteLineAsync($"Final image size: {(int)(w * imageScale)}x{(int)(h * imageScale)} pixels");
        await Console.Out.WriteLineAsync();
        await Console.Out.WriteLineAsync($"Saving...");

        if (onlySaveRaw)
        {
            StringBuilder raw = new();
            for (int y = heights.GetLength(1) - 1; y >= 0; y--)
            {
                for (int x = 0; x < heights.GetLength(0); x++)
                {
                    if (x != 0)
                        raw.Append(' ');
                    raw.Append(heights[x, y]);
                }
                raw.AppendLine();
            }
            if (outputFile == null) // Write to stdout if no output file is set
                await Console.Out.WriteLineAsync(raw.ToString());
            else
                await File.WriteAllTextAsync(outputFile, raw.ToString());
        }
        else if (topographical)
        {
            float[,] heightsScaled = new float[(int)(heights.GetLength(0) * imageScale), (int)(heights.GetLength(1) * imageScale)];
            for (int x = 0; x < heightsScaled.GetLength(0); x++)
                for (int y = 0; y < heightsScaled.GetLength(1); y++)
                {
                    float fX = x / imageScale;
                    float fY = y / imageScale;
                    int iX = (int)MathF.Min((int)fX, heights.GetLength(0) - 2);
                    int iY = (int)MathF.Min((int)fY, heights.GetLength(1) - 2);
                    float fracX = fX - iX;
                    float fracY = fY - iY;
                    // bilinear interpolation
                    float interpolatedValue = (1 - fracX) *
                                            ((1 - fracY) * heights[iX, iY] +
                                            fracY * heights[iX, iY + 1]) +
                                        fracX *
                                            ((1 - fracY) * heights[iX + 1, iY] +
                                            fracY * heights[iX + 1, iY + 1]);
                    heightsScaled[x, y] = interpolatedValue;
                }

            w = heightsScaled.GetLength(0);
            h = heightsScaled.GetLength(1);

            // mapping of height to color
            (float, Rgb24)[] colorMap = [
                (-1000f, new Rgb24(0, 0, 0)),
                (0f, new Rgb24(120, 170, 255)),
                (200f, new Rgb24(240, 255, 200)),
                (350f, new Rgb24(170, 255, 120)),
                (450f, new Rgb24(170, 200, 50)),
                (600f, new Rgb24(140, 160, 50)),
                (1000f, new Rgb24(255, 200, 120)),
                (2000f, new Rgb24(255, 240, 200)),
                (10000f, new Rgb24(255, 255, 255)),
            ];

            Rgb24 lineColor = new(0, 0, 0);
            using var bmp = new Image<Rgb24>(w, h);
            for (int x = 0; x < heightsScaled.GetLength(0) - 1; x++)
                for (int y = 0; y < heightsScaled.GetLength(1) - 1; y++)
                {
                    float heightA = heightsScaled[x, y];
                    float heightB = heightsScaled[x + 1, y];
                    float heightC = heightsScaled[x, y + 1];
                    float heightD = heightsScaled[x + 1, y + 1];
                    int lineIndexA = (int)(heightA / topographicalLineDistance);
                    int lineIndexB = (int)(heightB / topographicalLineDistance);
                    int lineIndexC = (int)(heightC / topographicalLineDistance);
                    int lineIndexD = (int)(heightD / topographicalLineDistance);
                    bool isSet = lineIndexA != lineIndexB || lineIndexB != lineIndexC || lineIndexC != lineIndexD;
                    Rgb24 color = lineColor;
                    if (!isSet) // if it's not a line
                    {
                        float v = heightsScaled[x, y];
                        int closestIndex = -1;
                        for (int i = 0; i < colorMap.Length; i++)
                        {
                            if (colorMap[i].Item1 > v)
                            {
                                closestIndex = i - 1;
                                break;
                            }
                        }

                        // For some reason we could not determine a color.
                        if (closestIndex == -1)
                        {
                            continue;
                        }

                        float interpolatedValue = MathF.Max(0, MathF.Min(1, Map(v, colorMap[closestIndex].Item1, colorMap[closestIndex + 1].Item1, 0, 1)));
                        color = LerpRgb24(colorMap[closestIndex].Item2, colorMap[closestIndex + 1].Item2, interpolatedValue);
                    }
                    bmp[x, h - y - 1] = color;
                }
            await bmp.SaveAsPngAsync(outputFile);
        }
        else // Save image
        {
            using var bmp = new Image<Rgb24>(w, h);
            for (int x = 0; x < heights.GetLength(0); x++)
                for (int y = 0; y < heights.GetLength(1); y++)
                {
                    float value = Map(heights[x, y], min, max, 0, 255);
                    byte byteValue = (byte)value;
                    bmp[x, h - y - 1] = new Rgb24(byteValue, byteValue, byteValue);
                }

            if (imageScale != 1f)
                bmp.Mutate(x => x.Resize((int)(w * imageScale), (int)(h * imageScale)));
            await bmp.SaveAsPngAsync(outputFile);
        }

        await Console.Out.WriteLineAsync("Done!");
        await Console.Out.WriteLineAsync();
    }
}
