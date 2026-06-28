namespace PixelSnapper.Core;

public sealed class Config
{
	public int KColors { get; set; } = 16;
	public double? PixelSizeOverride { get; set; }
	public int KSeed { get; set; } = 42;
	public int MaxKmeansIterations { get; set; } = 15;
	public double PeakThresholdMultiplier { get; set; } = 0.2;
	public int PeakDistanceFilter { get; set; } = 4;
	public double WalkerSearchWindowRatio { get; set; } = 0.35;
	public double WalkerMinSearchWindow { get; set; } = 2.0;
	public double WalkerStrengthThreshold { get; set; } = 0.5;
	public int MinCutsPerAxis { get; set; } = 4;
	public int FallbackTargetSegments { get; set; } = 64;
	public double MaxStepRatio { get; set; } = 1.8;
}

