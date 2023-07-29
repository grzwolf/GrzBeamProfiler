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

    // Bitmap related tools
    class BitmapTools
    {
        // convert a 24 bpp Bitmap to a 3 bytes per pixel array
        public static byte[] Bitmap24bppToByteArray( Bitmap sourceBitmap )
        {
            BitmapData sourceData = sourceBitmap.LockBits(new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte[] pixelBuffer = new byte[sourceData.Stride * sourceData.Height];
            Marshal.Copy(sourceData.Scan0, pixelBuffer, 0, pixelBuffer.Length);
            sourceBitmap.UnlockBits(sourceData);
            return pixelBuffer;
        }
        // convert a 3 bytes per pixel array to a 24 bpp Bitmap
        public static Bitmap ByteArrayToBitmap( int width, int height, byte[] pixelBuffer )
        {
            var resultBitmap = new Bitmap(width, height);
            BitmapData resultData = resultBitmap.LockBits(new Rectangle(0, 0, resultBitmap.Width, resultBitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb/* .Format32bppArgb*/);
            Marshal.Copy(pixelBuffer, 0, resultData.Scan0, pixelBuffer.Length);
            resultBitmap.UnlockBits(resultData);
            pixelBuffer = null;
            return resultBitmap;
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
