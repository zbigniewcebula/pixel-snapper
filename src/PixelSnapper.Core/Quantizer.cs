using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelSnapper.Core;

public static class Quantizer
{
	public static void Quantize(Image<Rgba32> image, Config config)
	{
		if (config.KColors <= 0)
		{
			throw new PixelSnapperException("Number of colors must be greater than 0");
		}

		var opaque = new List<RgbF>();
		image.ProcessPixelRows(accessor =>
		{
			for (var y = 0; y < accessor.Height; y++)
			{
				var row = accessor.GetRowSpan(y);
				for (var x = 0; x < row.Length; x++)
				{
					ref var p = ref row[x];
					if (p.A == 0)
					{
						continue;
					}

					opaque.Add(new RgbF(p.R, p.G, p.B));
				}
			}
		});

		var nPixels = opaque.Count;
		if (nPixels == 0)
		{
			return;
		}

		var rng = new Random(config.KSeed);
		var k = Math.Min(config.KColors, nPixels);
		var centroids = new RgbF[k];
		var distances = new double[nPixels];
		Array.Fill(distances, double.PositiveInfinity);

		centroids[0] = opaque[SampleIndex(rng, nPixels)];
		for (var ci = 1; ci < k; ci++)
		{
			var last = centroids[ci - 1];
			var sumSqDist = 0.0;
			for (var i = 0; i < nPixels; i++)
			{
				var dSq = DistSq(opaque[i], last);
				if (dSq < distances[i])
				{
					distances[i] = dSq;
				}

				sumSqDist += distances[i];
			}

			centroids[ci] = sumSqDist <= 0
				? opaque[SampleIndex(rng, nPixels)]
				: opaque[WeightedSample(distances, rng)];
		}

		var prevCentroids = (RgbF[])centroids.Clone();
		for (var iteration = 0; iteration < config.MaxKmeansIterations; iteration++)
		{
			var sums = new double[k, 3];
			var counts = new int[k];

			for (var pi = 0; pi < nPixels; pi++)
			{
				var p = opaque[pi];
				var minDist = double.PositiveInfinity;
				var best = 0;
				for (var i = 0; i < k; i++)
				{
					var d = DistSq(p, centroids[i]);
					if (d < minDist) { minDist = d; best = i; }
				}

				sums[best, 0] += p.R;
				sums[best, 1] += p.G;
				sums[best, 2] += p.B;
				counts[best]++;
			}

			for (var i = 0; i < k; i++)
			{
				if (counts[i] > 0)
				{
					centroids[i] = new RgbF(
						(float)(sums[i, 0] / counts[i]),
						(float)(sums[i, 1] / counts[i]),
						(float)(sums[i, 2] / counts[i]));
				}
			}

			if (iteration > 0)
			{
				var maxMovement = 0.0;
				for (var i = 0; i < k; i++)
				{
					var movement = DistSq(centroids[i], prevCentroids[i]);
					if (movement > maxMovement)
					{
						maxMovement = movement;
					}
				}

				if (maxMovement < 0.01)
				{
					break;
				}
			}

			Array.Copy(centroids, prevCentroids, k);
		}

		image.ProcessPixelRows(accessor =>
		{
			for (var y = 0; y < accessor.Height; y++)
			{
				var row = accessor.GetRowSpan(y);
				for (var x = 0; x < row.Length; x++)
				{
					ref var p = ref row[x];
					if (p.A == 0)
					{
						continue;
					}

					var px = new RgbF(p.R, p.G, p.B);
					var minDist = double.PositiveInfinity;
					RgbF best = px;
					for (var i = 0; i < k; i++)
					{
						var d = DistSq(px, centroids[i]);
						if (d < minDist) { minDist = d; best = centroids[i]; }
					}

					p.R = (byte)Math.Clamp((int)Math.Round(best.R), 0, 255);
					p.G = (byte)Math.Clamp((int)Math.Round(best.G), 0, 255);
					p.B = (byte)Math.Clamp((int)Math.Round(best.B), 0, 255);
				}
			}
		});
	}

	private static int SampleIndex(Random rng, int upper) => rng.Next(upper);

	private static int WeightedSample(double[] distances, Random rng)
	{
		var total = 0.0;
		foreach (var d in distances)
		{
			total += d;
		}

		if (total <= 0)
		{
			return SampleIndex(rng, distances.Length);
		}

		var r = rng.NextDouble() * total;
		var cum = 0.0;
		for (var i = 0; i < distances.Length; i++)
		{
			cum += distances[i];
			if (r <= cum)
			{
				return i;
			}
		}

		return distances.Length - 1;
	}

	private static double DistSq(RgbF p, RgbF c)
	{
		var dr = p.R - c.R;
		var dg = p.G - c.G;
		var db = p.B - c.B;
		return dr * dr + dg * dg + db * db;
	}

	private readonly struct RgbF
	{
		public readonly float R, G, B;

		public RgbF(float r, float g, float b)
		{
			R = r;
			G = g;
			B = b;
		}
	}
}

