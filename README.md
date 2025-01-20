# bayernatlas-heightmapper

A small cli tool to download heightmap-images from [Geoportal Bayern's Bayernatlas](https://geoportal.bayern.de/bayernatlas)

## Table of Contents
- [bayernatlas-heightmapper](#bayernatlas-heightmapper)
  - [Table of Contents](#table-of-contents)
  - [Dependencies](#dependencies)
  - [Usage](#usage)
    - [Example download](#example-download)
    - [More examples](#more-examples)
  - [Image pixel values](#image-pixel-values)
  - [Raw mode](#raw-mode)
  - [Topographical mode](#topographical-mode)

## Dependencies
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## Usage
Just running the program with `dotnet run` displays this help:
```
Usage: bayernatlas-heightmapper [-h] [-v] [-S] [-r] [-u <units>] [-s <size>] [-t <step>] centerX centerY [outputFile]

Download heightmap images or heightmap values from Bayernatlas.

centerX: Longitude in GK4-Coordinates.
centerY: Latitude in GK4-Coordinates.

Options:
 -h, --help     Display this help
 -u, --units    Units per pixel (meters). Default is 20
 -v, --verbose  Print occurring exceptions
 -S, --simple   Use a simplified downloading algorithm (not necessary in most cases)

 -s <size>,     Specify size (two-value tuple) in GK4 units in each direction from the center.
 --size <size>  Example: 12000,12000
                Default: 5000,5000

 -x <by>,       Scale the resulting image by that factor
 --scale <by>   Example: 5
                Default: 1

 -r, --raw      Don't render an image; output raw numeric height values instead

 -t <step>,     Instead of saving the image as a heightmap, draw a simplified topographical map
 --topo <step>  The lines will be separated by 'step' meters of height.
                Example: 22.5

outputFile:
        Write the output to a file.
        When not using '-r' or '--raw', this must be set.
```

The website and its api use [GK4-Coordinates](https://en.wikipedia.org/wiki/Gauss%E2%80%93Kr%C3%BCger_coordinate_system).
One unit in this coordinate system is equivalent to 1 meter in real life.

### Example download
1. Get the GK4 coordinates of the location you want to have at the center of the image. \
   Simply right click on the website: \
![screenshot of the website](/assets/on-website.png)
  - The fourth row contains the required coordinates. (marked with the text `GK4`) \
    Of course, the dialog layout might change in the future if the website gets updated.

1. Then, download a heightmap using the following command:
```sh
dotnet run 4466640 5418104 heightmap.png
```
![result of the above command](/assets/heightmap.png)

By default, a pixel will represent an area of `20x20` meters.
This can be changed by using `--units 100`, to change it to `100x100` meters for example, etc.
The lower the value, the higher the resolution, but the longer the image will take to download.
The resolution is also limited by the server. `--units 20` is approximately the minimal value.
Below that you don't really get any more precision as the server itself interpolates the values internally.

The physical size the image is covering can be changed by using `--size`. For example to capture an area
of `10000x8000` meters, you would have to use `--size 5000,4000`, as it specifies how far the region extends into each direction
from the center. I.e. 5000m north, 5000m south and 4000m west, 4000m east. So the resulting area will always be double that of `size`.
By default, `size` is set to `5000,5000`.

### More examples
```sh
dotnet run 4511300 5378491 heightmap2.png --units 30 --size 10000,8000
```
![result of the above command](/assets/heightmap2.png)

```sh
dotnet run 4481536 5422592 heightmap3.png --units 200 --size 130000,180000
```
![result of the above command](/assets/heightmap3.png)
This is a map showing the entirety of bavaria. \
The server does not have data outside the state borders, thus looking a bit chipped at the edges.

## Image pixel values
At the end of each download you will see a summary of some parameters:
```
Final Parameters:
Minimum Height: 370.53
Maximum Height: 518.14
Size-X: 10000
Size-Y: 8000
Center-X: 4511300
Center-Y: 5378491
Units per pixel: 30
Final image size: 666x533 pixels
```

Important are the `Minimum Height` and `Maximum Height` values, as they directly correspond to a pixel value of `0` and `255` respectively. All values are interpolated linearly within this range.

When importing the image as terrain in 3D software or a game engine, you need to specify this height
range.

## Raw mode
By using the argument `--raw`, instead of rendering an image, the raw height-values in `meters` will be saved.
It is a simple collection of space-separated values, where each line contains the "pixels" of the image row-by-row.

These values are not interpolated to fit any particular range, allowing more precision than the image
version, as the raw numbers are outputted.

## Topographical mode
By using the argument `--topo`, instead of outputting a grayscale heightmap, a simplified
[topographic map](https://en.wikipedia.org/wiki/Topographic_map) will be rendered.
Specify the height-spacing of lines after the `--topo` argument.
The coloring is chosen according to this hardcoded table:
| Height (m) | Color                |
| ---------: | :------------------- |
| < 0        | black                |
| 0          | rgb(120, 170, 255) |
| 200        | rgb(240, 255, 200) |
| 350        | rgb(170, 255, 120) |
| 450        | rgb(170, 200,  50) |
| 600        | rgb(140, 160,  50) |
| 1000       | rgb(255, 200, 120) |
| 2000       | rgb(255, 240, 200) |
| > 10000    | rgb(255, 255, 255) |

The output is linearly interpolated for any height between two values.

Examples:
```sh
dotnet run 4307200 5538368 topo1.png --topo 10 --scale 5 --size 6000,6000
```
![result of the above command](/assets/topo1.png)

```sh
dotnet run 4613664 5409312 topo2.png --topo 12.5 --scale 5 --size 15000,15000
```
![result of the above command](/assets/topo2.png)
(The image shown has been scaled down to 1500x1500 from its original resolution of 7500x7500)
