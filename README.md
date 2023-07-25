# bayernatlas-heightmapper

A small tool to download heightmap-images from [Geoportal Bayern's Bayernatlas](https://geoportal.bayern.de/bayernatlas/)

## Dependencies
- [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

## Usage
Just running the program with `dotnet run` displays a help:
```
Usage: bayernatlas-heightmapper [-h] [-S] [-r] [-u <units>] [-s <size>] centerX centerY [outputFile]

Download heightmap images or heightmap values from Bayernatlas.

Options:
 -h, --help     display this help
 -u, --units    units per pixel (meters). Default is 20
 -S, --simple   use a simplified downloading algorithm (not necessary in most cases)

 -s <size>,     specify size (two-value tuple) in GK4 units in each direction from the center.
 --size <size>  Example: 12000,12000
                Default: 5000,5000

 -r, --raw      don't render an image; output raw numeric height values instead

outputFile:
        Write the output to a file.
        When not using '-r' or '--raw', this must be set.
```

### Download example
1. Get the GK4 coordinates of a place, simply right click on a location on the website:
![screenshot of the website](/assets/on-website.png)
  - The fourth row contains the required coordinates.

2. Then, simply download a heightmap using the following command: \
`dotnet run 4466640 5418104 heightmap.png`
![result of the above command](/assets/heightmap.png)


By default, a pixel will represent an area of `20x20` meters.
This can be changed by using `--units 100`, to change it to `100x100` meters for example, etc.
The lower the value, the higher the resolution, but the longer the image will take to download.
The resolution is also limited by the server. `--units 20` is approximately the minimal resolution.

The physical size the image is covering can be changed by using `--size 10000,8000`, to capture an area
of `10000x8000` meters for example, etc.
By default, a `5000x5000` meter region will be captured.

### More examples
`dotnet run 4511300 5378491 heightmap2.png --units 30 --size 10000,8000`
![result of the above command](/assets/heightmap2.png)

`dotnet run 4481536 5422592 heightmap3.png --units 200 --size 130000,180000`
![result of the above command](/assets/heightmap3.png)

### Output image pixel values
At the end of each download you will see a summary of some parameters:
```
Final Parameters:
Minimum Height: 370.6
Maximum Height: 518.2
Size-X: 10000
Size-Y: 8000
Center-X: 4511300
Center-Y: 5378491
Units per pixel: 30
```

Important are the `Minimum Height` and `Maximum Height` values, as they directly depict a pixel value of `0` and `255` respectively. In-between values are interpolated linearly.

### Raw mode
By using the argument `--raw`, instead of rendering an image, the raw height-values in `meters` will be saved.
It is a simple collection of space-separated values, where each line contains the "pixels" of the image row-by-row.