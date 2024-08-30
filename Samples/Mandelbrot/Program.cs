using System.Text;

using Sharpmd;

int width = 1920;
int height = 1080;
var buffer = new byte[width * height];

SpmdRunner.DispatchRange2D(width, height, (x, y) => {
    float cr = x / (float)width * 2.5f - 2;
    float ci = y / (float)height * 2.0f - 1;

    float zr = 0, zi = 0;
    float zr2 = 0, zi2 = 0;
    int i = 0;

    for (; i < 255; i++) {
        zi = 2 * zr * zi + ci;
        zr = zr2 - zi2 + cr;
        zr2 = zr * zr;
        zi2 = zi * zi;
        if (zr2 + zi2 > 4) break;
    }
    buffer[x + y * width] = (byte)i;
});

using var fs = File.Create("mandel.pgm");
fs.Write(Encoding.ASCII.GetBytes($"P5\n{width} {height}\n255\n"));
fs.Write(buffer);
