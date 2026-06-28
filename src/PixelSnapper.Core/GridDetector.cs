using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelSnapper.Core;

public static class GridDetector
{
	public static (double[] ColProj, double[] RowProj) ComputeProfiles(Image<Rgba32> image)
	{
		var width = image.Width;
		var height = image.Height;
		if (width < 3 || height < 3)
		{
			throw new PixelSnapperException("Image too small (minimum 3x3)");
		}

		var colProj = new double[width];
		var rowProj = new double[height];

		image.ProcessPixelRows(accessor =>
		{
			for (var y = 0; y < height; y++)
			{
				var row = accessor.GetRowSpan(y);
				for (var x = 1; x < width - 1; x++)
				{
					var left = Gray(row[x - 1]);
					var right = Gray(row[x + 1]);
					colProj[x] += Math.Abs(right - left);
				}
			}
		});

		for (var x = 0; x < width; x++)
		{
			for (var y = 1; y < height - 1; y++)
			{
				var top = Gray(image[x, y - 1]);
				var bottom = Gray(image[x, y + 1]);
				rowProj[y] += Math.Abs(bottom - top);
			}
		}

		return (colProj, rowProj);
	}

	public static double? EstimateStepSize(double[] profile, Config config)
	{
		if (profile.Length == 0)
		{
			return null;
		}

		var maxVal = profile.Max();
		if (maxVal == 0)
		{
			return null;
		}

		var threshold = maxVal * config.PeakThresholdMultiplier;
		var peaks = new List<int>();
		for (var i = 1; i < profile.Length - 1; i++)
		{
			if (profile[i] > threshold && profile[i] > profile[i - 1] && profile[i] > profile[i + 1])
			{
				peaks.Add(i);
			}
		}

		if (peaks.Count < 2)
		{
			return null;
		}

		var cleanPeaks = new List<int> { peaks[0] };
		for (var i = 1; i < peaks.Count; i++)
		{
			if (peaks[i] - cleanPeaks[^1] > config.PeakDistanceFilter - 1)
			{
				cleanPeaks.Add(peaks[i]);
			}
		}

		if (cleanPeaks.Count < 2)
		{
			return null;
		}

		var diffs = new List<double>();
		for (var i = 0; i < cleanPeaks.Count - 1; i++)
		{
			diffs.Add(cleanPeaks[i + 1] - cleanPeaks[i]);
		}

		diffs.Sort();
		return diffs[diffs.Count / 2];
	}

	public static (double StepX, double StepY) ResolveStepSizes(
		double? stepXOpt, double? stepYOpt, int width, int height, Config config)
	{
		if (config.PixelSizeOverride is { } px)
		{
			return (px, px);
		}

		if (stepXOpt is { } sx && stepYOpt is { } sy)
		{
			var ratio = sx > sy ? sx / sy : sy / sx;
			if (ratio > config.MaxStepRatio)
			{
				var smaller = Math.Min(sx, sy);
				return (smaller, smaller);
			}

			var avg = (sx + sy) / 2.0;
			return (avg, avg);
		}

		if (stepXOpt is { } sxOnly)
		{
			return (sxOnly, sxOnly);
		}

		if (stepYOpt is { } syOnly)
		{
			return (syOnly, syOnly);
		}

		var fallback = Math.Max(Math.Min(width, height) / (double)config.FallbackTargetSegments, 1.0);
		return (fallback, fallback);
	}

	public static List<int> Walk(double[] profile, double stepSize, int limit, Config config)
	{
		if (profile.Length == 0)
		{
			throw new PixelSnapperException("Cannot walk on empty profile");
		}

		var cuts = new List<int> { 0 };
		var currentPos = 0.0;
		var searchWindow = Math.Max(stepSize * config.WalkerSearchWindowRatio, config.WalkerMinSearchWindow);
		var meanVal = profile.Average();

		while (currentPos < limit)
		{
			var target = currentPos + stepSize;
			if (target >= limit)
			{
				cuts.Add(limit);
				break;
			}

			var startSearch = Math.Max((int)(target - searchWindow), (int)(currentPos + 1));
			var endSearch = Math.Min((int)(target + searchWindow), limit);

			if (endSearch <= startSearch)
			{
				currentPos = target;
				continue;
			}

			var maxVal = -1.0;
			var maxIdx = startSearch;
			for (var i = startSearch; i < endSearch; i++)
			{
				if (profile[i] > maxVal) { maxVal = profile[i]; maxIdx = i; }
			}

			if (maxVal > meanVal * config.WalkerStrengthThreshold)
			{
				cuts.Add(maxIdx);
				currentPos = maxIdx;
			}
			else
			{
				cuts.Add((int)target);
				currentPos = target;
			}
		}

		return cuts;
	}

	public static (List<int> ColCuts, List<int> RowCuts) StabilizeBothAxes(
		double[] profileX, double[] profileY,
		List<int> rawColCuts, List<int> rawRowCuts,
		int width, int height, Config config)
	{
		var colPass1 = StabilizeCuts(profileX, rawColCuts, width, rawRowCuts, height, config);
		var rowPass1 = StabilizeCuts(profileY, rawRowCuts, height, rawColCuts, width, config);

		var colCells = Math.Max(colPass1.Count - 1, 1);
		var rowCells = Math.Max(rowPass1.Count - 1, 1);
		var colStep = width / (double)colCells;
		var rowStep = height / (double)rowCells;
		var stepRatio = colStep > rowStep ? colStep / rowStep : rowStep / colStep;

		if (stepRatio > config.MaxStepRatio)
		{
			var targetStep = Math.Min(colStep, rowStep);
			var finalCol = colStep > targetStep * 1.2
				? SnapUniformCuts(profileX, width, targetStep, config, config.MinCutsPerAxis)
				: colPass1;
			var finalRow = rowStep > targetStep * 1.2
				? SnapUniformCuts(profileY, height, targetStep, config, config.MinCutsPerAxis)
				: rowPass1;
			return (finalCol, finalRow);
		}

		return (colPass1, rowPass1);
	}

