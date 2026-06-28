using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelSnapper.App.Services;
using PixelSnapper.Core;

namespace PixelSnapper.App;

public partial class MainWindow : Window
{
	private const int DebounceMs = 300;

	private byte[]? _inputBytes;
	private string? _inputPath;
	private ProcessedImage? _processed;
	private readonly DispatcherTimer _debounceTimer;
	private CancellationTokenSource? _processCts;
	private int _processGeneration;

	public MainWindow()
	{
		InitializeComponent();
		_debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
		_debounceTimer.Tick += (_, _) =>
		{
			_debounceTimer.Stop();
			_ = ProcessCurrentFileAsync();
		};
	}

	private void Params_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
		ScheduleProcess();

	private void Browse_Click(object sender, RoutedEventArgs e)
	{
		var dlg = new OpenFileDialog
		{
			Title = "Open image",
			Filter = "Images|*.png;*.jpg;*.jpeg|PNG|*.png|JPEG|*.jpg;*.jpeg|All files|*.*"
		};
		if (dlg.ShowDialog() == true)
		{
			LoadFile(dlg.FileName);
		}
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		if (_processed is null)
		{
			return;
		}

		var stem = "output";
		if (_inputPath is not null)
		{
			stem = Path.GetFileNameWithoutExtension(_inputPath);
		}

		var dlg = new SaveFileDialog
		{
			Title = "Save image",
			Filter = "PNG|*.png",
			DefaultExt = ".png",
			FileName = $"{stem}_snapped.png"
		};

		if (dlg.ShowDialog() != true)
		{
			return;
		}

		try
		{
			File.WriteAllBytes(dlg.FileName, _processed.OutputPng);
			SetStatus($"Saved: {dlg.FileName}");
		}
		catch (IOException ex)
		{
			SetStatus($"Save failed: {ex.Message}", true);
			MessageBox.Show($"Cannot save file:\n{ex.Message}", "Save error",
				MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private void DropZone_Click(object sender, MouseButtonEventArgs e) => Browse_Click(sender, e);

	private void DropZone_PreviewDragOver(object sender, DragEventArgs e)
	{
		HandleDragOver(e);
		DropZone.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D7EFF")!);
	}

	private void Window_PreviewDragOver(object sender, DragEventArgs e) => HandleDragOver(e);

	private void DropZone_Drop(object sender, DragEventArgs e) => HandleFileDrop(e);

	private void Window_Drop(object sender, DragEventArgs e) => HandleFileDrop(e);

	private Config BuildConfig()
	{
		if (int.TryParse(KColorsBox.Text.Trim(), out var kColors) == false || kColors <= 0)
		{
			throw new PixelSnapperException("k_colors must be greater than 0");
		}

		double? pixelSizeOverride = null;
		var pxText = PixelSizeBox.Text.Trim();
		if (string.IsNullOrEmpty(pxText) == false)
		{
			if (double.TryParse(pxText, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out var px) == false || px <= 0)
			{
				throw new PixelSnapperException("pixel_size must be a positive number");
			}

			pixelSizeOverride = px;
		}

		return new Config { KColors = kColors, PixelSizeOverride = pixelSizeOverride };
	}

	private void SetStatus(string message, bool isError = false)
	{
		StatusText.Text = message;
		StatusText.Foreground = new SolidColorBrush(isError
			? (Color)ColorConverter.ConvertFromString("#E74C3C")!
			: (Color)ColorConverter.ConvertFromString("#AAAAAA")!);
	}

	private void SetLoading(bool show)
	{
		LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		OutputPreview.Opacity = show ? 0.35 : 1.0;
		KColorsBox.IsEnabled = show == false;
		PixelSizeBox.IsEnabled = show == false;
	}

	private static BitmapImage LoadBitmap(byte[] data)
	{
		var image = new BitmapImage();
		using var ms = new MemoryStream(data);
		image.BeginInit();
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.StreamSource = ms;
		image.EndInit();
		image.Freeze();
		return image;
	}

	private void ShowInputPreview(byte[] data)
	{
		InputPreview.Source = LoadBitmap(data);
		InputPreview.Visibility = Visibility.Visible;
		InputPlaceholder.Visibility = Visibility.Collapsed;
	}

	private void ShowOutputPreview(byte[] pngData)
	{
		OutputPreview.Source = LoadBitmap(pngData);
		OutputPreview.Visibility = Visibility.Visible;
		OutputPlaceholder.Visibility = Visibility.Collapsed;
	}

	private void ClearOutputPreview()
	{
		OutputPreview.Source = null;
		OutputPreview.Visibility = Visibility.Collapsed;
		OutputPlaceholder.Visibility = Visibility.Visible;
		DetectedSizeText.Text = "—";
		OutputSizeText.Text = "—";
		_processed = null;
		SaveButton.IsEnabled = false;
	}

	private void LoadFile(string path)
	{
		if (File.Exists(path) == false)
		{
			SetStatus($"File not found: {path}", true);
			return;
		}

		if (PixelSnapperPipeline.IsSupportedExtension(path) == false)
		{
			SetStatus("Unsupported format. Use PNG, JPG, or JPEG.", true);
			return;
		}

		try
		{
			_inputBytes = File.ReadAllBytes(path);
			_inputPath = path;
			ShowInputPreview(_inputBytes);
			SetStatus($"Loaded: {Path.GetFileName(path)}");
			ScheduleProcess();
		}
		catch (IOException ex)
		{
			SetStatus($"Cannot read file: {ex.Message}", true);
		}
	}

	private void ScheduleProcess()
	{
		if (_inputBytes is null)
		{
			return;
		}

		_debounceTimer.Stop();
		_debounceTimer.Start();
	}

	private async Task ProcessCurrentFileAsync()
	{
		if (_inputBytes is null)
		{
			return;
		}

		Config config;
		try
		{
			config = BuildConfig();
		}
		catch (PixelSnapperException ex)
		{
			ClearOutputPreview();
			SetStatus(ex.Message, true);
			return;
		}

		_processCts?.Cancel();
		_processCts?.Dispose();
		_processCts = new CancellationTokenSource();
		var token = _processCts.Token;
		var generation = ++_processGeneration;

		SaveButton.IsEnabled = false;
		SetLoading(true);
		SetStatus("Processing…");

		var inputCopy = _inputBytes;
		try
		{
			var result = await ImageProcessingService.ProcessAsync(inputCopy, config, token);
			if (generation != _processGeneration || token.IsCancellationRequested)
			{
				return;
			}

			_processed = result;
			ShowOutputPreview(result.OutputPng);
			var source = result.PixelSizeOverride ? "override" : "auto-detected";
			DetectedSizeText.Text = $"{result.PixelSize:F1}px ({source})";
			OutputSizeText.Text = $"{result.OutputWidth}x{result.OutputHeight}";
			SaveButton.IsEnabled = true;

			var name = _inputPath is not null ? Path.GetFileName(_inputPath) : "image";
			SetStatus($"Done: {name} → {result.OutputWidth}x{result.OutputHeight}px");
		}
		catch (OperationCanceledException)
		{
			if (generation == _processGeneration)
			{
				SetStatus("Cancelled.");
			}
		}
		catch (PixelSnapperException ex)
		{
			if (generation != _processGeneration)
			{
				return;
			}

			ClearOutputPreview();
			SetStatus(ex.Message, true);
		}
		catch (Exception ex)
		{
			if (generation != _processGeneration)
			{
				return;
			}

			ClearOutputPreview();
			SetStatus($"Unexpected error: {ex.Message}", true);
		}
		finally
		{
			if (generation == _processGeneration)
			{
				SetLoading(false);
			}
		}
	}

	private static void HandleDragOver(DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effects = DragDropEffects.Copy;
			e.Handled = true;
		}
	}

	private void HandleFileDrop(DragEventArgs e)
	{
		DropZone.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555")!);
		if (e.Data.GetDataPresent(DataFormats.FileDrop) == false)
		{
			return;
		}

		var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
		if (files is { Length: > 0 })
		{
			LoadFile(files[0]);
		}
	}
}

