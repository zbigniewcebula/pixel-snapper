using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelSnapper.Core;

public static class Resampler
{
	public static Image<Rgba32> Resample(Image<Rgba32> source, List<int> cols, List<int> rows)
	{
		if (cols.Count < 2 || rows.Count < 2)
		{
			throw new PixelSnapperException("Insufficient grid cuts for resampling");
		}

		var outW = Math.Max(cols.Count, 1) - 1;
		var outH = Math.Max(rows.Count, 1) - 1;
		var final = new Image<Rgba32>(outW, outH);
		var width = source.Width;
		var height = source.Height;

		for (var yI = 0; yI < rows.Count - 1; yI++)
		{
			var ys = rows[yI];
			var ye = rows[yI + 1];
			for (var xI = 0; xI < cols.Count - 1; xI++)
			{
				var xs = cols[xI];
				var xe = cols[xI + 1];
				if (xe <= xs || ye <= ys)
				{
					continue;
				}

				var counts = new Dictionary<ulong, (Rgba32 Pixel, int Count)>();
				for (var y = ys; y < ye; y++)
				{
					for (var x = xs; x < xe; x++)
					{
						if (x >= width || y >= height)
						{
							continue;
						}

						var p = source[x, y];
						var key = PixelKey(p);
						if (counts.TryGetValue(key, out var entry))
						{
							counts[key] = (entry.Pixel, entry.Count + 1);
						}
						else
						{
							counts[key] = (p, 1);
						}
					}
				}

				if (counts.Count > 0)
				{
					var best = counts.Values
						.OrderByDescending(v => v.Count)
						.ThenBy(v => v.Pixel.R)
						.ThenBy(v => v.Pixel.G)
						.ThenBy(v => v.Pixel.B)
						.ThenBy(v => v.Pixel.A)
						.First();
					final[xI, yI] = best.Pixel;
				}
			}
		}

		return final;
	}

	private static ulong PixelKey(Rgba32 p) =>
		((ulong)p.R << 24) | ((ulong)p.G << 16) | ((ulong)p.B << 8) | p.A;
}

