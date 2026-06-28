using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelSnapper.Core;

public static class PixelSnapperPipeline
{
	private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg"];

	public static bool IsSupportedExtension(string path)
	{
		var ext = Path.GetExtension(path).ToLowerInvariant();
		return SupportedExtensions.Contains(ext);
	}

	public static ProcessedImage Process(byte[] inputBytes, Config? config = null)
	{
		config ??= new Config();

		if (config.KColors <= 0)
		{
			throw new PixelSnapperException("k_colors must be greater than 0");
		}

		using var image = Image.Load<Rgba32>(inputBytes);
		var width = image.Width;
		var height = image.Height;

		ValidateDimensions(width, height);

		if (config.PixelSizeOverride is { } px)
		{
			var maxPx = Math.Min(width, height) / 2.0;
			if (double.IsNaN(px) || px < 1.0 || px > maxPx)
			{
				throw new PixelSnapperException(
					$"pixel_size_override {px:F1} is out of valid range [1, {Math.Min(width, height) / 2}]");
			}
		}

		Quantizer.Quantize(image, config);

		var (profileX, profileY) = GridDetector.ComputeProfiles(image);
		var stepXOpt = GridDetector.EstimateStepSize(profileX, config);
		var stepYOpt = GridDetector.EstimateStepSize(profileY, config);
		var (stepX, stepY) = GridDetector.ResolveStepSizes(stepXOpt, stepYOpt, width, height, config);

		var rawColCuts = GridDetector.Walk(profileX, stepX, width, config);
		var rawRowCuts = GridDetector.Walk(profileY, stepY, height, config);
		var (colCuts, rowCuts) = GridDetector.StabilizeBothAxes(
			profileX, profileY, rawColCuts, rawRowCuts, width, height, config);

		using var outputImage = Resampler.Resample(image, colCuts, rowCuts);

		using var ms = new MemoryStream();
		outputImage.Save(ms, new PngEncoder());
		var outputBytes = ms.ToArray();

		return new ProcessedImage(
			outputBytes,
			stepX,
			config.PixelSizeOverride.HasValue,
			colCuts.Count - 1,
			rowCuts.Count - 1);
	}

	private static void ValidateDimensions(int width, int height)
	{
		if (width == 0 || height == 0)
		{
			throw new PixelSnapperException("Image dimensions cannot be zero");
		}

		if (width > 10000 || height > 10000)
		{
			throw new PixelSnapperException("Image dimensions too large (max 10000x10000)");
		}
	}
}

