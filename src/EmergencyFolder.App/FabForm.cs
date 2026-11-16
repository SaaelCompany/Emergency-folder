using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace EmergencyFolder;

internal sealed class FabForm : Form
{
    const int LogicalSize = 100;
    const int PaddingPx = 12;
    const int ButtonPx = 44;
    const int WsExLayered = 0x00080000;
    const int WsExToolWindow = 0x00000080;
    const int WsExNoActivate = 0x08000000;
    const int WsExTransparent = 0x00000020;
    const int GwlExStyle = -20;
    const int UlwAlpha = 2;

    readonly Action _onClick;
    readonly System.Windows.Forms.Timer _timer;
    float _ripple;
    bool _hover;
    bool _clickThrough = true;

    public FabForm(Action onClick)
    {
        _onClick = onClick;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Size = new Size(LogicalSize, LogicalSize);
        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += (_, _) => OnTick();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExToolWindow | WsExNoActivate | WsExTransparent;
            return parameters;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Dismiss();
            return;
        }

        base.OnFormClosing(e);
    }

    public void PlaceBottomRight()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        Location = new Point(area.Right - LogicalSize, area.Bottom - LogicalSize);
    }

    public void Present()
    {
        if (!Visible)
        {
            PlaceBottomRight();
            Show();
        }

        if (!_timer.Enabled)
        {
            _timer.Start();
        }

        Render();
    }

    public void Dismiss()
    {
        _timer.Stop();
        if (Visible)
        {
            Hide();
        }
    }

    void OnTick()
    {
        _ripple += 33f / 2000f;
        if (_ripple > 1f)
        {
            _ripple -= 1f;
        }

        UpdateHover();
        Render();
    }

    void UpdateHover()
    {
        if (!Visible)
        {
            return;
        }

        var inside = IsInside(PointToClient(Cursor.Position));
        if (inside == _hover && _clickThrough == !inside)
        {
            return;
        }

        _hover = inside;
        SetClickThrough(!inside);
    }

    bool IsInside(Point client)
    {
        var center = ButtonCenter();
        var radius = ButtonPx / 2f * (_hover ? 1.12f : 1f);
        var dx = client.X - center.X;
        var dy = client.Y - center.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    static PointF ButtonCenter() => new(LogicalSize - PaddingPx - ButtonPx / 2f, LogicalSize - PaddingPx - ButtonPx / 2f);

    void SetClickThrough(bool enabled)
    {
        if (!IsHandleCreated || enabled == _clickThrough)
        {
            _clickThrough = enabled;
            return;
        }

        var style = GetWindowLongPtr(Handle, GwlExStyle).ToInt64();
        style = enabled ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(Handle, GwlExStyle, new IntPtr(style));
        _clickThrough = enabled;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (IsInside(e.Location))
        {
            _onClick();
        }

        base.OnMouseClick(e);
    }

    void Render()
    {
        if (!IsHandleCreated || !Visible)
        {
            return;
        }

        var scale = Math.Max(1f, DeviceDpi / 96f);
        var size = Math.Max(1, (int)Math.Round(LogicalSize * scale));
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            DrawButton(graphics, scale);
        }

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var dib = CreateAlphaBitmap(bitmap);
        var old = SelectObject(memory, dib);
        try
        {
            var destination = PointToScreen(Point.Empty);
            var origin = Point.Empty;
            var windowSize = new Size(size, size);
            var blend = new BlendFunction
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1
            };
            UpdateLayeredWindow(Handle, screen, ref destination, ref windowSize, memory, ref origin, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(dib);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    void DrawButton(Graphics graphics, float scale)
    {
        var center = ButtonCenter();
        var cx = center.X * scale;
        var cy = center.Y * scale;
        var button = ButtonPx * scale;
        var rippleRadius = button / 2f * (1f + 0.6f * _ripple);
        var rippleAlpha = (int)(0.8f * (1f - _ripple) * 255);
        using (var ripplePen = new Pen(Color.FromArgb(rippleAlpha, 239, 68, 68), Math.Max(1f, 2f * scale)))
        {
            graphics.DrawEllipse(ripplePen, cx - rippleRadius, cy - rippleRadius, rippleRadius * 2, rippleRadius * 2);
        }

        var draw = button * (_hover ? 1.08f : 1f);
        var rect = new RectangleF(cx - draw / 2f, cy - draw / 2f, draw, draw);
        using (var shadow = new SolidBrush(Color.FromArgb(90, 220, 38, 38)))
        {
            graphics.FillEllipse(shadow, rect.X + scale, rect.Y + 3f * scale, rect.Width, rect.Height);
        }

        using (var brush = new LinearGradientBrush(rect, Color.FromArgb(185, 28, 28), Color.FromArgb(239, 68, 68), LinearGradientMode.ForwardDiagonal))
        {
            graphics.FillEllipse(brush, rect);
        }

        using var pen = new Pen(Color.White, Math.Max(2f, 3f * scale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var mark = draw * 0.28f;
        graphics.DrawLine(pen, cx, cy - mark, cx, cy + mark * 0.2f);
        var dot = Math.Max(2f, 3.2f * scale);
        graphics.FillEllipse(Brushes.White, cx - dot / 2f, cy + mark * 0.45f, dot, dot);
    }

    static IntPtr CreateAlphaBitmap(Bitmap bitmap)
    {
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = bitmap.Width,
                Height = -bitmap.Height,
                Planes = 1,
                BitCount = 32,
                Compression = 0
            }
        };
        var dib = CreateDIBSection(IntPtr.Zero, ref info, 0, out var bits, IntPtr.Zero, 0);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var rowBytes = bitmap.Width * 4;
            var buffer = new byte[rowBytes];
            for (var y = 0; y < bitmap.Height; y++)
            {
                var source = IntPtr.Add(data.Scan0, y * data.Stride);
                Marshal.Copy(source, buffer, 0, rowBytes);
                Marshal.Copy(buffer, 0, IntPtr.Add(bits, y * rowBytes), rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return dib;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref Point pptDst,
        ref Size psize,
        IntPtr hdcSrc,
        ref Point pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BitmapInfo pbmi,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [StructLayout(LayoutKind.Sequential)]
    struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public BitmapInfoHeader Header;
    }
}
