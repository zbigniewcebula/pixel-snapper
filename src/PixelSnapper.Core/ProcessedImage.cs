namespace PixelSnapper.Core;

public sealed record ProcessedImage(
	byte[] OutputPng,
	double PixelSize,
	bool PixelSizeOverride,
	int OutputWidth,
	int OutputHeight);