	private static double Gray(Rgba32 p) =>
		p.A == 0 ? 0.0 : 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;

	private static List<int> StabilizeCuts(
		double[] profile, List<int> cuts, int limit,
		List<int> siblingCuts, int siblingLimit, Config config)
	{
		if (limit == 0)
		{
			return [0];
		}

		cuts = SanitizeCuts(cuts, limit);
		var minRequired = Math.Min(Math.Max(config.MinCutsPerAxis, 2), limit + 1);

		var axisCells = Math.Max(cuts.Count - 1, 0);
		var siblingCells = Math.Max(siblingCuts.Count - 1, 0);
		var siblingHasGrid = siblingLimit > 0 && siblingCells >= Math.Max(minRequired - 1, 0) && siblingCells > 0;

		var stepsSkewed = false;
		if (siblingHasGrid && axisCells > 0)
		{
			var axisStep = limit / (double)axisCells;
			var siblingStep = siblingLimit / (double)siblingCells;
			var stepRatio = axisStep / siblingStep;
			stepsSkewed = stepRatio > config.MaxStepRatio || stepRatio < 1.0 / config.MaxStepRatio;
		}

		if (cuts.Count >= minRequired && stepsSkewed == false)
		{
			return cuts;
		}

		double targetStep;
		if (siblingHasGrid)
		{
			targetStep = siblingLimit / (double)siblingCells;
		}
		else if (config.FallbackTargetSegments > 1)
		{
			targetStep = limit / (double)config.FallbackTargetSegments;
		}
		else if (axisCells > 0)
		{
			targetStep = limit / (double)axisCells;
		}
		else
		{
			targetStep = limit;
		}

		if (double.IsNaN(targetStep) || double.IsInfinity(targetStep) || targetStep <= 0)
		{
			targetStep = 1.0;
		}

		return SnapUniformCuts(profile, limit, targetStep, config, minRequired);
	}

	private static List<int> SanitizeCuts(List<int> cuts, int limit)
	{
		if (limit == 0)
		{
			return [0];
		}

		var result = new List<int>(cuts);
		var hasZero = false;
		var hasLimit = false;

		for (var i = 0; i < result.Count; i++)
		{
			if (result[i] == 0)
			{
				hasZero = true;
			}

			if (result[i] >= limit)
			{
				result[i] = limit;
			}

			if (result[i] == limit)
			{
				hasLimit = true;
			}
		}

		if (hasZero == false)
		{
			result.Add(0);
		}

		if (hasLimit == false)
		{
			result.Add(limit);
		}

		result.Sort();
		var deduped = new List<int>();
		foreach (var v in result)
		{
			if (deduped.Count == 0 || deduped[^1] != v)
			{
				deduped.Add(v);
			}
		}

		return deduped;
	}

	private static List<int> SnapUniformCuts(
		double[] profile, int limit, double targetStep, Config config, int minRequired)
	{
		if (limit == 0)
		{
			return [0];
		}

		if (limit == 1)
		{
			return [0, 1];
		}

		var desiredCells = targetStep > 0 && double.IsInfinity(targetStep) == false
			? (int)Math.Round(limit / targetStep)
			: 0;
		desiredCells = Math.Max(desiredCells, Math.Max(minRequired - 1, 1));
		desiredCells = Math.Min(desiredCells, limit);

		var cellWidth = limit / (double)desiredCells;
		var searchWindow = Math.Max(cellWidth * config.WalkerSearchWindowRatio, config.WalkerMinSearchWindow);
		var meanVal = profile.Length > 0 ? profile.Average() : 0.0;

		var cuts = new List<int> { 0 };
		for (var idx = 1; idx < desiredCells; idx++)
		{
			var target = cellWidth * idx;
			var prev = cuts[^1];
			if (prev + 1 >= limit)
			{
				break;
			}

			var start = Math.Max((int)Math.Floor(target - searchWindow), prev + 1);
			var end = Math.Min((int)Math.Ceiling(target + searchWindow), limit - 1);
			if (end < start) { start = prev + 1; end = start; }

			var profileLen = profile.Length;
			var bestIdx = Math.Min(start, Math.Max(profileLen - 1, 0));
			var bestVal = -1.0;
			var endBound = Math.Min(end, profileLen - 1);
			for (var i = start; i <= endBound; i++)
			{
				var v = profile[i];
				if (v > bestVal) { bestVal = v; bestIdx = i; }
			}

			if (bestVal < meanVal * config.WalkerStrengthThreshold)
			{
				var fallbackIdx = (int)Math.Round(target);
				if (fallbackIdx <= prev)
				{
					fallbackIdx = prev + 1;
				}

				if (fallbackIdx >= limit)
				{
					fallbackIdx = Math.Max(limit - 1, prev + 1);
				}

				bestIdx = fallbackIdx;
			}

			cuts.Add(bestIdx);
		}

		if (cuts[^1] != limit)
		{
			cuts.Add(limit);
		}

		return SanitizeCuts(cuts, limit);
	}
}

