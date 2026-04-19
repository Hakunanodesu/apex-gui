using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

internal sealed class SmartCorePreviewForm : System.Windows.Forms.Form
{
    public SmartCorePreviewForm()
    {
        SetStyle(
            System.Windows.Forms.ControlStyles.UserPaint |
            System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
            System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer,
            true);
        UpdateStyles();
    }

    protected override void OnPaintBackground(System.Windows.Forms.PaintEventArgs e)
    {
        // Skip the default background erase to reduce visible flicker between frames.
    }
}

public sealed partial class MainWindow
{
    private const int SmartCorePreviewIntervalMs = 1000 / 60;
    private readonly object _smartCorePreviewWindowLock = new();
    private System.Windows.Forms.Form? _smartCorePreviewWindow;
    private bool _smartCorePreviewShuttingDown;

    private void OpenSmartCorePreviewWindow()
    {
        lock (_smartCorePreviewWindowLock)
        {
            if (_smartCorePreviewWindow is not null && !_smartCorePreviewWindow.IsDisposed)
            {
                _smartCorePreviewWindow.BeginInvoke(new Action(() =>
                {
                    _smartCorePreviewWindow.Show();
                    _smartCorePreviewWindow.Activate();
                    _smartCorePreviewWindow.BringToFront();
                }));
                return;
            }

            var previewWindowThread = new Thread(() =>
            {
                var initialSize = Math.Max(1, _homeViewState.SnapOuterRange);
                using var form = new SmartCorePreviewForm
                {
                    Text = "智慧核心预览",
                    StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                    ClientSize = new System.Drawing.Size(initialSize, initialSize),
                    TopMost = true,
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle,
                    MaximizeBox = false,
                    MinimizeBox = false
                };
                form.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
                form.BackColor = System.Drawing.Color.FromArgb(18, 20, 24);
                form.ShowInTaskbar = false;
                form.Shown += (_, _) =>
                {
                    form.MinimumSize = form.Size;
                    form.MaximumSize = form.Size;
                    form.TopMost = true;
                };

                var frameBuffer = Array.Empty<byte>();
                var lastFrameId = 0;
                var frameWidth = 0;
                var frameHeight = 0;
                string? frameError = null;
                System.Drawing.Bitmap? cachedBitmap = null;

                var refreshTimer = new System.Windows.Forms.Timer { Interval = SmartCorePreviewIntervalMs };
                refreshTimer.Tick += (_, _) =>
                {
                    var targetSize = Math.Max(1, _homeViewState.SnapOuterRange);
                    var expectedClientSize = new System.Drawing.Size(targetSize, targetSize);
                    if (form.ClientSize != expectedClientSize)
                    {
                        form.ClientSize = expectedClientSize;
                        form.MinimumSize = form.Size;
                        form.MaximumSize = form.Size;
                    }

                    var worker = _dxgiWorker;
                    var hasNewFrame = false;
                    if (worker is not null)
                    {
                        hasNewFrame = worker.TryCopyLatestFrame(ref frameBuffer, ref lastFrameId, out frameWidth, out frameHeight, out frameError);
                    }
                    else
                    {
                        frameWidth = 0;
                        frameHeight = 0;
                    }

                    if (hasNewFrame || worker is null)
                    {
                        form.Invalidate();
                    }
                };

                form.Paint += (_, e) =>
                {
                    e.Graphics.Clear(System.Drawing.Color.FromArgb(18, 20, 24));
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                    if (frameWidth <= 0 || frameHeight <= 0 || frameBuffer.Length != frameWidth * frameHeight * 4)
                    {
                        var statusText = string.IsNullOrWhiteSpace(frameError) ? "等待捕获画面..." : $"捕获错误: {frameError}";
                        using var statusBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Gainsboro);
                        e.Graphics.DrawString(statusText, form.Font, statusBrush, new System.Drawing.PointF(12f, 12f));
                        return;
                    }

                    var clientRect = new System.Drawing.Rectangle(0, 0, form.ClientSize.Width, form.ClientSize.Height);
                    var scale = Math.Min(clientRect.Width / (float)frameWidth, clientRect.Height / (float)frameHeight);
                    scale = Math.Max(scale, 1f);
                    var drawWidth = Math.Max(1, (int)MathF.Round(frameWidth * scale));
                    var drawHeight = Math.Max(1, (int)MathF.Round(frameHeight * scale));
                    var drawRect = new System.Drawing.Rectangle(
                        clientRect.X + (clientRect.Width - drawWidth) / 2,
                        clientRect.Y + (clientRect.Height - drawHeight) / 2,
                        drawWidth,
                        drawHeight);

                    if (cachedBitmap is null || cachedBitmap.Width != frameWidth || cachedBitmap.Height != frameHeight)
                    {
                        cachedBitmap?.Dispose();
                        cachedBitmap = new System.Drawing.Bitmap(frameWidth, frameHeight, PixelFormat.Format32bppArgb);
                    }

                    var bitmapData = cachedBitmap.LockBits(
                        new System.Drawing.Rectangle(0, 0, frameWidth, frameHeight),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);
                    try
                    {
                        Marshal.Copy(frameBuffer, 0, bitmapData.Scan0, frameBuffer.Length);
                    }
                    finally
                    {
                        cachedBitmap.UnlockBits(bitmapData);
                    }

                    e.Graphics.DrawImage(cachedBitmap, drawRect);

                    var boxes = _onnxWorker?.GetDebugBoxes() ?? Array.Empty<OnnxDebugBox>();
                    if (boxes.Length > 0)
                    {
                        using var boxPen = new System.Drawing.Pen(System.Drawing.Color.Red, 2f);
                        for (var i = 0; i < boxes.Length; i++)
                        {
                            var box = boxes[i];
                            var x1 = box.X - box.W * 0.5f;
                            var y1 = box.Y - box.H * 0.5f;
                            var x2 = box.X + box.W * 0.5f;
                            var y2 = box.Y + box.H * 0.5f;

                            var minX = Math.Clamp(MathF.Min(x1, x2), 0f, frameWidth);
                            var minY = Math.Clamp(MathF.Min(y1, y2), 0f, frameHeight);
                            var maxX = Math.Clamp(MathF.Max(x1, x2), 0f, frameWidth);
                            var maxY = Math.Clamp(MathF.Max(y1, y2), 0f, frameHeight);

                            var overlayRect = new System.Drawing.RectangleF(
                                drawRect.Left + minX / frameWidth * drawRect.Width,
                                drawRect.Top + minY / frameHeight * drawRect.Height,
                                (maxX - minX) / frameWidth * drawRect.Width,
                                (maxY - minY) / frameHeight * drawRect.Height);

                            if (overlayRect.Width > 1f && overlayRect.Height > 1f)
                            {
                                e.Graphics.DrawRectangle(boxPen, overlayRect.X, overlayRect.Y, overlayRect.Width, overlayRect.Height);
                            }
                        }
                    }
                };

                form.FormClosing += (_, e) =>
                {
                    if (!_smartCorePreviewShuttingDown && e.CloseReason == System.Windows.Forms.CloseReason.UserClosing)
                    {
                        e.Cancel = true;
                        form.Hide();
                    }
                };

                form.FormClosed += (_, _) =>
                {
                    refreshTimer.Stop();
                    refreshTimer.Dispose();
                    cachedBitmap?.Dispose();
                    lock (_smartCorePreviewWindowLock)
                    {
                        _smartCorePreviewWindow = null;
                    }
                };

                lock (_smartCorePreviewWindowLock)
                {
                    _smartCorePreviewWindow = form;
                }

                refreshTimer.Start();
                form.Show();
                System.Windows.Forms.Application.Run(form);
            })
            {
                IsBackground = true,
                Name = "SmartCorePreviewWindowThread"
            };
            previewWindowThread.SetApartmentState(ApartmentState.STA);
            previewWindowThread.Start();
        }
    }

    private void CloseSmartCorePreviewWindow()
    {
        lock (_smartCorePreviewWindowLock)
        {
            if (_smartCorePreviewWindow is not null && !_smartCorePreviewWindow.IsDisposed)
            {
                _smartCorePreviewShuttingDown = true;
                _smartCorePreviewWindow.BeginInvoke(new Action(() => _smartCorePreviewWindow.Close()));
            }
        }
    }

    private bool IsSmartCorePreviewWindowOpen()
    {
        lock (_smartCorePreviewWindowLock)
        {
            return _smartCorePreviewWindow is not null && !_smartCorePreviewWindow.IsDisposed && _smartCorePreviewWindow.Visible;
        }
    }
}
