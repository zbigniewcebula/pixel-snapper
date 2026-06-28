using PixelSnapper.Core;

namespace PixelSnapper.App.Services;

public static class ImageProcessingService
{
	public static Task<ProcessedImage> ProcessAsync(byte[] inputBytes, Config config, CancellationToken cancellationToken = default)
	{
		return Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			return PixelSnapperPipeline.Process(inputBytes, config);
		}, cancellationToken);
	}
}

