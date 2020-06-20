using FadeBar.Properties;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FadeBar
{
    public partial class Form1 : Form
    {
        #region Native Methods and Structures

        const Int32 WS_EX_LAYERED = 0x80000;
        const Int32 ULW_ALPHA = 0x02;
        const byte AC_SRC_OVER = 0x00;
        const byte AC_SRC_ALPHA = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        struct PPoint
        {
            public Int32 x;
            public Int32 y;

            public PPoint(Int32 x, Int32 y)
            { this.x = x; this.y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PSize
        {
            public Int32 cx;
            public Int32 cy;

            public PSize(Int32 cx, Int32 cy)
            { this.cx = cx; this.cy = cy; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct ARGB
        {
            public byte Blue;
            public byte Green;
            public byte Red;
            public byte Alpha;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref PPoint pptDst, ref PSize psize, IntPtr hdcSrc, ref PPoint pprSrc,
            Int32 crKey, ref BLENDFUNCTION pblend, Int32 dwFlags);

        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal enum WindowCompositionAttribute
        {
            // ...
            WCA_ACCENT_POLICY = 19
            // ...
        }

        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_INVALID_STATE = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        #endregion

        private bool isVisible = true;
        private MenuItem[] trayMenu;
        private NotifyIcon trayIcon;
        private const int TASKBAR_HEIGHT = 29; // guessing here, need to find a way to get it from windows
        public Form1()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            InitializeComponent();

            int scrWidth = Screen.PrimaryScreen.Bounds.Width;
            int scrHeight = Screen.PrimaryScreen.Bounds.Height;
            trayMenu = new MenuItem[] {
                new MenuItem("Visible", ToggleVisible),
                new MenuItem("Exit", Exit)
            };
            trayMenu[0].Checked = isVisible;

            trayIcon = new NotifyIcon()
            {
                Icon = Resources.AppIcon,
                ContextMenu = new ContextMenu(trayMenu),
                Visible = true
            };

            this.FormClosing += new FormClosingEventHandler(Exit);
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.Size = new Size(scrWidth, scrHeight / 60);
            this.StartPosition = FormStartPosition.Manual;
            this.Left = 0;
            this.Top = Screen.PrimaryScreen.Bounds.Height - Size.Height - TASKBAR_HEIGHT;

            Bitmap backImage = new Bitmap(Image.FromFile(GetWallpaperPath()), new Size(scrWidth, scrHeight));
            backImage = backImage.Clone(new Rectangle(new Point(0, scrHeight - Height - TASKBAR_HEIGHT), new Size(Width, Height)), backImage.PixelFormat);
            for(int x = 0; x < backImage.Width; x++)
            {
                for(int y = 0; y < backImage.Height; y++)
                {
                    Color pixColor = backImage.GetPixel(x, y);
                    int a = Math.Min((int)((float) y / Height * 255f), 255);
                    backImage.SetPixel(x, y, Color.FromArgb(a, pixColor));
                }
            }
            
            SelectBitmap(backImage, 255);

            SetTaskbarTransparent(true);
        }

        public void SetTaskbarTransparent(bool transparent)
        {
            IntPtr hTaskbar = FindWindow("Shell_TrayWnd", null);

            var accent = new AccentPolicy();
            var accentStructSize = Marshal.SizeOf(accent);
            accent.AccentState = transparent ? AccentState.ACCENT_INVALID_STATE: AccentState.ACCENT_DISABLED;

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(hTaskbar, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }

        public void SelectBitmap(Bitmap bitmap, int opacity)
        {
            // Does this bitmap contain an alpha channel?
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
            {
                throw new ApplicationException("The bitmap must be 32bpp with alpha-channel.");
            }

            // Get device contexts
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOldBitmap = IntPtr.Zero;

            try
            {
                // Get handle to the new bitmap and select it into the current 
                // device context.
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                hOldBitmap = SelectObject(memDc, hBitmap);

                // Set parameters for layered window update.
                PSize newSize = new PSize(bitmap.Width, bitmap.Height);
                PPoint sourceLocation = new PPoint(0, 0);
                PPoint newLocation = new PPoint(this.Left, this.Top);
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = (byte)opacity;
                blend.AlphaFormat = AC_SRC_ALPHA;

                // Update the window.
                UpdateLayeredWindow(
                    this.Handle,     // Handle to the layered window
                    screenDc,        // Handle to the screen DC
                    ref newLocation, // New screen position of the layered window
                    ref newSize,     // New size of the layered window
                    memDc,           // Handle to the layered window surface DC
                    ref sourceLocation, // Location of the layer in the DC
                    0,               // Color key of the layered window
                    ref blend,       // Transparency of the layered window
                    ULW_ALPHA        // Use blend as the blend function
                    );
            }
            finally
            {
                // Release device context.
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    SelectObject(memDc, hOldBitmap);
                    DeleteObject(hBitmap);
                }
                DeleteDC(memDc);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // Add the layered extended style (WS_EX_LAYERED) to this window.
                CreateParams createParams = base.CreateParams;
                if (!DesignMode)
                    createParams.ExStyle |= WS_EX_LAYERED;
                return createParams;
            }
        }

        void ToggleVisible(object sender, EventArgs e)
        {
            isVisible = !isVisible;
            SetTaskbarTransparent(isVisible);
            this.Visible = isVisible;
            trayMenu[0].Checked = isVisible;
        }

        void Exit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            SetTaskbarTransparent(false);
            Application.Exit();
        }

        String GetWallpaperPath()
        {
            byte[] path = (byte[])Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop").GetValue("TranscodedImageCache");
            byte[] buf = new byte[path.Length - 24];
            Array.Copy(path, 24, buf, 0, buf.Length);
            return Encoding.Unicode.GetString(buf).TrimEnd("\0".ToCharArray());
        }
    }
}
