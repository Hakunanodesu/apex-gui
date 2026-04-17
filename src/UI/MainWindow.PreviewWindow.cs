using System.Threading;

public sealed partial class MainWindow
{
    private readonly object _snapRangePreviewWindowLock = new();
    private System.Windows.Forms.Form? _snapRangePreviewWindow;
    private bool _snapRangePreviewWindowVisible;
    private bool _snapRangePreviewShuttingDown;

    private void OpenSnapRangePreviewWindow()
    {
        lock (_snapRangePreviewWindowLock)
        {
            if (_snapRangePreviewWindow is not null && !_snapRangePreviewWindow.IsDisposed)
            {
                _snapRangePreviewWindow.BeginInvoke(new Action(() =>
                {
                    _snapRangePreviewWindowVisible = true;
                    _snapRangePreviewWindow.Show();
                    _snapRangePreviewWindow.Activate();
                    _snapRangePreviewWindow.BringToFront();
                }));
                return;
            }

            var previewWindowThread = new Thread(() =>
            {
                using var form = new System.Windows.Forms.Form
                {
                    Text = string.Empty,
                    StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                    ClientSize = new System.Drawing.Size(Math.Max(1, _snapOuterRange), Math.Max(1, _snapOuterRange)),
                    TopMost = true,
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                    MaximizeBox = false
                };
                form.BackColor = System.Drawing.Color.FromArgb(20, 22, 26);
                form.ShowInTaskbar = false;

                var refreshTimer = new System.Windows.Forms.Timer { Interval = 50 };
                refreshTimer.Tick += (_, _) =>
                {
                    var outer = Math.Max(1, _snapOuterRange);
                    var expectedSize = new System.Drawing.Size(outer, outer);
                    if (form.ClientSize != expectedSize)
                    {
                        form.ClientSize = expectedSize;
                    }

                    form.Invalidate();
                };

                form.Paint += (_, e) =>
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    var outer = Math.Max(1, _snapOuterRange);
                    var inner = Math.Clamp(_snapInnerRange, 0, outer);
                    var drawDiameter = Math.Max(2, Math.Min(form.ClientSize.Width, form.ClientSize.Height) - 2);
                    var centerX = form.ClientSize.Width / 2f;
                    var centerY = form.ClientSize.Height / 2f;
                    var outerRadius = drawDiameter / 2f;
                    var innerRadius = outerRadius * (inner / (float)outer);

                    using var outerPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0, 180, 255), 2f);
                    using var innerPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 140, 60), 2f);

                    var outerRect = new System.Drawing.RectangleF(
                        centerX - outerRadius,
                        centerY - outerRadius,
                        outerRadius * 2f,
                        outerRadius * 2f);
                    var innerRect = new System.Drawing.RectangleF(
                        centerX - innerRadius,
                        centerY - innerRadius,
                        innerRadius * 2f,
                        innerRadius * 2f);

                    e.Graphics.DrawEllipse(outerPen, outerRect);
                    e.Graphics.DrawEllipse(innerPen, innerRect);
                };

                form.FormClosing += (_, e) =>
                {
                    // Keep the UI thread alive; user close only hides window.
                    if (!_snapRangePreviewShuttingDown && e.CloseReason == System.Windows.Forms.CloseReason.UserClosing)
                    {
                        e.Cancel = true;
                        _snapRangePreviewWindowVisible = false;
                        form.Hide();
                    }
                };

                form.FormClosed += (_, _) =>
                {
                    refreshTimer.Stop();
                    refreshTimer.Dispose();
                    lock (_snapRangePreviewWindowLock)
                    {
                        _snapRangePreviewWindowVisible = false;
                        _snapRangePreviewWindow = null;
                    }
                };

                lock (_snapRangePreviewWindowLock)
                {
                    _snapRangePreviewWindow = form;
                }

                refreshTimer.Start();
                _snapRangePreviewWindowVisible = true;
                form.Show();
                System.Windows.Forms.Application.Run(form);
            })
            {
                IsBackground = true,
                Name = "SnapRangePreviewWindowThread"
            };
            previewWindowThread.SetApartmentState(ApartmentState.STA);
            previewWindowThread.Start();
        }
    }

    private bool IsSnapRangePreviewWindowOpen()
    {
        lock (_snapRangePreviewWindowLock)
        {
            return _snapRangePreviewWindowVisible && _snapRangePreviewWindow is not null && !_snapRangePreviewWindow.IsDisposed;
        }
    }

    private void CloseSnapRangePreviewWindow()
    {
        lock (_snapRangePreviewWindowLock)
        {
            if (_snapRangePreviewWindow is not null && !_snapRangePreviewWindow.IsDisposed)
            {
                _snapRangePreviewShuttingDown = true;
                _snapRangePreviewWindow.BeginInvoke(new Action(() => _snapRangePreviewWindow.Close()));
            }
        }
    }
}
