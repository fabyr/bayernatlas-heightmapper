using System.Text;
using System.Globalization;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using System.Net.Mime;

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

    private static (int, int) UnflattenIndex(int index, int height)
    {
        int x = index / height;
        int y = index % height;
        if (x % 2 != 0)
        {
            y = height - y - 1;
        }
        return (x, y);
    }

    public static async Task Main(string[] args)
    {
        async Task PrintHelp()
        {
            const string programName = "bayernatlas-heightmapper";
            await Console.Error.WriteLineAsync($"Usage: {programName} [-h] [-v] [-S] [-r] [-u <units>] [-s <size>] [-t <step>] centerX centerY [outputFile]");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync("Download heightmap images or heightmap values from Bayernatlas.");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync("centerX: Longitude in GK4-Coordinates.");
            await Console.Error.WriteLineAsync("centerY: Latitude in GK4-Coordinates.");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync("Options:");
            await Console.Error.WriteLineAsync(" -h, --help\tDisplay this help");
            await Console.Error.WriteLineAsync(" -u, --units\tUnits per pixel (meters). Default is 20");
            await Console.Error.WriteLineAsync(" -v, --verbose\tPrint occurring exceptions");
            await Console.Error.WriteLineAsync(" -S, --simple\tUse a simplified downloading algorithm (not necessary in most cases)");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(" -s <size>,\tSpecify size (two-value tuple) in GK4 units in each direction from the center.");
            await Console.Error.WriteLineAsync(" --size <size>\tExample: 12000,12000");
            await Console.Error.WriteLineAsync("\t\tDefault: 5000,5000");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(" -x <by>,\tScale the resulting image by that factor");
            await Console.Error.WriteLineAsync(" --scale <by>\tExample: 5");
            await Console.Error.WriteLineAsync("\t\tDefault: 1");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(" -r, --raw\tDon't render an image; output raw numeric height values instead");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(" -t <step>,\tInstead of saving the image as a heightmap, draw a simplified topographical map");
            await Console.Error.WriteLineAsync(" --topo <step>\tThe lines will be separated by 'step' meters of height.");
            await Console.Error.WriteLineAsync("\t\tExample: 22.5");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync("outputFile:");
            await Console.Error.WriteLineAsync("\tWrite the output to a file.");
            await Console.Error.WriteLineAsync("\tWhen not using '-r' or '--raw', this must be set.");
        }

        if (args.Length == 0)
        {
            await PrintHelp();
            return;
        }

        const int requestComplexBlockSize = 5000;
        const string url = "https://geoportal.bayern.de/ba-backend/dgm/profile/";

        bool onlySaveRaw = false;
        bool topographical = false;
        bool verbose = false;
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
                        case "verbose" or "v":
                            verbose = true;
                            break;
                        case "simple" or "S":
                            requestComplex = false;
                            break;
                        case "raw" or "r":
                            onlySaveRaw = true;
                            break;
                        case "topo" or "t":
                            if (i == args.Length - 1)
                            {
                                await Console.Error.WriteLineAsync("'topo' requires a value afterwards. Example: --topo 15");
                                return;
                            }
                            else
                            {
                                string topoArg = args[++i];
                                if (!float.TryParse(topoArg, out topographicalLineDistance))
                                {
                                    await Console.Error.WriteLineAsync($"Invalid value '{topoArg}'. Valid Example: 15");
                                    return;
                                }
                                topographical = true;
                            }
                            break;
                        case "size" or "s":
                            if (i == args.Length - 1)
                            {
                                await Console.Error.WriteLineAsync("'size' requires a size-value afterwards. Example: --size 12000,12000");
                                return;
                            }
                            else
                            {
                                string sizeArg = args[++i];
                                string[] parts = sizeArg.Split(',');
                                if (parts.Length != 2
                                    || !int.TryParse(parts[0], out sizeX)
                                    || !int.TryParse(parts[1], out sizeY))
                                {
                                    await Console.Error.WriteLineAsync($"Invalid size value '{sizeArg}'. Valid Example: 12000,12000");
                                    return;
                                }
                            }
                            break;
                        case "units" or "u":
                            if (i == args.Length - 1)
                            {
                                await Console.Error.WriteLineAsync("'units' requires a value afterwards. Example: --units 50");
                                return;
                            }
                            else
                            {
                                string unitsArg = args[++i];
                                if (!int.TryParse(unitsArg, out step))
                                {
                                    await Console.Error.WriteLineAsync($"Invalid units value '{unitsArg}'. It must be a whole number.");
                                    return;
                                }
                            }
                            break;
                        case "scale" or "x":
                            if (i == args.Length - 1)
                            {
                                await Console.Error.WriteLineAsync("'scale' requires a scaling-value afterwards. Example: --scale 5");
                                return;
                            }
                            else
                            {
                                string scaleArg = args[++i];
                                if (!float.TryParse(scaleArg, out imageScale))
                                {
                                    await Console.Error.WriteLineAsync($"Invalid scale value '{scaleArg}'. Valid Example: 5");
                                    return;
                                }
                            }
                            break;
                        default:
                            await Console.Error.WriteLineAsync($"Unknown argument '{innerArg}'.");
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
                            await Console.Error.WriteLineAsync($"Invalid value '{arg}' for centerX. It must be a whole number.");
                            return;
                        }
                        break;
                    case 1:
                        if (!int.TryParse(arg, out centerY))
                        {
                            await Console.Error.WriteLineAsync($"Invalid value '{arg}' for centerY. It must be a whole number.");
                            return;
                        }
                        break;
                    case 2:
                        outputFile = arg;
                        break;
                    default:
                        await Console.Error.WriteLineAsync($"Too many positional arguments. (at '{arg}')");
                        return;
                }
                positionalArgumentPosition++;
            }
        }

        // Validate arguments
        if (positionalArgumentPosition < 2)
        {
            await Console.Error.WriteLineAsync("Missing required 'centerX' and 'centerY' values.");
            return;
        }

        if (onlySaveRaw && topographical)
        {
            await Console.Error.WriteLineAsync("Raw mode is incompatible with topographical mode.");
            return;
        }

        if (!onlySaveRaw && outputFile == null)
        {
            await Console.Error.WriteLineAsync("You must specify an output file at the end when not using '-r' or '--raw'.");
            return;
        }

        // Display some information about the upcoming download
        await Console.Error.WriteLineAsync($"Using {(requestComplex ? "complex" : "simple")} request algorithm");
        await Console.Error.WriteLineAsync($"Output will be saved to {outputFile ?? "stdout"}");
        await Console.Error.WriteLineAsync($"Output is {(onlySaveRaw ? "a list of raw height values" : topographical ? $"a topographical map with steps of {topographicalLineDistance}" : "an image")}");
        await Console.Error.WriteLineAsync($"Size: {sizeX}, {sizeY}");
        await Console.Error.WriteLineAsync($"Additional scaling afterwards: {imageScale}");
        await Console.Error.WriteLineAsync($"Units per height-point: {step}");
        await Console.Error.WriteLineAsync();

        int width = sizeX * 2 / step, height = sizeY * 2 / step;
        float[,] heightmap = new float[width, height];

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
            client.Timeout = TimeSpan.FromSeconds(60);

            if (requestComplex)
            {
                /* "Complex" algorithm does not fetch the image line by line.
                 * Rather, it builds a path consisting of a maximum of requestComplexBlockSize
                 * points in a "snake"-like pattern, and repeats this until the entire image is fetched.
                 * Fewer requests will be sent with smaller images.
                 * For bigger images it is mandatory as a single line could hit a server-side limit
                 * with the other, simpler line-by-line algorithm.
                 */
                List<(int, int)> points = [];

                int total = width * height;
                int blockCount = (int)MathF.Ceiling(total / (float)requestComplexBlockSize);
                for (int i = 0; i < total; i++)
                {
                    var (pointX, pointY) = UnflattenIndex(i, height);
                    points.Add((XIndexToGK4(pointX), YIndexToGK4(pointY)));

                    // Once the threshold is reached (requestComplexBlockSize) or we reached the end,
                    // we send a request to the server
                    if (points.Count == requestComplexBlockSize || i == total - 1)
                    {
                        int blockNumber = (int)MathF.Round(i / (float)requestComplexBlockSize);
                        await Console.Error.WriteLineAsync($"Processing block {blockNumber} of {blockCount}");

                        string json = JsonConvert.SerializeObject(new LineStringRequest()
                        {
                            CoordinateTuples = [.. points]
                        });

                        StringContent content = new(json, Encoding.UTF8, MediaTypeNames.Application.Json);
                        HttpRequestMessage request = new(HttpMethod.Post, url)
                        {
                            Content = content
                        };

                        try
                        {
                            // Send the points to the server
                            HttpResponseMessage response = await client.SendAsync(request);
                            string responseContent = await response.Content.ReadAsStringAsync();

                            // Process received altitude values
                            LineStringResponse? responseObject = JsonConvert.DeserializeObject<LineStringResponse>(responseContent);
                            if (responseObject != null)
                            {
                                int count = responseObject.Heights.Length;

                                for (int k = 0; k < count; k++)
                                {
                                    float value = responseObject.Heights[k].Altitude?.Value ?? 0;

                                    var (x, y) = UnflattenIndex((blockNumber - 1) * requestComplexBlockSize + k, height);

                                    if (x >= width)
                                        break;

                                    heightmap[x, y] = value;
                                }
                            }
                        }
                        catch (JsonException je)
                        {
                            await Console.Error.WriteLineAsync($"Warning: Server returned malformed json for block {blockNumber}");
                            if (verbose)
                            {
                                await Console.Error.WriteLineAsync(je.ToString());
                            }
                        }
                        catch (Exception e)
                        {
                            await Console.Error.WriteLineAsync($"Error: Request to server failed for block {blockNumber}");
                            if (verbose)
                            {
                                await Console.Error.WriteLineAsync(e.ToString());
                            }
                        }

                        points.Clear();
                    }
                }
            }
            else
            {
                List<(int, int)> points = [];

                for (int x = 0; x < width; x++)
                {
                    await Console.Error.WriteLineAsync($"Processing line {x + 1} of {width}");

                    for (int y = 0; y < height; y++)
                    {
                        points.Add((XIndexToGK4(x), YIndexToGK4(y)));
                    }

                    string json = JsonConvert.SerializeObject(new LineStringRequest()
                    {
                        CoordinateTuples = [.. points]
                    });

                    points.Clear();

                    StringContent content = new(json, Encoding.UTF8, MediaTypeNames.Application.Json);
                    HttpRequestMessage request = new(HttpMethod.Post, url)
                    {
                        Content = content
                    };

                    try
                    {
                        // Send the points to the server
                        HttpResponseMessage response = await client.SendAsync(request);
                        string responseContent = await response.Content.ReadAsStringAsync();

                        // process received altitude values
                        LineStringResponse? responseObject = JsonConvert.DeserializeObject<LineStringResponse>(responseContent);
                        if (responseObject != null)
                        {
                            int count = responseObject.Heights.Length;

                            for (int i = 0; i < count; i++)
                            {
                                float value = responseObject.Heights[i].Altitude?.Value ?? 0;
                                heightmap[x, (int)(i / (float)count * height)] = value;
                            }
                        }
                    }
                    catch (JsonException je)
                    {
                        await Console.Error.WriteLineAsync($"Warning: Server returned malformed json for line {x + 1}");
                        if (verbose)
                        {
                            await Console.Error.WriteLineAsync(je.ToString());
                        }
                    }
                    catch (Exception e)
                    {
                        await Console.Error.WriteLineAsync($"Error: Request to server failed for line {x + 1}");
                        if (verbose)
                        {
                            await Console.Error.WriteLineAsync(e.ToString());
                        }
                    }
                }
            }
        }

        // Find min and max height values.
        // Note: when an image has parts outside of bavaria,
        // the server will return a height of zero or no height data, which will mess up the result.
        // Since in reality there is no point in bavaria at sea level, we can just filter that out.
        float min = heightmap.Cast<float>().Where(x => x > 1).Min();
        float max = heightmap.Cast<float>().Max();

        // Output some useful information
        await Console.Error.WriteLineAsync("Finished!");
        await Console.Error.WriteLineAsync();
        await Console.Error.WriteLineAsync("Final Parameters:");
        await Console.Error.WriteLineAsync($"Minimum Height: {min}");
        await Console.Error.WriteLineAsync($"Maximum Height: {max}");
        await Console.Error.WriteLineAsync($"Size-X: {sizeX}");
        await Console.Error.WriteLineAsync($"Size-Y: {sizeY}");
        await Console.Error.WriteLineAsync($"Center-X: {centerX}");
        await Console.Error.WriteLineAsync($"Center-Y: {centerY}");
        await Console.Error.WriteLineAsync($"Units per pixel: {step / imageScale}");
        await Console.Error.WriteLineAsync($"Final image size: {(int)(width * imageScale)}x{(int)(height * imageScale)} pixels");
        await Console.Error.WriteLineAsync();
        await Console.Error.WriteLineAsync($"Saving...");

        if (onlySaveRaw)
        {
            StringBuilder raw = new();
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x != 0)
                        raw.Append(' ');
                    raw.Append(heightmap[x, y]);
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
            float[,] heightsScaled = new float[(int)(width * imageScale), (int)(height * imageScale)];
            int originalWidth = width,
                originalHeight = height;
            width = heightsScaled.GetLength(0);
            height = heightsScaled.GetLength(1);
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    float fX = x / imageScale;
                    float fY = y / imageScale;
                    int iX = (int)MathF.Min((int)fX, originalWidth - 2);
                    int iY = (int)MathF.Min((int)fY, originalHeight - 2);
                    float fracX = fX - iX;
                    float fracY = fY - iY;
                    // bilinear interpolation
                    float interpolatedValue = (1 - fracX) *
                                            ((1 - fracY) * heightmap[iX, iY] +
                                            fracY * heightmap[iX, iY + 1]) +
                                        fracX *
                                            ((1 - fracY) * heightmap[iX + 1, iY] +
                                            fracY * heightmap[iX + 1, iY + 1]);
                    heightsScaled[x, y] = interpolatedValue;
                }

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
            using var bmp = new Image<Rgb24>(width, height);
            for (int x = 0; x < width - 1; x++)
                for (int y = 0; y < height - 1; y++)
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

                        // if for some reason we could not determine a color, we just skip it
                        if (closestIndex == -1)
                        {
                            continue;
                        }

                        float interpolatedValue = MathF.Max(0, MathF.Min(1, Map(v, colorMap[closestIndex].Item1, colorMap[closestIndex + 1].Item1, 0, 1)));
                        color = LerpRgb24(colorMap[closestIndex].Item2, colorMap[closestIndex + 1].Item2, interpolatedValue);
                    }
                    bmp[x, height - y - 1] = color;
                }
            await bmp.SaveAsPngAsync(outputFile);
        }
        else // Save normal heightmap image
        {
            using var bmp = new Image<Rgb24>(width, height);
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    float value = Map(heightmap[x, y], min, max, 0, 255);
                    byte byteValue = (byte)value;
                    bmp[x, height - y - 1] = new Rgb24(byteValue, byteValue, byteValue);
                }

            if (imageScale != 1f)
                bmp.Mutate(x => x.Resize((int)(width * imageScale), (int)(height * imageScale)));
            await bmp.SaveAsPngAsync(outputFile);
        }

        await Console.Error.WriteLineAsync("Done!");
    }
}
