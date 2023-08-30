using System;
using System.Drawing;
using System.Drawing.Imaging;               // BitmapData, LockBits
using System.Runtime.InteropServices;       // DLLImport, Marshal
using System.IO;                            // File, Path
using System.Globalization;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace GrzTools
{

    // logger class
    public static class Logger {
        // write to log flag
        public static bool WriteToLog { get; set; }
        public static String FullFileNameBase { get; set; }
        // unconditional logging
        public static void logTextLnU(DateTime now, string logtxt) {
            _writeLogOverrule = true;
            logTextLn(now, logtxt);
            _writeLogOverrule = false;
        }
        public static void logTextU(string logtxt) {
            _writeLogOverrule = true;
            logTextToFile(logtxt);
            _writeLogOverrule = false;
        }
        // logging depending on WriteToLog
        public static void logTextLn(DateTime now, string logtxt) {
            logtxt = now.ToString("dd.MM.yyyy HH:mm:ss_fff ", CultureInfo.InvariantCulture) + logtxt;
            logText(logtxt + "\r\n");
        }
        public static void logText(string logtxt) {
            logTextToFile(logtxt);
        }
        // log motions list entry
        public async static void logMotionListEntry(string loc, int motionIndex, bool bmpExists, bool motionConsecutive, DateTime motionTime, bool motionSaved) {
            while ( _busy ) {
                await Task.Delay(500);
            }
            _busy = true;
            try {
                if ( FullFileNameBase.Length == 0 ) {
                    FullFileNameBase = Application.ExecutablePath;
                }
                string logFileName = FullFileNameBase + DateTime.Now.ToString("_yyyyMMdd", CultureInfo.InvariantCulture) + ".motions";
                System.IO.StreamWriter lsw = System.IO.File.AppendText(logFileName);
                if ( new FileInfo(logFileName).Length == 0 ) {
                    lsw.Write("call\tndx\tbmpEx.\tconsec.\ttimestamp\t\tbmpSaved\n");
                }
                string text = String.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}", loc, motionIndex, bmpExists, motionConsecutive, motionTime.ToString("HH:mm:ss_fff", CultureInfo.InvariantCulture), motionSaved);
                lsw.Write(text + "\n");
                lsw.Close();
            } catch {; }
            _busy = false;
        }
        // log motions list extra marker
        public async static void logMotionListExtra(string text) {
            while ( _busy ) {
                await Task.Delay(500);
            }
            _busy = true;
            try {
                if ( FullFileNameBase.Length == 0 ) {
                    FullFileNameBase = Application.ExecutablePath;
                }
                string logFileName = FullFileNameBase + DateTime.Now.ToString("_yyyyMMdd", CultureInfo.InvariantCulture) + ".motions";
                System.IO.StreamWriter lsw = System.IO.File.AppendText(logFileName);
                if ( new FileInfo(logFileName).Length == 0 ) {
                    lsw.Write("call\tndx\tbmpEx.\tconsec.\ttimestamp\t\tbmpSaved\n");
                }
                lsw.Write(text + "\n");
                lsw.Close();
            } catch {; }
            _busy = false;
        }
        // private
        private static bool _writeLogOverrule = false;
        private static bool _busy = false;
        private async static void logTextToFile(string logtxt) {
            if ( !WriteToLog && !_writeLogOverrule ) {
                return;
            }
            while ( _busy ) {
                await Task.Delay(500);
            }
            _busy = true;
            try {
                if ( FullFileNameBase.Length == 0 ) {
                    FullFileNameBase = Application.ExecutablePath;
                }
                string logFileName = FullFileNameBase + DateTime.Now.ToString("_yyyyMMdd", CultureInfo.InvariantCulture) + ".log";
                System.IO.StreamWriter lsw = System.IO.File.AppendText(logFileName);
                lsw.Write(logtxt);
                lsw.Close();
            } catch {; }
            _busy = false;
        }
    }

    // allows to set a different font to a MenuItem of a ContextMenu
    // https://stackoverflow.com/questions/12681806/changing-the-font-for-menu-in-the-windows-forms-application-net3-5
    class CustomMenuItem : MenuItem {
        private Font _font;
        public Font Font {
            get {
                return _font;
            }
            set {
                _font = value;
            }
        }
        public CustomMenuItem() {
            this.OwnerDraw = true;
            this.Font = SystemFonts.DefaultFont;
        }
        public CustomMenuItem(string text) : this() {
            this.Text = text;
        }
        protected override void OnMeasureItem(MeasureItemEventArgs e) {
            var size = TextRenderer.MeasureText(this.Text, this.Font);
            e.ItemWidth = (int)size.Width;
            e.ItemHeight = (int)size.Height;
        }
        protected override void OnDrawItem(DrawItemEventArgs e) {
            e.DrawBackground();
            e.Graphics.DrawString(this.Text, this.Font, Brushes.Blue, e.Bounds);
        }
    }

    // Point related tools
    class PointTools 
    {
        // returns if a point is inside a given convex hull polygon
        static bool IsPointInPolygon(Point p, Point[] polygon) {
            double minX = polygon[0].X;
            double maxX = polygon[0].X;
            double minY = polygon[0].Y;
            double maxY = polygon[0].Y;
            for ( int i = 1; i < polygon.Length; i++ ) {
                Point q = polygon[i];
                minX = Math.Min(q.X, minX);
                maxX = Math.Max(q.X, maxX);
                minY = Math.Min(q.Y, minY);
                maxY = Math.Max(q.Y, maxY);
            }
            if ( p.X < minX || p.X > maxX || p.Y < minY || p.Y > maxY ) {
                return false;
            }
            bool inside = false;
            for ( int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++ ) {
                if ( (polygon[i].Y > p.Y) != (polygon[j].Y > p.Y) &&
                     p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X ) {
                    inside = !inside;
                }
            }
            return inside;
        }

        // cartesian distance between two points
        public static int GetDistance(PointF pt1, PointF pt2) {
            return (int)Math.Sqrt((pt1.X - pt2.X) * (pt1.X - pt2.X) + (pt1.Y - pt2.Y) * (pt1.Y - pt2.Y));
        }
    }

    // Bitmap related tools
    class BitmapTools
    {
        // return a Bitmap 24bpp from an arbitrary Bitmap
        public static Bitmap ConvertTo24bpp(Image img) {
            var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
            using ( var gr = Graphics.FromImage(bmp) ) {
                gr.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
            }
            return bmp;
        }

        // make inplace 24bpp bitmap pixel by pixel subtraction of two 24 bpp bitmaps 
        public static unsafe void SubtractBitmap24bppToBitmap24bpp(ref Bitmap bmp, Bitmap subBmp) 
        {
            // sanity check
            if ( bmp.Size != subBmp.Size ) {
                return;
            }
            // lock bitmaps
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* bmpScan0 = (byte*)bmpData.Scan0.ToPointer();
            BitmapData subBmpData = subBmp.LockBits(new Rectangle(0, 0, subBmp.Width, subBmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte* subScan0 = (byte*)subBmpData.Scan0.ToPointer();
            // get bmp length
            int bmpLen = bmpData.Stride * bmp.Height;
            // loop over bmp length
            for ( int i = 0; i < bmpLen; i += 3 ) {
                // write subtraction back to minuend
                bmpScan0[i + 0] = (byte)Math.Abs(bmpScan0[i + 0] - subScan0[i + 0]);
                bmpScan0[i + 1] = (byte)Math.Abs(bmpScan0[i + 1] - subScan0[i + 1]);
                bmpScan0[i + 2] = (byte)Math.Abs(bmpScan0[i + 2] - subScan0[i + 2]);
            }
            // unlock bitmaps
            bmp.UnlockBits(bmpData);
            subBmp.UnlockBits(subBmpData);
        }

        // make inplace a Bitmap 24bpp gray from a Bitmap 24bpp color INLINE byte array 8bpp
        public static unsafe void Bmp24bppColorToBmp24bppGrayToArray8bppGray(ref Bitmap bmp, ref byte[] bmpArr) {
            int bmpArrNdx = 0;
            // lock 
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0 = (byte*)bmpData.Scan0.ToPointer();
            // loop bmp length
            int bmpLen = bmpData.Stride * bmp.Height;
            for ( int i = 0; i < bmpLen; i += 3 ) {
                // make gray
                byte gray = (byte)(0.2f * scan0[i + 0] + 0.6f * scan0[i + 1] + 0.2f * scan0[i + 2] + 0.5f);
                // write gray to bmpArr
                bmpArr[bmpArrNdx++] = gray;
                // write to back
                scan0[i + 0] = gray;
                scan0[i + 1] = gray;
                scan0[i + 2] = gray;
            }
            // unlock
            bmp.UnlockBits(bmpData);
        }

        // make inplace byte array 8bpp gray from a Bitmap 24bpp color
        public static unsafe void Bmp24bppColorToArray8bppGray(Bitmap bmp, ref byte[] bmpArr ) {
            int bmpArrNdx = 0;
            // lock 
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte* scan0 = (byte*)bmpData.Scan0.ToPointer();
            // loop bmp length
            int bmpLen = bmpData.Stride * bmp.Height;
            for ( int i = 0; i < bmpLen; i += 3 ) {
                // make gray out of 3 pixels
                byte gray = (byte)(0.2f * scan0[i + 0] + 0.6f * scan0[i + 1] + 0.2f * scan0[i + 2] + 0.5f);
                // write gray to outArray
                bmpArr[bmpArrNdx++] = gray;
            }
            // unlock
            bmp.UnlockBits(bmpData);
        }

        // make inplace a Bitmap 24bpp pseudo color from a Bitmap 24bpp color and INLINE byte array 8 bpp gray 
        public static unsafe void Bmp24bppColorToBmp24bppPseudoToArray8bppGray(ref Bitmap bmp, GrzBeamProfiler.MainForm.palette pal, ref byte[] bmpArr) {
            int bmpArrNdx = 0;
            // lock bitmap 
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte* scan0 = (byte*)bmpData.Scan0.ToPointer();
            // loop bmp length
            int bmpLen = bmpData.Stride * bmp.Height;
            for ( int i = 0; i < bmpLen; i += 3 ) {
                // get curent gray level
                byte gray = (byte)(0.2f * scan0[i + 0] + 0.6f * scan0[i + 1] + 0.2f * scan0[i + 2] + 0.5f);
                // write gray to bmpArr
                bmpArr[bmpArrNdx++] = gray;
                // write pseudo color data back to bmp
                scan0[i + 0] = pal.mapB[gray];
                scan0[i + 1] = pal.mapG[gray];
                scan0[i + 2] = pal.mapR[gray];
            }
            // unlock bitmap
            bmp.UnlockBits(bmpData);
        }
    }

    // simple string input dialog
    class InputDialog {
        public static DialogResult GetText(string title, string curVal, Form parent, out string retVal) {
            Form dc = new Form();
            dc.Text = title;
            dc.HelpButton = dc.MinimizeBox = dc.MaximizeBox = false;
            dc.ShowIcon = dc.ShowInTaskbar = false;
            dc.TopMost = true;
            dc.Height = 100;
            dc.Width = 300;
            dc.MinimumSize = new Size(dc.Width, dc.Height);
            int margin = 5;
            Size size = dc.ClientSize;
            TextBox tb = new TextBox();
            tb.Text = curVal;
            tb.Height = 20;
            tb.Width = size.Width - 2 * margin;
            tb.Location = new Point(margin, margin);
            tb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            dc.Controls.Add(tb);
            Button ok = new Button();
            ok.Text = "Ok";
            ok.Height = 23;
            ok.Width = 75;
            ok.Location = new Point(size.Width / 3 - ok.Width / 2, size.Height / 2);
            ok.Anchor = AnchorStyles.Bottom;
            ok.DialogResult = DialogResult.OK;
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Height = 23;
            cancel.Width = 75;
            cancel.Location = new Point(size.Width * 2 / 3 - ok.Width / 2, size.Height / 2);
            cancel.Anchor = AnchorStyles.Bottom;
            dc.Controls.Add(ok);
            dc.Controls.Add(cancel);
            dc.AcceptButton = ok;
            dc.CancelButton = cancel;
            dc.StartPosition = FormStartPosition.Manual;
            dc.Location = new Point(parent.Location.X + parent.Width / 2 - dc.ClientSize.Width / 2, parent.Location.Y + 300);
            if ( dc.ShowDialog() == DialogResult.OK ) {
                retVal = tb.Text;
                return DialogResult.OK;
            } else {
                retVal = curVal;
                return DialogResult.Cancel;
            }
        }
    }

}
