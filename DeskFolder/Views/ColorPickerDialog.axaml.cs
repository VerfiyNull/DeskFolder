using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System;

namespace DeskFolder.Views;

public partial class ColorPickerDialog : Window
{
    public string SelectedColor { get; private set; } = "#FFFFFF";
    private bool _updatingFromHex = false;
    private bool _suppressHexTextChanged = false;
    private double _currentHue = 0;
    private double _currentSaturation = 1;
    private double _currentValue = 1;
    private Ellipse? _pickerIndicator;
    private Image? _wheelImage;
    private WriteableBitmap? _wheelBitmap;
    private int _wheelSize;
    private bool _isPressed = false;
    private Avalonia.Point _lastPointerPos;

    public ColorPickerDialog(string initialColor = "#FFFFFF")
    {
        InitializeComponent();
        SelectedColor = initialColor;

        Loaded += (s, e) =>
        {
            SetColorFromHex(initialColor);
            DrawColorWheel();

            if (BrightnessSlider != null)
            {
                BrightnessSlider.ValueChanged += BrightnessSlider_ValueChanged;
            }
            if (HexInput != null)
            {
                HexInput.TextChanged += HexInput_TextChanged;
                HexInput.KeyDown += HexInput_KeyDown;
            }
            if (ColorWheelCanvas != null)
            {
                ColorWheelCanvas.PointerPressed += ColorWheel_PointerPressed;
                ColorWheelCanvas.PointerMoved += ColorWheel_PointerMoved;
                ColorWheelCanvas.PointerReleased += ColorWheel_PointerReleased;
                ColorWheelCanvas.SizeChanged += (s, args) =>
                {
                    if (ColorWheelCanvas.Bounds.Width > 0 && ColorWheelCanvas.Bounds.Height > 0)
                    {
                        DrawColorWheel();
                        UpdatePickerPosition();
                    }
                };
            }
            if (SelectButton != null)
            {
                SelectButton.Click += SelectButton_Click;
            }
            if (CancelButton != null)
            {
                CancelButton.Click += CancelButton_Click;
            }

            // Track pointer globally while dragging so the indicator stays under the cursor
            AddHandler(PointerMovedEvent, Window_PointerMoved, RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, Window_PointerReleased, RoutingStrategies.Tunnel);

            // Ensure we draw after layout when bounds are valid
            Dispatcher.UIThread.Post(() =>
            {
                DrawColorWheel();
                UpdatePickerPosition();
            }, DispatcherPriority.Loaded);
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
            }
        };
    }

    private void DrawColorWheel()
    {
        if (ColorWheelCanvas == null) return;

        double width = ColorWheelCanvas.Bounds.Width;
        double height = ColorWheelCanvas.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            width = ColorWheelCanvas.Width > 0 ? ColorWheelCanvas.Width : 240;
            height = ColorWheelCanvas.Height > 0 ? ColorWheelCanvas.Height : width;
        }

        double size = Math.Min(width, height);
        if (size < 1) return;

        ColorWheelCanvas.Children.Clear();

        _wheelSize = (int)Math.Round(size);
        double radius = size / 2;

        _wheelBitmap = new WriteableBitmap(
            new PixelSize(_wheelSize, _wheelSize),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        _wheelImage = new Image
        {
            Width = size,
            Height = size,
            Source = _wheelBitmap,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(_wheelImage, 0);
        Canvas.SetTop(_wheelImage, 0);
        ColorWheelCanvas.Children.Add(_wheelImage);

        RenderWheelBitmap();


        _pickerIndicator = new Ellipse
        {
            Width = 20,
            Height = 20,
            Fill = Brushes.Transparent,
            Stroke = Brushes.White,
            StrokeThickness = 3,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(_pickerIndicator, radius - 10);
        Canvas.SetTop(_pickerIndicator, radius - 10);
        ColorWheelCanvas.Children.Add(_pickerIndicator);
    }

    private unsafe void RenderWheelBitmap()
    {
        if (_wheelBitmap == null) return;

        using var fb = _wheelBitmap.Lock();
        byte* ptr = (byte*)fb.Address;

        int size = _wheelSize;
        double radius = size / 2.0;
        double r2 = radius * radius;

        for (int y = 0; y < size; y++)
        {
            byte* row = ptr + (y * fb.RowBytes);
            double dy = (y + 0.5) - radius;

            for (int x = 0; x < size; x++)
            {
                double dx = (x + 0.5) - radius;
                double dist2 = dx * dx + dy * dy;

                int idx = x * 4;

                if (dist2 > r2)
                {
                    row[idx + 3] = 0; // A
                    row[idx + 2] = 0; // R
                    row[idx + 1] = 0; // G
                    row[idx + 0] = 0; // B
                    continue;
                }

                double distance = Math.Sqrt(dist2);
                double saturation = distance / radius;

                double angleRad = Math.Atan2(dy, dx);
                double angleDeg = angleRad * 180 / Math.PI;
                if (angleDeg < 0) angleDeg += 360;

                var color = HsvToRgb(angleDeg, saturation, _currentValue);

                row[idx + 3] = 255; // A
                row[idx + 2] = color.R; // R
                row[idx + 1] = color.G; // G
                row[idx + 0] = color.B; // B
            }
        }
    }

    private void ColorWheel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isPressed = true;
        e.Pointer.Capture(this);
        UpdateColorFromPosition(e.GetPosition(ColorWheelCanvas));
        e.Handled = true;
    }

    private void ColorWheel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPressed)
        {
            UpdateColorFromPosition(e.GetPosition(ColorWheelCanvas));
            e.Handled = true;
        }
    }

    private void ColorWheel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPressed = false;
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPressed || ColorWheelCanvas == null) return;

        UpdateColorFromPosition(e.GetPosition(ColorWheelCanvas));
        e.Handled = true;
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPressed) return;

        _isPressed = false;
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);

        e.Handled = true;
    }

    private void UpdateColorFromPosition(Avalonia.Point pos)
    {
        if (ColorWheelCanvas == null) return;

        _lastPointerPos = pos;

        double width = ColorWheelCanvas.Bounds.Width;
        double height = ColorWheelCanvas.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        double centerX = width / 2;
        double centerY = height / 2;
        double radius = Math.Min(width, height) / 2;

        double dx = pos.X - centerX;
        double dy = pos.Y - centerY;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance > radius)
        {
            double angle = Math.Atan2(dy, dx);
            dx = Math.Cos(angle) * radius;
            dy = Math.Sin(angle) * radius;
            distance = radius;
        }

        double angleRad = Math.Atan2(dy, dx);
        double angleDeg = angleRad * 180 / Math.PI;
        if (angleDeg < 0) angleDeg += 360;

        _currentHue = angleDeg;
        _currentSaturation = Math.Min(distance / radius, 1.0);

        // Always keep indicator at cursor position (clamped to wheel)
        UpdateIndicatorPosition(centerX + dx, centerY + dy);

        UpdateColorFromHSV();
    }

    private void BrightnessSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingFromHex) return;

        _currentValue = (BrightnessSlider?.Value ?? 100) / 100.0;

        if (BrightnessValue != null)
        {
            BrightnessValue.Text = $"{(int)((BrightnessSlider?.Value ?? 100))}%";
        }

        RenderWheelBitmap();

        UpdateColorFromHSV();
    }

    private void UpdateColorFromHSV()
    {
        var color = HsvToRgb(_currentHue, _currentSaturation, _currentValue);

        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        SelectedColor = hex;

        if (HexInput != null)
        {
            _suppressHexTextChanged = true;
            _updatingFromHex = true;
            HexInput.Text = hex;
            _updatingFromHex = false;
        }

        if (ColorPreview != null)
        {
            ColorPreview.Background = new SolidColorBrush(color);
        }
    }

    private void HexInput_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (_updatingFromHex) return;
        if (_isPressed) return;
        if (_suppressHexTextChanged)
        {
            _suppressHexTextChanged = false;
            return;
        }

        var text = HexInput?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (TryParseHexColor(text, out int r, out int g, out int b))
        {
            _updatingFromHex = true;

            var (h, s, v) = RgbToHsv(r, g, b);
            _currentHue = h;
            _currentSaturation = s;
            _currentValue = v;

            if (BrightnessSlider != null)
            {
                BrightnessSlider.Value = v * 100;
            }
            if (BrightnessValue != null)
            {
                BrightnessValue.Text = $"{(int)(v * 100)}%";
            }

            UpdatePickerPosition();
            RenderWheelBitmap();

            if (ColorPreview != null)
            {
                ColorPreview.Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
            }

            SelectedColor = text.StartsWith("#") ? text.ToUpperInvariant() : $"#{text.ToUpperInvariant()}";

            _updatingFromHex = false;
        }
    }

    private void UpdatePickerPosition()
    {
        if (_pickerIndicator == null || ColorWheelCanvas == null) return;

        double width = ColorWheelCanvas.Bounds.Width;
        double height = ColorWheelCanvas.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        double centerX = width / 2;
        double centerY = height / 2;
        double radius = Math.Min(width, height) / 2;

        double angleRad = _currentHue * Math.PI / 180;
        double distance = _currentSaturation * radius;

        double x = centerX + Math.Cos(angleRad) * distance;
        double y = centerY + Math.Sin(angleRad) * distance;

        UpdateIndicatorPosition(x, y);
    }

    private void UpdateIndicatorPosition(double x, double y)
    {
        if (_pickerIndicator == null) return;

        double indicatorSize = _pickerIndicator.Bounds.Width > 0
            ? _pickerIndicator.Bounds.Width
            : _pickerIndicator.Width;
        double indicatorRadius = indicatorSize / 2;

        Canvas.SetLeft(_pickerIndicator, x - indicatorRadius);
        Canvas.SetTop(_pickerIndicator, y - indicatorRadius);
    }

    private void HexInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SelectButton_Click(null, null);
        }
    }

    private void SetColorFromHex(string hex)
    {
        if (HexInput != null)
        {
            HexInput.Text = hex;
        }

        if (TryParseHexColor(hex, out int r, out int g, out int b))
        {
            _updatingFromHex = true;

            var (h, s, v) = RgbToHsv(r, g, b);
            _currentHue = h;
            _currentSaturation = s;
            _currentValue = v;

            if (BrightnessSlider != null)
            {
                BrightnessSlider.Value = v * 100;
            }
            if (BrightnessValue != null)
            {
                BrightnessValue.Text = $"{(int)(v * 100)}%";
            }

            UpdatePickerPosition();
            RenderWheelBitmap();
            UpdateColorFromHSV();

            _updatingFromHex = false;
        }
    }

    private bool TryParseHexColor(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        hex = hex.Trim().TrimStart('#');

        if (hex.Length == 3)
        {
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        }

        if (hex.Length != 6)
            return false;

        try
        {
            r = Convert.ToInt32(hex.Substring(0, 2), 16);
            g = Convert.ToInt32(hex.Substring(2, 2), 16);
            b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;

        double r = 0, g = 0, b = 0;

        if (h >= 0 && h < 60) { r = c; g = x; b = 0; }
        else if (h >= 60 && h < 120) { r = x; g = c; b = 0; }
        else if (h >= 120 && h < 180) { r = 0; g = c; b = x; }
        else if (h >= 180 && h < 240) { r = 0; g = x; b = c; }
        else if (h >= 240 && h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255)
        );
    }

    private (double h, double s, double v) RgbToHsv(int r, int g, int b)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double h = 0;
        if (delta != 0)
        {
            if (max == rd)
                h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd)
                h = 60 * (((bd - rd) / delta) + 2);
            else
                h = 60 * (((rd - gd) / delta) + 4);
        }
        if (h < 0) h += 360;

        double s = max == 0 ? 0 : delta / max;
        double v = max;

        return (h, s, v);
    }

    private void SelectButton_Click(object? sender, RoutedEventArgs? e)
    {
        var text = HexInput?.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (!text.StartsWith("#"))
                text = $"#{text}";
            SelectedColor = text.ToUpperInvariant();
        }

        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
