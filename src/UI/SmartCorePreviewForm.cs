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
