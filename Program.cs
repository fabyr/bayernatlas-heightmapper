using System;
using System.Text;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BayernatlasHeightmapper
{
    public class Program
    {
        private static float Lerp(float firstFloat, float secondFloat, float by)
        {
            return firstFloat * (1 - by) + secondFloat * by;
        }

        private static float Map(float x, float in_min, float in_max, float out_min, float out_max)
        {
            return (x - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
        }

        public static async Task Main(string[] args)
        {
            async Task PrintHelp()
            {
                const string programName = "bayernatlas-heightmapper";
                await Console.Out.WriteLineAsync($"Usage: {programName} [-h] [-S] [-r] [-u <units>] [-s <size>] centerX centerY [outputFile]");
                await Console.Out.WriteLineAsync();
                await Console.Out.WriteLineAsync("Download heightmap images or heightmap values from Bayernatlas.");
                await Console.Out.WriteLineAsync();
                await Console.Out.WriteLineAsync("Options:");
                await Console.Out.WriteLineAsync(" -h, --help\tdisplay this help");
                await Console.Out.WriteLineAsync(" -u, --units\tunits per pixel (meters). Default is 20");
                await Console.Out.WriteLineAsync(" -S, --simple\tuse a simplified downloading algorithm (not necessary in most cases)");
                await Console.Out.WriteLineAsync();
                await Console.Out.WriteLineAsync(" -s <size>,\tspecify size (two-value tuple) in GK4 units in each direction from the center.");
                await Console.Out.WriteLineAsync(" --size <size>\tExample: 12000,12000");
                await Console.Out.WriteLineAsync("\t\tDefault: 5000,5000");
                await Console.Out.WriteLineAsync();
                await Console.Out.WriteLineAsync(" -r, --raw\tdon't render an image; output raw numeric height values instead");
                await Console.Out.WriteLineAsync();
                await Console.Out.WriteLineAsync("outputFile:");
                await Console.Out.WriteLineAsync("\tWrite the output to a file.");
                await Console.Out.WriteLineAsync("\tWhen not using '-r' or '--raw', this must be set.");
            }
            
            if(args.Length == 0)
            {
                await PrintHelp();
                return;
            }

            const int requestComplexPointCount = 5000;
            const string url = "https://geoportal.bayern.de/ba-backend/dgm/profile/";

            bool onlySaveRaw = false;
            bool requestComplex = true;

            // GK4
            int centerX = 0, centerY = 0;

            // GK4-Points in every direction
            int sizeX = 5000, sizeY = 5000;
            int step = 20;

            string? outputFile = null;

            // Parse arguments
            int positionalArgumentPosition = 0;
            for(int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if(arg.StartsWith("-"))
                {
                    string[] innerArgList;
                    if(!arg.StartsWith("--"))
                        // a single dash can have any amount of single character arguments
                        innerArgList = arg.Substring(1).ToCharArray().Select(x => x.ToString()).ToArray();
                    else
                        innerArgList = new string[1] { arg.Substring(2) };
                    
                    foreach(string innerArg in innerArgList)
                        switch(innerArg)
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
                            case "size" or "s":
                                if(i == args.Length - 1)
                                {
                                    await Console.Out.WriteLineAsync("'size' requires a size-value afterwards. Example: --size 12000,12000");
                                    return;
                                }
                                else
                                {
                                    string sizeArg = args[++i];
                                    string[] parts = sizeArg.Split(',');
                                    if(parts.Length != 2 || !int.TryParse(parts[0], out sizeX)
                                        || !int.TryParse(parts[1], out sizeY))
                                    {
                                        await Console.Out.WriteLineAsync($"Invalid size value '{sizeArg}'. Valid Example: 12000,12000");
                                        return;
                                    }
                                }
                                break;
                            case "units" or "u":
                                if(i == args.Length - 1)
                                {
                                    await Console.Out.WriteLineAsync("'units' requires a value afterwards. Example: --units 50");
                                    return;
                                }
                                else
                                {
                                    string unitsArg = args[++i];
                                    if(!int.TryParse(unitsArg, out step))
                                    {
                                        await Console.Out.WriteLineAsync($"Invalid units value '{unitsArg}'. It must be a whole number.");
                                        return;
                                    }
                                }
                                break;
                            default:
                                await Console.Out.WriteLineAsync($"Unknown argument '{innerArg}'.");
                                return;
                        }
                }
                else
                {
                    switch(positionalArgumentPosition)
                    {
                        case 0:
                            if(!int.TryParse(arg, out centerX))
                            {
                                await Console.Out.WriteLineAsync($"Invalid value '{arg}' for centerX. It must be a whole number.");
                                return;
                            }
                            break;
                        case 1:
                            if(!int.TryParse(arg, out centerY))
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
            if(positionalArgumentPosition < 2)
            {
                await Console.Out.WriteLineAsync("Missing required 'centerX' and 'centerY' values.");
                return;
            }

            if(!onlySaveRaw && outputFile == null)
            {
                await Console.Out.WriteLineAsync("You must specify an output file at the end when not using '-r' or '--raw'.");
                return;
            }

            await Console.Out.WriteLineAsync($"Using {(requestComplex ? "complex" : "simple")} request algorithm");
            await Console.Out.WriteLineAsync($"Output will be saved to {outputFile ?? "stdout"}");
            await Console.Out.WriteLineAsync($"Output is {(onlySaveRaw ? "a list of raw height values": "an image")}");
            await Console.Out.WriteLineAsync($"Size: {sizeX}, {sizeY}");
            await Console.Out.WriteLineAsync($"Units per pixel: {step}");
            await Console.Out.WriteLineAsync();
            
            int w = (sizeX * 2) / step, h = (sizeY * 2) / step;

            float[,] heights = new float[w, h];

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            int XIdx2GK4(int value)
            {
                return value * step + (centerX - sizeX);
            }

            int YIdx2GK4(int value)
            {
                return value * step + (centerY - sizeY);
            }
            
            HttpClient client = new HttpClient();

            if(requestComplex)
            {
                /* "Complex" algorithm does not fetch the image line by line.
                 * Rather, it builds a path consisting of a maximum of requestComplexPointCount
                 * points in a "snake"-like pattern. Until the entire image is fetched.
                 * Fewer requests will be made with smaller images.
                 * For bigger images it is mandatory as a single line could hit a server-side limit
                 * with the other, more simple line-by-line algorithm.
                 */
                StringBuilder json = new StringBuilder();
                
                int x = 0, y = 0, ydir = 1, xarr = 0, yarr = 0, yarrdir = 1;
                int blockCount = (int)Math.Ceiling(w * h / (float)requestComplexPointCount);
                bool notPrepend = true;
                int lastRequest = 0, blockAt = 0;
                for(int i = 0; i <= w * h; i++)
                {
                    // Once the threshold is reached (requestComplexPointCount) or we reached the end
                    // we send a request to the server
                    if(i % requestComplexPointCount == 0 || i == w * h)
                    {
                        if(i != 0)
                        {
                            await Console.Out.WriteLineAsync($"Processing Block {blockAt + 1} of {blockCount}");
                            json.Append("]}");

                            StringContent content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
                            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
                            request.Content = content;
                            HttpResponseMessage response = await client.SendAsync(request);
                            string respContent = await response.Content.ReadAsStringAsync();
                            using(JsonTextReader reader = new JsonTextReader(new StringReader(respContent)))
                            {
                                JObject o;
                                JToken? hArray = null;
                                try {
                                    o = (JObject)JToken.ReadFrom(reader);
                                    hArray = o["heights"];
                                } catch {}
                                {
                                    var vals = hArray?.Values<JToken>();
                                    //foreach(JToken? pt in hArray.Values<JToken>())
                                    for(int k = 0; k < i - lastRequest; k++)
                                    {
                                        float v = 0;
                                        if(vals != null && k < vals.Count())
                                        {
                                            v = vals.ElementAt(k)?.Value<JToken>("alts")?.Value<float>("COMB") ?? 0;
                                        }
                                        
                                        heights[xarr, yarr] = v;
                                        yarr += yarrdir;
                                        if(yarr == h || yarr == -1)
                                        {
                                            yarr += -yarrdir;
                                            yarrdir = -yarrdir;
                                            xarr++;
                                        }
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
                    if(i < w * h)
                    {
                        int gkX = XIdx2GK4(x), gkY = YIdx2GK4(y);
                        if(notPrepend)
                        {
                            notPrepend = false;
                        } else
                            json.Append(",");
                        json.Append($"[{gkX}, {gkY}]");
                        y += ydir;
                        if(y == h || y == -1)
                        {
                            y += -ydir;
                            ydir = -ydir;
                            x++;
                        }
                    }
                }
            } else
            {   
                for(int x = 0; x < heights.GetLength(0); x++)
                {
                    await Console.Out.WriteLineAsync($"Processing Block {x + 1} of {heights.GetLength(0)}");
                            
                    StringBuilder json = new StringBuilder();
                    json.Append("{\"type\":\"LineString\",\"coordinates\":[");
                    for(int y = 0; y < heights.GetLength(1); y++)
                    {
                        int gkX = XIdx2GK4(x), gkY = YIdx2GK4(y);
                        if(y != 0)
                            json.Append(",");
                        json.Append($"[{gkX}, {gkY}]");
                    }
                    json.Append("]}");
                    
                    StringContent content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
                    HttpRequestMessage hrm = new HttpRequestMessage(HttpMethod.Post, url);
                    hrm.Content = content;
                    HttpResponseMessage response = await client.SendAsync(hrm);
                    string respContent = await response.Content.ReadAsStringAsync();
                    using(JsonTextReader jtr = new JsonTextReader(new StringReader(respContent)))
                    {
                        JObject o = (JObject)JToken.ReadFrom(jtr);
                        JToken? hArray = o["heights"];
                        if(hArray == null)
                            continue;
                        int c = 0;
                        int count = hArray.Count();
                        
                        foreach(JToken? pt in hArray.Values<JToken>())
                        {
                            float v = pt?.Value<JToken>("alts")?.Value<float>("COMB") ?? 0;
                            heights[x, (int)((c / (float)count) * heights.GetLength(1))] = v;
                            
                            c++;
                        }
                    }
                }
            }
            client.Dispose();
            
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
            await Console.Out.WriteLineAsync($"Units per pixel: {step}");
            await Console.Out.WriteLineAsync();

            if(onlySaveRaw)
            {
                StringBuilder raw = new StringBuilder();
                for(int y = heights.GetLength(1) - 1; y >= 0; y--)
                {
                    for(int x = 0; x < heights.GetLength(0); x++)
                    {
                        if(x != 0)
                            raw.Append(" ");
                        raw.Append(heights[x, y]);
                    }
                    raw.AppendLine();
                }
                if(outputFile == null) // Write to stdout if no output file is set
                    await Console.Out.WriteLineAsync(raw.ToString());
                else
                    await File.WriteAllTextAsync(outputFile, raw.ToString());
            }
            else // Save image
            {
                using(var bmp = new Image<Rgb24>(w, h))
                {
                    for(int x = 0; x < heights.GetLength(0); x++)
                        for(int y = 0; y < heights.GetLength(1); y++)
                        {
                            float v = Map(heights[x, y], min, max, 0, 255);
                            byte vInt = (byte)v;
                            bmp[x, h - y - 1] = new Rgb24(vInt, vInt, vInt);
                        }
                    await bmp.SaveAsPngAsync(outputFile);
                }
            }
        }
    }
}