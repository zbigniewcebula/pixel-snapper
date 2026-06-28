using System.Diagnostics;
using PixelSnapper.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

var sizes = new[] { (128, 96), (512, 512), (1024, 1024) };

foreach (var (w, h) in sizes)
{
	using var img = new Image<Rgba32>(w, h);
	for (var y = 0; y < h; y += 8)
	{
		for (var x = 0; x < w; x += 8)
		{
			img[x, y] = ((x + y) / 8 % 2 == 0)
				? new Rgba32(220, 50, 50, 255)
				: new Rgba32(50, 180, 80, 255);
		}
	}

	using var ms = new MemoryStream();
	img.SaveAsPng(ms);
	var input = ms.ToArray();

	var sw = Stopwatch.StartNew();
	var result = PixelSnapperPipeline.Process(input, new Config());
	sw.Stop();

	Console.WriteLine($"{w}x{h}: {sw.ElapsedMilliseconds} ms -> {result.OutputWidth}x{result.OutputHeight}");
}

