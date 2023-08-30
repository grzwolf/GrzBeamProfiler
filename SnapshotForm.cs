using System;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.IO;

namespace GrzBeamProfiler
{
    public partial class SnapshotForm : Form
    {
        public SnapshotForm()
        {
            InitializeComponent();
        }

        public SnapshotForm(Bitmap bitmap)
        {
            InitializeComponent();
            SetImage(bitmap);
        }

        public void SetImage(Bitmap bitmap)
        {
            if ( bitmap == null ) {
                return;
            }

            timeBox.Text = DateTime.Now.ToLongTimeString() + "  --  " + bitmap.Width.ToString() + " x " + bitmap.Height.ToString();

            lock ( this ) {
                if ( pictureBox.Image != null ) {
                    pictureBox.Image.Dispose();
                }
                pictureBox.Image = bitmap;
            }
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach ( ImageCodecInfo codec in codecs ) {
                if ( codec.FormatID == format.Guid ) {
                    return codec;
                }
            }
            return null;
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            saveFileDialog.InitialDirectory = Application.StartupPath;
            saveFileDialog.FileName = "BeamProfile_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            if ( saveFileDialog.ShowDialog() == DialogResult.OK ) {
                string ext = Path.GetExtension(saveFileDialog.FileName).ToLower();
                ImageFormat format = ImageFormat.Jpeg;
                ImageCodecInfo encoder = GetEncoder(ImageFormat.Jpeg);

                if ( ext == ".bmp" ) {
                    format = ImageFormat.Bmp;
                } else {
                    if ( ext == ".png" ) {
                        format = ImageFormat.Png;
                    }
                }

                System.Drawing.Imaging.Encoder myEncoder = System.Drawing.Imaging.Encoder.Quality;
                EncoderParameters myEncoderParameters = new EncoderParameters(1);
                EncoderParameter myEncoderParameter = new EncoderParameter(myEncoder, 100L);
                myEncoderParameters.Param[0] = myEncoderParameter;

                try {
                    lock ( this ) {
                        Bitmap image = (Bitmap)pictureBox.Image;
                        if ( ext == ".jpg" ) {
                            image.Save(saveFileDialog.FileName, encoder, myEncoderParameters);
                        } else {
                            image.Save(saveFileDialog.FileName, format);
                        }
                    }
                } catch ( Exception ex ) {
                    MessageBox.Show("Failed saving the snapshot.\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
