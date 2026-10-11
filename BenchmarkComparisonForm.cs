using WukongBenchmarkRunner.Services;

namespace WukongBenchmarkRunner.UI;
// картинка с результатми, которая отображается в конце, поддерживает от 1 до 6 прогонов
public sealed class BenchmarkComparisonForm : Form
{
    private readonly List<Image> _images = [];

    public BenchmarkComparisonForm(IReadOnlyList<BenchmarkRun> runs, string screenshotDirectory)
    {
        if (runs.Count is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(runs), "Окно сравнения поддерживает от одного до шести прогонов.");
        }

        Text = "обезьяна самурай: результаты бенчмарка";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1600, 1000);
        MinimumSize = new Size(900, 600);
        WindowState = FormWindowState.Maximized;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = "обезьяна самурай: результаты бенчмарка",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = $"Скриншоты сохранены в: {screenshotDirectory}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 1);

        var columnCount = runs.Count switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 2,
            _ => 3
        };
        var rowCount = (runs.Count + columnCount - 1) / columnCount;
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columnCount,
            RowCount = rowCount,
            Padding = new Padding(8)
        };

        for (var column = 0; column < columnCount; column++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columnCount));
        }

        for (var row = 0; row < rowCount; row++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowCount));
        }

        for (var index = 0; index < runs.Count; index++)
        {
            grid.Controls.Add(CreateRunPanel(runs[index]), index % columnCount, index / columnCount);
        }

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        Controls.Add(layout);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var image in _images)
            {
                image.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private Control CreateRunPanel(BenchmarkRun run)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(6),
            Margin = new Padding(5)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = run.Name,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            AutoEllipsis = true
        }, 0, 0);

        Control imageViewport;
        if (run.ScreenshotPath is { } path && File.Exists(path))
        {
            try
            {
                var image = Image.FromFile(path);
                _images.Add(image);
                imageViewport = new ZoomableImagePanel(image);
            }
            catch (Exception exception)
            {
                imageViewport = CreatePlaceholder($"Снимок недоступен: {exception.Message}");
            }
        }
        else
        {
            imageViewport = CreatePlaceholder("Снимок недоступен");
        }

        panel.Controls.Add(imageViewport, 0, 1);
        return panel;
    }

    private static Label CreatePlaceholder(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.White,
        BackColor = Color.FromArgb(28, 28, 28),
        AutoEllipsis = true
    };

    private sealed class ZoomableImagePanel : Panel
    {
        private const float MinimumZoom = 1f;
        private const float MaximumZoom = 4f;
        private readonly PictureBox _pictureBox;
        private Size _fitSize;
        private float _zoom = MinimumZoom;

        public ZoomableImagePanel(Image image)
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            BackColor = Color.FromArgb(28, 28, 28);
            BorderStyle = BorderStyle.FixedSingle;
            TabStop = true;

            _pictureBox = new PictureBox
            {
                Image = image,
                Location = Point.Empty,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = BackColor,
                TabStop = true
            };
            _pictureBox.MouseEnter += (_, _) => _pictureBox.Focus();
            _pictureBox.MouseWheel += HandleMouseWheel;
            _pictureBox.DoubleClick += (_, _) => ResetZoom();
            Controls.Add(_pictureBox);

            MouseWheel += HandleMouseWheel;
            SizeChanged += (_, _) =>
            {
                if (_zoom == MinimumZoom)
                {
                    FitImage();
                }
            };
        }

        private void HandleMouseWheel(object? sender, MouseEventArgs e)
        {
            if (e.Delta == 0)
            {
                return;
            }

            _zoom = e.Delta > 0
                ? Math.Min(MaximumZoom, _zoom * 1.25f)
                : Math.Max(MinimumZoom, _zoom / 1.25f);

            if (_zoom == MinimumZoom)
            {
                FitImage();
                return;
            }

            ApplyZoom();
        }

        private void ResetZoom()
        {
            _zoom = MinimumZoom;
            FitImage();
        }

        private void FitImage()
        {
            AutoScrollMinSize = Size.Empty;
            _fitSize = ClientSize;
            _pictureBox.Location = Point.Empty;
            _pictureBox.Size = _fitSize;
        }

        private void ApplyZoom()
        {
            if (_fitSize.IsEmpty)
            {
                _fitSize = ClientSize;
            }

            var size = new Size(
                Math.Max(1, (int)Math.Ceiling(_fitSize.Width * _zoom)),
                Math.Max(1, (int)Math.Ceiling(_fitSize.Height * _zoom)));
            _pictureBox.Location = Point.Empty;
            _pictureBox.Size = size;
            AutoScrollMinSize = size;
        }
    }
}
