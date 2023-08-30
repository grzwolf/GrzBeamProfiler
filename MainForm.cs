using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;                    // DLLImport
using System.Linq;
using AForge.Video.DirectShow;
using System.Threading.Tasks;
using GrzBeamProfiler.Properties;
using Emgu.CV;                                           // fit ellipse

namespace GrzBeamProfiler
{
    public partial class MainForm : Form, IMessageFilter
    {
        AppSettings _settings = new AppSettings();       // app settings

        string _headlineAlert = "";                      // headline alert message
        bool _beamSearchError = false;

        private FilterInfoCollection _videoDevices;      // AForge collection of camera devices
        private VideoCaptureDevice _videoDevice = null;  // AForge camera device
        bool _videoDeviceJustConnected = false;          // just connected means: the stage right after the camera was started, it ends after the first image was processed
        double _fps = 0;                                 // camera fps
        private int _videoDeviceRestartCounter = 0;      // video device restart counter per app session

        private string _buttonConnectString;             // button text, a lame method to distinguish between camera "connect" vs. "- stop -" 

        Bitmap _bmp = null;                              // copy of current camera frame
        Size _bmpSize = new Size();                      // dimensions of current camera frame 
        byte[] _bmpArr;                                  // all bmp pixel values in a separate 8bpp array; allows fast access

        Point _crosshairCenter = new Point();            // crosshair is the beam's ellipse center relative to canvas 
        int _crosshairDiameterLast = 0;                  // last known crosshair diameter, needed if crosshair doesn't follow beam 
        byte _beamPeakIntensity;                         // beam's peak gray value
        ulong _beamEllipsePower;                         // calculated beam power according to FWHM / D86  
        byte _beamEllipseBorderIntensity;                // pixel intensity on ellipse boder is used to determine to pen color
        Point _peakCenterSub = new Point();              // peak intensity center point of subBeam 

        Point _beamSearchOriginCurrent = new Point();    // memorize the current beam search offset, needed if search origin's white cross is hidden 

        float _multiplierBmp2Paint;                      // multiplier between _bmp and tableLayoutPanelGraphs_Paint / pictureBox_MouseDown
        Point[] _horProf;                                // beam horizontal pixel intensity profile
        Point[] _verProf;                                // beam vertical pixel intensity profile

        Task _socketTask;                                // another app can connect via network socket to GrzBeamProfiler to receive screenshots
        bool _runSocketTask = false;
        Bitmap _socketBmp = null;

        Emgu.CV.Structure.Ellipse _epCv;                 // the beam's standardized ellipse shape (FWHM, D86) 

        Bitmap _bmpBkgnd = null;                         // background image to reduce the nose level around a beam

        palette _pal = new palette();                    // pseudo color palette
        AForge.Imaging.Filters.ColorRemapping _filter;   // pseudo color filter

        // init pens
        Pen _crosshairPen = new Pen(Color.FromArgb(255, 255, 0, 0), 3);
        Pen _ellipsePen = new Pen(Color.FromArgb(255, 255, 0, 0), 3);
        Pen _thresholdPen = new Pen(Color.FromArgb(255, 255, 255, 255), 1);
        Color _ellipseColorInverted;

        System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();

        // the one and only way to avoid the 'red cross exception' in pictureBox: "wrong parameter" 
        public class PictureBoxPlus : System.Windows.Forms.PictureBox
        {
            // that's all the magic: catch exceptions inside 'protected override void OnPaint'; there is NO way to interpret/avoid them, since they come from the underlying Win32 
            protected override void OnPaint(PaintEventArgs pea)
            {
                try {
                    base.OnPaint(pea);
                }
                catch {;}
            }
        }
        private PictureBoxPlus pictureBox;

        // pictureBox context menu 
        ContextMenu pictureBox_Cm = new ContextMenu();
        MenuItem pictureBox_CmShowNative = new MenuItem("show native image");
        MenuItem pictureBox_CmShowCrosshair = new MenuItem("show crosshair (ESC toggles too)");
        MenuItem pictureBox_CmCrosshairFollowsBeam = new MenuItem("crosshair follows beam");
        MenuItem pictureBox_CmForceGray = new MenuItem("show gray color");
        MenuItem pictureBox_CmPseudoColors = new MenuItem("show pseudo color");
        MenuItem pictureBox_CmEllipse = new MenuItem("show fitted ellipse");
        MenuItem pictureBox_CmEllipsePoints = new MenuItem("show fitted ellipse intensity points");
        MenuItem pictureBox_CmEllipsePointsHull = new MenuItem("show fitted ellipse intensity hull");
        MenuItem pictureBox_CmBeamBorder = new MenuItem("show beam border");
        MenuItem pictureBox_CmBeamPeak = new MenuItem("show beam peak intensity point");
        MenuItem pictureBox_CmSearchTraces = new MenuItem("show beam search traces");
        MenuItem pictureBox_CmShowSearchOrigin = new MenuItem("show beam search origin");
        MenuItem pictureBox_CmSearchCenter = new MenuItem("beam search from center");
        MenuItem pictureBox_CmSearchManual = new MenuItem("beam search manual (SHIFT+left mouse)");
        MenuItem pictureBox_CmBeamDiameter = new MenuItem("");
        MenuItem pictureBox_CmBeamThreshold = new MenuItem("");
        MenuItem pictureBox_CmSwapProfiles = new MenuItem("swap intensity profile sections");
        Font cmFont = new Font(FontFamily.GenericSansSerif, 10);
        Point _mouseButtonDownDown; // allows to keep open (re show) the context menu after editing

        public MainForm( )
        {
            // form designer standard init
            InitializeComponent();

            // subclassed PictureBoxPlus handles the 'red cross exception'
            //   !! I couldn't find a way to make this class accessible thru designer & toolbox (exception thrown when dragging to form)
            this.pictureBox = new PictureBoxPlus();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).BeginInit();
            this.pictureBox.BackColor = SystemColors.ActiveBorder;
            this.pictureBox.Dock = DockStyle.Fill;
            this.pictureBox.Location = new Point(104, 0);
            this.pictureBox.Margin = new Padding(0);
            this.pictureBox.Name = "pictureBox";
            this.pictureBox.Size = new Size(796, 492);
            this.pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            this.pictureBox.TabIndex = 0;
            this.pictureBox.TabStop = false;
            this.pictureBox.MouseDown += new MouseEventHandler(this.pictureBox_MouseDown);
            this.pictureBox.MouseMove += new MouseEventHandler(this.pictureBox_MouseMove);
            this.tableLayoutPanelGraphs.Controls.Add(this.pictureBox, 1, 0);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).EndInit();

            // pictureBox context menu 
            GrzTools.CustomMenuItem cmi = new GrzTools.CustomMenuItem();
            cmi.Text = "Beam characteristics";
            cmi.Font = cmFont;
            this.pictureBox_Cm.MenuItems.Add(cmi);
            this.pictureBox_CmBeamThreshold.Click += pictureBox_CmClickBeamThreshold;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmBeamThreshold);
            this.pictureBox_CmBeamDiameter.Click += pictureBox_CmClickBeamDiameter;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmBeamDiameter);
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmSwapProfiles);
            this.pictureBox_CmSwapProfiles.Click += pictureBox_CmClickSwapProfiles;
            this.pictureBox_Cm.MenuItems.Add("-");
            cmi = new GrzTools.CustomMenuItem();
            cmi.Text = "Beam appearance";
            cmi.Font = cmFont;
            this.pictureBox_Cm.MenuItems.Add(cmi);
            this.pictureBox_CmShowNative.Click += pictureBox_CmClickShowNative;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmShowNative);
            this.pictureBox_CmShowCrosshair.Checked = true;
            this.pictureBox_CmShowCrosshair.Click += pictureBox_CmClickShowCrosshair;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmShowCrosshair);
            this.pictureBox_CmForceGray.Click += pictureBox_CmClickForceGray;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmForceGray);
            this.pictureBox_CmPseudoColors.Click += pictureBox_CmClickPseudoColors;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmPseudoColors);
            this.pictureBox_CmEllipse.Checked = true;
            this.pictureBox_CmEllipse.Click += pictureBox_CmClickEllipse;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmEllipse);
            this.pictureBox_CmBeamBorder.Click += pictureBox_CmClickBeamBorder;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmBeamBorder);
            this.pictureBox_CmEllipsePoints.Click += pictureBox_CmClickEllipsePoints;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmEllipsePoints);
            this.pictureBox_CmEllipsePointsHull.Click += pictureBox_CmClickEllipsePointsHull;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmEllipsePointsHull);
            this.pictureBox_CmSearchTraces.Click += pictureBox_CmClickSearchTraces;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmSearchTraces);
            this.pictureBox_CmBeamPeak.Click += pictureBox_CmClickBeamPeak;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmBeamPeak);
            this.pictureBox_CmCrosshairFollowsBeam.Checked = true;
            this.pictureBox_CmCrosshairFollowsBeam.Click += pictureBox_CmClickCrosshaisFollowsBeam;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmCrosshairFollowsBeam);
            this.pictureBox_Cm.MenuItems.Add("-");
            cmi = new GrzTools.CustomMenuItem();
            cmi.Text = "Beam search origin";
            cmi.Font = cmFont;
            this.pictureBox_Cm.MenuItems.Add(cmi);
            this.pictureBox_CmShowSearchOrigin.Click += pictureBox_CmClickShowSearchOrigin;
            this.pictureBox_Cm.MenuItems.Add(pictureBox_CmShowSearchOrigin);
            this.pictureBox_CmSearchCenter.Click += pictureBox_CmClickSearchCenter;
            this.pictureBox_Cm.MenuItems.Add(pictureBox_CmSearchCenter);
            this.pictureBox_CmSearchManual.Click += pictureBox_CmClickSearchManual;
            this.pictureBox_Cm.MenuItems.Add(pictureBox_CmSearchManual);
            this.pictureBox.ContextMenu = this.pictureBox_Cm;

            // set control's protected property double buffered to prevent flickering when paint
            // https://stackoverflow.com/questions/24910574/how-to-prevent-flickering-when-using-paint-method-in-c-sharp-winforms  
            Control ctrl = this.tableLayoutPanelGraphs;
            ctrl.GetType()
                .GetProperty("DoubleBuffered",
                             System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(this.tableLayoutPanelGraphs, true, null);

            // memorize the connect button text, abused as status flag
            _buttonConnectString = this.connectButton.Text;

            // add "about entry" to system menu
            SetupSystemMenu();

            // get background image from file, if existing
            if ( System.IO.File.Exists("BackGroundImage.bmp") ) {
                _bmpBkgnd = new Bitmap("BackGroundImage.bmp");
                if ( _bmpBkgnd.PixelFormat != PixelFormat.Format24bppRgb ) {
                    _bmpBkgnd = GrzTools.BitmapTools.ConvertTo24bpp(_bmpBkgnd);
                }
            }

            // get settings from INI
            _settings.ReadFromIni();
            // set app behaviour according to settings; in case ini craps out, delete it and begin from scratch 
            try {
                updateAppFromSettings();
            }
            catch {
                System.IO.File.Delete(System.Windows.Forms.Application.ExecutablePath + ".ini");
                _settings.ReadFromIni();
                updateAppFromSettings();
            }

            // IMessageFilter - an encapsulated message filter
            // - also needed: class declaration "public partial class MainForm: Form, IMessageFilter"
            // - also needed: event handler "public bool PreFilterMessage( ref Message m )"
            // - also needed: Application.RemoveMessageFilter(this) when closing this form
            System.Windows.Forms.Application.AddMessageFilter(this);

        }

        // update app from settings
        void updateAppFromSettings()
        {
            // essential app settings
            this.Size = _settings.FormSize;
            this.Location = _settings.FormLocation;

            // context menu
            this.pictureBox_CmBeamBorder.Checked = _settings.BeamBorder;
            this.pictureBox_CmBeamPeak.Checked = _settings.BeamPeak;
            this.pictureBox_CmForceGray.Checked = _settings.ForceGray;
            this.pictureBox_CmPseudoColors.Checked = _settings.PseudoColors;
            this.pictureBox_CmSwapProfiles.Checked = _settings.SwapProfiles;

            // update exposure and brightness scrollers
            this.hScrollBarExposure.Minimum = _settings.ExposureMin;
            this.hScrollBarExposure.Maximum = _settings.ExposureMax;
            this.hScrollBarExposure.Value = _settings.Exposure;
            this.hScrollBarBrightness.Minimum = _settings.BrightnessMin;
            this.hScrollBarBrightness.Maximum = _settings.BrightnessMax;
            this.hScrollBarBrightness.Value = _settings.Brightness;
            // update exposure and brightness labels and tooltips
            updateExposureUI();
            updateBrightnessUI();

            // pseudo color table
            switch ( _settings.PseudoColorTable ) {
                case AppSettings.ColorTable.TEMPERATURE: _pal = buildPaletteTemperature(); break;
                case AppSettings.ColorTable.RAINBOW:     _pal = buildPaletteSpectrum(); break;
                default:                                 _pal = buildPaletteSpectrum(); break;
            }
            this.pictureBoxPseudo.Image = bitmapFromPalette(_pal);
            _filter = new AForge.Imaging.Filters.ColorRemapping(_pal.mapR, _pal.mapG, _pal.mapB);

            // beam characteristics
            _settings.BeamMinimumDiameter = Math.Max(4, Math.Min(5000, _settings.BeamMinimumDiameter));
            updateBeamSearchOriginFromSettings();
            this.pictureBox_CmBeamDiameter.Text = "beam minimum diameter: " + _settings.BeamMinimumDiameter.ToString();
            this.pictureBox_CmBeamThreshold.Text = "beam intensity threshold: " + _settings.BeamIntensityThreshold.ToString();

            // init logger
            GrzTools.Logger.FullFileNameBase = System.Windows.Forms.Application.ExecutablePath; // option: if useful, could be set to another location
            GrzTools.Logger.WriteToLog = true;                                                  // option: if useful, could be set otherwise    
            
            // SubtractBackgroundImage
            if ( _settings.SubtractBackgroundImage ) {
                if ( _bmpBkgnd == null ) {
                    MessageBox.Show("Please capture a background image, otherwise backgound subtraction doesn't work.");
                }
            }
        }

        // update settings from app
        void updateSettingsFromApp()
        {
            _settings.FormSize = this.Size;
            _settings.FormLocation = this.Location;
            _settings.ForceGray = this.pictureBox_CmForceGray.Checked;
            _settings.SwapProfiles = this.pictureBox_CmSwapProfiles.Checked;
            _settings.PseudoColors = this.pictureBox_CmPseudoColors.Checked;
            _settings.Exposure = this.hScrollBarExposure.Value;
            _settings.ExposureMin = this.hScrollBarExposure.Minimum;
            _settings.ExposureMax = this.hScrollBarExposure.Maximum;
            _settings.Brightness = this.hScrollBarBrightness.Value;
            _settings.BrightnessMin = this.hScrollBarBrightness.Minimum;
            _settings.BrightnessMax = this.hScrollBarBrightness.Maximum;
            updateSettingsFromBeamSearchOrigin();
        }

        //
        // pictureBox context menu click handlers
        //
        void pictureBox_CmClickBeamThreshold(object sender, EventArgs e) {
            string retVal;
            DialogResult dr = GrzTools.InputDialog.GetText("Beam intensity threshold", _settings.BeamIntensityThreshold.ToString(), this, out retVal);
            int intVal;
            int.TryParse(retVal, out intVal);
            _settings.BeamIntensityThreshold = Math.Min(250, Math.Max(10, intVal));
            this.pictureBox_CmBeamThreshold.Text = "beam intensity threshold: " + _settings.BeamIntensityThreshold.ToString();
            if ( dr == DialogResult.OK ) {
                // keep context menu open
                pictureBox_Cm.Show(pictureBox, _mouseButtonDownDown);
            }
        }
        void pictureBox_CmClickBeamDiameter(object sender, EventArgs e) {
            string retVal;
            DialogResult dr = GrzTools.InputDialog.GetText("Beam minimum diameter", _settings.BeamMinimumDiameter.ToString(), this, out retVal);
            int intVal;
            int.TryParse(retVal, out intVal);
            int maxDiam = _bmpSize.Width > 0 ? _bmpSize.Width / 2 : 500;
            _settings.BeamMinimumDiameter = Math.Min(maxDiam, Math.Max(5, intVal));
            this.pictureBox_CmBeamDiameter.Text = "beam minimum diameter: " + _settings.BeamMinimumDiameter.ToString();
            if ( dr == DialogResult.OK ) {
                // keep context menu open
                pictureBox_Cm.Show(pictureBox, _mouseButtonDownDown);
            }
            _beamSearchError = false;
        }
        void pictureBox_CmClickSwapProfiles(object sender, EventArgs e) {
            _settings.SwapProfiles = !_settings.SwapProfiles;
            ((MenuItem)sender).Checked = _settings.SwapProfiles;
        }
        void pictureBox_CmClickShowNative(object sender, EventArgs e) {
            ((MenuItem)sender).Checked = !((MenuItem)sender).Checked;
            if ( ((MenuItem)sender).Checked ) {
                // disable
                this.pictureBox_CmShowCrosshair.Enabled = false;
                this.pictureBox_CmEllipse.Enabled = false;
                this.pictureBox_CmPseudoColors.Enabled = false;
                this.pictureBox_CmForceGray.Enabled = false;
                this.pictureBox_CmSearchTraces.Enabled = false;
                this.pictureBox_CmEllipsePoints.Enabled = false;
                this.pictureBox_CmEllipsePointsHull.Enabled = false;
                this.pictureBox_CmBeamBorder.Enabled = false;
                this.pictureBox_CmBeamPeak.Enabled = false;
            } else {
                // enable
                this.pictureBox_CmShowCrosshair.Enabled = true;
                this.pictureBox_CmEllipse.Enabled = true;
                this.pictureBox_CmPseudoColors.Enabled = true;
                this.pictureBox_CmForceGray.Enabled = true;
                this.pictureBox_CmSearchTraces.Enabled = true;
                this.pictureBox_CmEllipsePoints.Enabled = true;
                this.pictureBox_CmEllipsePointsHull.Enabled = true;
                this.pictureBox_CmBeamBorder.Enabled = true;
                this.pictureBox_CmBeamPeak.Enabled = true;
            }
        }
        void pictureBox_CmClickShowCrosshair(object sender, EventArgs e) {
            _settings.Crosshair = !_settings.Crosshair;
            ((MenuItem)sender).Checked = _settings.Crosshair;
        }
        void pictureBox_CmClickCrosshaisFollowsBeam(object sender, EventArgs e) {
            _settings.FollowBeam = !_settings.FollowBeam;
            ((MenuItem)sender).Checked = _settings.FollowBeam;
            headLine();
        }
        void pictureBox_CmClickForceGray(object sender, EventArgs e) {
            _settings.ForceGray = !_settings.ForceGray;
            ((MenuItem)sender).Checked = _settings.ForceGray;
            if ( _settings.ForceGray ) {
                this.pictureBox_CmPseudoColors.Checked = false;
                _settings.PseudoColors = false;
            }
        }
        void pictureBox_CmClickPseudoColors(object sender, EventArgs e) {
            _settings.PseudoColors = !_settings.PseudoColors;
            ((MenuItem)sender).Checked = _settings.PseudoColors;
            if ( _settings.PseudoColors ) {
                this.pictureBox_CmForceGray.Checked = false;
                _settings.ForceGray = false;
            }
        }
        void pictureBox_CmClickEllipse(object sender, EventArgs e) {
            _settings.Ellipse = !_settings.Ellipse;
            ((MenuItem)sender).Checked = _settings.Ellipse;
        }
        void pictureBox_CmClickBeamPeak(object sender, EventArgs e) {
            _settings.BeamPeak = !_settings.BeamPeak;
            ((MenuItem)sender).Checked = _settings.BeamPeak;
        }
        void pictureBox_CmClickBeamBorder(object sender, EventArgs e) {
            _settings.BeamBorder = !_settings.BeamBorder;
            ((MenuItem)sender).Checked = _settings.BeamBorder;
            if ( _settings.BeamBorder ) {
                this.pictureBox_CmSearchTraces.Checked = false;
                _settings.SearchTraces = false;
                this.pictureBox_CmEllipsePoints.Checked = false;
                _settings.EllipsePower = false;
                this.pictureBox_CmEllipsePointsHull.Checked = false;
                _settings.EllipsePointsHull = false;
            }
        }
        void pictureBox_CmClickSearchTraces(object sender, EventArgs e) {
            _settings.SearchTraces = !_settings.SearchTraces;
            ((MenuItem)sender).Checked = _settings.SearchTraces;
            if ( _settings.SearchTraces ) {
                this.pictureBox_CmBeamBorder.Checked = false;
                _settings.BeamBorder = false;
                this.pictureBox_CmEllipsePoints.Checked = false;
                _settings.EllipsePower = false;
                this.pictureBox_CmEllipsePointsHull.Checked = false;
                _settings.EllipsePointsHull = false;
            }
        }
        void pictureBox_CmClickEllipsePoints(object sender, EventArgs e) {
            _settings.EllipsePower = !_settings.EllipsePower;
            ((MenuItem)sender).Checked = _settings.EllipsePower;
            if ( _settings.EllipsePower ) {
                this.pictureBox_CmBeamBorder.Checked = false;
                _settings.BeamBorder = false;
                this.pictureBox_CmSearchTraces.Checked = false;
                _settings.SearchTraces = false;
                this.pictureBox_CmEllipsePointsHull.Checked = false;
                _settings.EllipsePointsHull = false;
            }
        }
        void pictureBox_CmClickEllipsePointsHull(object sender, EventArgs e) {
            _settings.EllipsePointsHull = !_settings.EllipsePointsHull;
            ((MenuItem)sender).Checked = _settings.EllipsePointsHull;
            if ( _settings.EllipsePointsHull ) {
                this.pictureBox_CmBeamBorder.Checked = false;
                _settings.BeamBorder = false;
                this.pictureBox_CmSearchTraces.Checked = false;
                _settings.SearchTraces = false;
                this.pictureBox_CmEllipsePoints.Checked = false;
                _settings.EllipsePower = false;
            }
        }
        // hide beam search origin but keep the previously selected origin
        void pictureBox_CmClickShowSearchOrigin(object sender, EventArgs e) {
            ((MenuItem)sender).Checked = !((MenuItem)sender).Checked;
            updateSettingsFromApp();
        }
        // beam search from center
        void pictureBox_CmClickSearchCenter(object sender, EventArgs e) {
            ((MenuItem)sender).Checked = !((MenuItem)sender).Checked;
            pictureBox_CmSearchManual.Checked = !((MenuItem)sender).Checked;
            updateSettingsFromApp();
        }
        // beam search from offset 
        void pictureBox_CmClickSearchManual(object sender, EventArgs e) {
            ((MenuItem)sender).Checked = !((MenuItem)sender).Checked;
            pictureBox_CmSearchCenter.Checked = !((MenuItem)sender).Checked;
            updateSettingsFromApp();
        }

        // update beam search status from settings
        void updateBeamSearchOriginFromSettings() {
            switch ( _settings.BeamSearchOrigin ) {
                case AppSettings.BeamSearchStates.HIDE: {
                        pictureBox_CmShowSearchOrigin.Checked = !(_settings.BeamSearchOrigin == AppSettings.BeamSearchStates.HIDE); 
                        if ( _beamSearchOriginCurrent == new Point(0, 0) ) {
                            pictureBox_CmSearchCenter.Checked = true;
                            pictureBox_CmSearchManual.Checked = false;
                        } else {
                            pictureBox_CmSearchCenter.Checked = false;
                            pictureBox_CmSearchManual.Checked = true;
                        }
                        break;
                    }
                case AppSettings.BeamSearchStates.CENTER: {
                        pictureBox_CmShowSearchOrigin.Checked = true;
                        pictureBox_CmSearchCenter.Checked = true;
                        pictureBox_CmSearchManual.Checked = false;
                        _beamSearchOriginCurrent = new Point(0, 0);
                        break;
                    }
                case AppSettings.BeamSearchStates.MANUAL: {
                        pictureBox_CmShowSearchOrigin.Checked = true;
                        pictureBox_CmSearchCenter.Checked = false;
                        pictureBox_CmSearchManual.Checked = true;
                        _beamSearchOriginCurrent = _settings.BeamSearchOffset;
                        break;
                    }
                default: {
                        pictureBox_CmShowSearchOrigin.Checked = true;
                        pictureBox_CmSearchCenter.Checked = true;
                        pictureBox_CmSearchManual.Checked = false;
                        _beamSearchOriginCurrent = new Point(0, 0);
                        break;
                    }
            }
        }
        // update _settings from beam search status
        void updateSettingsFromBeamSearchOrigin() {
            // show beam search origin
            if ( pictureBox_CmShowSearchOrigin.Checked ) {
                // origin is either CENTER or MANUAL
                if ( _beamSearchOriginCurrent == _settings.BeamSearchOffset ) {
                    _settings.BeamSearchOrigin = AppSettings.BeamSearchStates.MANUAL;
                } else { 
                    _settings.BeamSearchOrigin = AppSettings.BeamSearchStates.CENTER;
                }
            } else { 
                // HIDE beam search origin
                _settings.BeamSearchOrigin = AppSettings.BeamSearchStates.HIDE;
                return;
            }
            // CENTER
            if ( pictureBox_CmSearchCenter.Checked ) {
                _settings.BeamSearchOrigin = AppSettings.BeamSearchStates.CENTER;
            }
             // MANUAL
            if ( pictureBox_CmSearchManual.Checked ) {
                _settings.BeamSearchOrigin = AppSettings.BeamSearchStates.MANUAL;
            }
        }

        // after MainForm is loaded, check for UVC devices
        private void MainForm_Load(object sender, EventArgs e)
        {
            // check for UVC devices
            getCameraBasics();
            // some controls
            EnableConnectionControls(true);
            // pictureBox gives a hint
            if ( Resources.start != null ) {
                pictureBox.Image = new Bitmap(Resources.start);
            }
        }

        // get UVC devices into combos
        void getCameraBasics()
        {
            this.devicesCombo.Items.Clear();

            // enumerate video devices
            _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            // no camera was found
            if ( _videoDevices.Count == 0 ) {
                this.devicesCombo.Items.Add("No UVC camera(s)");
                this.devicesCombo.SelectedIndexChanged -= new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
                this.devicesCombo.SelectedIndex = 0;
                this.devicesCombo.SelectedIndexChanged += new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
                videoResolutionsCombo.Items.Clear();
                // most recent camera disappeared, stop it running via connectButton_Click
                if ( _videoDevice != null && _videoDevice.IsRunning ) {
                    try {
                        this.connectButton.PerformClick();
                        _videoDevice = null;
                    } catch ( Exception ) {; }
                }
                return;
            }

            // loop all devices and add them to combo
            bool currentDeviceDisappeared = true;
            int indexToSelect = -1;
            int ndx = 0;
            foreach ( FilterInfo device in _videoDevices ) {
                this.devicesCombo.Items.Add(device.Name);
                if ( device.MonikerString == _settings.CameraMoniker ) {
                    indexToSelect = ndx;
                    currentDeviceDisappeared = false;
                }
                ndx++;
            }

            // if no camera according to _settings was found
            if ( indexToSelect == -1 ) {
                // 1st app start with empty INI
                if ( _settings.CameraMoniker == "empty" ) {
                    indexToSelect = 0;
                }
            }

            // null at 1st enter OR camera disappeared
            if ( _videoDevice == null ) {
                // selecting an index automatically calls devicesCombo_SelectedIndexChanged(..), which creates a new _videoDevice
                 this.devicesCombo.SelectedIndex = indexToSelect; 
            } else {
                // a new camera arrived or an existing camera disappeared
                if ( _videoDevice.IsRunning ) {
                    // the most recent camera is running ... 
                    if ( currentDeviceDisappeared ) {
                        // ... but it disappeared - other camera(s) might be available
                        try {
                            // stop disappeared camera
                            this.connectButton.PerformClick();
                            _videoDevice = null;
                        } catch (Exception) {;}
                        // do not select any other available camera
                        ;
                        // leave note
                        MessageBox.Show("No camera according to app settings found.", "Note");
                    } else {
                        // re select most recent camera, despite of other cameras
                        this.devicesCombo.SelectedIndexChanged -= new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
                        this.devicesCombo.SelectedIndex = indexToSelect;
                        this.devicesCombo.SelectedIndexChanged += new System.EventHandler(this.devicesCombo_SelectedIndexChanged);
                    }
                } else {
                    // no camera is running
                    if ( !currentDeviceDisappeared ) {
                        // only select indexToSelect, if it did not disappear - would otherwise select another available camera
                        this.devicesCombo.SelectedIndex = indexToSelect;
                    }
                }
            }
        }

        // closing the app
        private void MainForm_FormClosing( object sender, FormClosingEventArgs e )
        {
            // stop screenshot providing network socket server
            _runSocketTask = false;

            // IMessageFilter
            System.Windows.Forms.Application.RemoveMessageFilter(this);

            // stop camera
            if ( ( _videoDevice != null ) && _videoDevice.IsRunning ) {
                _videoDevice.SignalToStop();
                _videoDevice.NewFrame -= new AForge.Video.NewFrameEventHandler(videoDevice_NewFrame);
            }

            // INI: write to ini
            updateSettingsFromApp();
            _settings.WriteToIni();
        }

        // enable/disable camera connection related controls
        private void EnableConnectionControls( bool enable )
        {
            this.devicesCombo.Enabled = enable;
            this.videoResolutionsCombo.Enabled = enable;
            this.connectButton.Text = enable ? _buttonConnectString : "-- stop --";
        }

        // a video device was selected: either on purpose by user or at app start
        private void devicesCombo_SelectedIndexChanged( object sender, EventArgs e )
        {
            // sanity checks
            if ( _videoDevices.Count != 0 && this.devicesCombo.SelectedIndex >= 0 ) {
                // create _videoDevice object
                _videoDevice = new VideoCaptureDevice(_videoDevices[this.devicesCombo.SelectedIndex].MonikerString);
                // check and if needed, sync app _settings with camera props
                evaluateCameraAndSettings();
            }
        }

        // check and if needed, sync _settings with camera props
        void evaluateCameraAndSettings() {
            // camera or its properties might have changed: update _settings accordingly
            _headlineAlert = "";
            // _videoDevice moniker may not match to app _settings
            if ( _settings.CameraMoniker != _videoDevices[devicesCombo.SelectedIndex].MonikerString ) {
                _settings.CameraMoniker = _videoDevices[devicesCombo.SelectedIndex].MonikerString;
                _headlineAlert = "model";
            }
            // _videoDevice resolution may not match to app _settings
            if ( !evaluateCameraFrameSizes(_videoDevice) ) {
                _headlineAlert += _headlineAlert.Length > 0 ? ", " : "";
                _headlineAlert += "resolution";
            }
            // _videoDevice exposure / brightness may not match to app _settings
            if ( !evaluateCameraExposureBrightnessProps() ) {
                _headlineAlert += _headlineAlert.Length > 0 ? ", " : "";
                _headlineAlert += "exposure";
            }
            // camera vs. _settings mismatch note
            if ( _headlineAlert.Length > 0 ) {
                _headlineAlert = String.Format("Camera '{0}' did not match to app settings, latter are now updated.", _headlineAlert);
            }
        }

        // collect supported video sizes
        private bool evaluateCameraFrameSizes( VideoCaptureDevice videoDevice )
        {
            bool retVal;
            this.Cursor = Cursors.WaitCursor;
            Size foundSize = new Size();  
            this.videoResolutionsCombo.Items.Clear();
            try {
                int indexToSelect = -1;
                int ndx = 0;
                foreach ( VideoCapabilities capabilty in videoDevice.VideoCapabilities ) {
                    string currRes = string.Format("{0} x {1}", capabilty.FrameSize.Width, capabilty.FrameSize.Height);
                    // for unknown reason 'videoDevice.VideoCapabilities' sometimes contains all resolutions of a given camera twice
                    if ( this.videoResolutionsCombo.FindString(currRes) == -1 ) {
                        this.videoResolutionsCombo.Items.Add(currRes);
                    }
                    // match to settings
                    if ( currRes == String.Format("{0} x {1}", _settings.CameraResolution.Width, _settings.CameraResolution.Height) ) {
                        indexToSelect = ndx;
                    }
                    ndx++;
                }
                if ( indexToSelect != -1 ) {
                    // match
                    this.videoResolutionsCombo.SelectedIndex = indexToSelect;
                    retVal = true;
                } else {
                    // no match
                    this.videoResolutionsCombo.SelectedIndex = 0;
                    _settings.CameraResolution = foundSize;
                    retVal = false;
                }
            } finally {
                this.Cursor = Cursors.Default;
            }
            return retVal;
        }

        // camera resolution was changed
        private void videoResolutionsCombo_SelectedIndexChanged( object sender, EventArgs e )
        {
            // get altered video resolution
            if ( (_videoDevice.VideoCapabilities != null) && (_videoDevice.VideoCapabilities.Length != 0) ) {
                _videoDevice.VideoResolution = _videoDevice.VideoCapabilities[this.videoResolutionsCombo.SelectedIndex];
                _settings.CameraResolution = new Size(_videoDevice.VideoCapabilities[this.videoResolutionsCombo.SelectedIndex].FrameSize.Width, 
                                                      _videoDevice.VideoCapabilities[this.videoResolutionsCombo.SelectedIndex].FrameSize.Height);
            }
        }

        //
        // "Connect" button clicked: starts or stops image processing
        //
        private void connectButton_Click( object sender, EventArgs e )
        {
            // stop still image timer
            this.timerStillImage.Stop();

            // restore pictureBox in case of 'Big Red Cross' exception
            ResetExceptionState(this.pictureBox);

            if ( (_buttonConnectString == this.connectButton.Text) && (sender != null) ) {
                // clear alert msg
                _headlineAlert = "";
                _beamSearchError = false;
                // connect to camera if feasible
                if ( (_videoDevice == null) || (_videoDevice.VideoCapabilities == null) || (_videoDevice.VideoCapabilities.Length == 0) || (this.videoResolutionsCombo.Items.Count == 0) ) {
                    return;
                }
                _videoDevice.VideoResolution = _videoDevice.VideoCapabilities[videoResolutionsCombo.SelectedIndex];
                _videoDevice.Start();
                _videoDeviceJustConnected = true;
                _videoDevice.NewFrame += new AForge.Video.NewFrameEventHandler(videoDevice_NewFrame);

                // crosshair and ellipse pen thickness
                if ( _videoDevice.VideoResolution.FrameSize.Width <= 1024 ) {
                    _crosshairPen.Width = 1;
                    _ellipsePen.Width = 1;
                } else {
                    _crosshairPen.Width = 3;
                    _ellipsePen.Width = 3;
                }

                // fire & forget: in case, the _videoDevice won't start within 10s or _videoDeviceJustConnected is still true (aka no videoDevice_NewFrame event)
                Task.Delay(10000).ContinueWith(t => {
                    Invoke(new Action(async () => {
                        // trigger for delayed action: camera is clicked on, aka  shows '- stop -'  AND camera is not running OR no new frame event happened
                        if ( _buttonConnectString != this.connectButton.Text && (!_videoDevice.IsRunning || _videoDeviceJustConnected) ) {
                            if ( _videoDeviceRestartCounter < 5 ) {
                                _videoDeviceRestartCounter++;
                                if ( !_videoDevice.IsRunning ) {
                                    GrzTools.Logger.logTextLn(DateTime.Now, String.Format("connectButton_Click: _videoDevice is not running"));
                                }
                                if ( _videoDeviceJustConnected ) {
                                    GrzTools.Logger.logTextLn(DateTime.Now, String.Format("connectButton_Click: no videoDevice_NewFrame event received"));
                                }
                                // stop camera 
                                this.connectButton.PerformClick();
                                // wait
                                await Task.Delay(600);
                                // reset all camera properties to camera default values:
                                // !! OV5640 vid_05a3&pid_9520 stops working after gotten fooled with awkward exposure params !!
                                setCameraDefaultProps();
                                // start camera
                                this.connectButton.PerformClick();
                            } else {
                                // give up note
                                GrzTools.Logger.logTextLn(DateTime.Now, String.Format("connectButton_Click: _videoDeviceRestartCounter >= 5, giving up in current app session"));
                                // stop camera
                                this.connectButton.PerformClick();
                                this.Text = "!! Camera failure !!";
                            }
                        }
                    }));
                });

                // disable camera controls
                EnableConnectionControls(false);
                // start screenshot socket server, runs in a separate task
                if ( _settings.ProvideSocketScreenshots ) {
                    _runSocketTask = true;
                    _socketTask = Task.Run(() => sendSocketScreenshotAsync());
                    _socketTask.Start();
                }
            } else {
                // disconnect means stop video device
                if ( _videoDevice.IsRunning ) {
                    _videoDevice.NewFrame -= new AForge.Video.NewFrameEventHandler(videoDevice_NewFrame);
                    _videoDevice.SignalToStop();
                    _fps = 0;
                }
                // enable some controls
                EnableConnectionControls(true);
                // stop screenshot socket server
                _runSocketTask = false;
            }
        }

        // full window screenshot
        public Bitmap thisScreenShot()
        {
            Point location = new Point();
            Invoke(new Action(() =>
            {
                location = new Point(this.Bounds.Left, this.Bounds.Top);
            }));
            Bitmap bmp = new Bitmap(this.Width, this.Height, PixelFormat.Format32bppArgb);
            Graphics g = Graphics.FromImage(bmp);
            g.CopyFromScreen(location.X, location.Y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }
        // show current snapshot modeless in new window
        private void snapshotButton_Click( object sender, EventArgs e )
        {
            if ( _bmp == null ) {
                return;
            }
            try {
                Bitmap snapshot = thisScreenShot();
                SnapshotForm snapshotForm = new SnapshotForm(snapshot);
                snapshotForm.Show();
            } catch {
                ;
            } 
        }

        // camera exposure mode flag
        private CameraControlFlags getCameraExposureMode() {
            if ( _videoDevice == null ) {
                return CameraControlFlags.None;
            }
            CameraControlFlags flag;
            int intValue;
            _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out intValue, out flag);
            return flag;
        }

        // evaluate camera exposure & brightness against _settings
        bool evaluateCameraExposureBrightnessProps() {
            bool retVal = true;

            int minValue;
            int maxValue;
            int stepSize;
            int outValue;
            int setValue;
            CameraControlFlags controlFlags;
            // get camera exposure parameters
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out minValue, out maxValue, out stepSize, out outValue, out controlFlags);
            _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out setValue, out controlFlags);
            // compare camera props with _settings
            if ( minValue != _settings.ExposureMin || maxValue != _settings.ExposureMax || setValue != _settings.Exposure ) {
                retVal = false;
                // get camera exposure auto mode
                controlFlags = getCameraExposureMode();
                this.hScrollBarExposure.Maximum = maxValue;
                _settings.ExposureMin = maxValue;
                this.hScrollBarExposure.Minimum = minValue;
                _settings.ExposureMax = minValue;
                this.hScrollBarExposure.SmallChange = stepSize;
                this.hScrollBarExposure.LargeChange = stepSize;
                this.hScrollBarExposure.Value = setValue;
                _settings.Exposure = setValue;
                if ( controlFlags == CameraControlFlags.Auto ) {
                    // leave camera in exposure auto mode
                    _videoDevice.SetCameraProperty(CameraControlProperty.Exposure, setValue, CameraControlFlags.Auto);
                }
            }
            // get camera brightness parameters
            VideoProcAmpFlags controlFlagsVideo;
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Brightness, out minValue, out maxValue, out stepSize, out outValue, out controlFlagsVideo);
            _videoDevice.GetVideoProperty(VideoProcAmpProperty.Brightness, out setValue, out controlFlagsVideo);
            if ( minValue != _settings.BrightnessMin || maxValue != _settings.BrightnessMax || setValue != _settings.Brightness ) {
                retVal = false;
                this.hScrollBarBrightness.Maximum = maxValue;
                _settings.BrightnessMax = maxValue;
                this.hScrollBarBrightness.Minimum = minValue;
                _settings.BrightnessMin = minValue;
                this.hScrollBarBrightness.Value = setValue;
                _settings.Brightness = setValue;
                this.hScrollBarBrightness.SmallChange = stepSize;
                this.hScrollBarBrightness.LargeChange = stepSize;
            }
            // update related labels & tooltips
            updateExposureUI();
            updateBrightnessUI();

            return retVal;
        }

        // force camera to set all its properties to default values: useful if camera won't start due to awkward settings
        private void setCameraDefaultProps() {
            if ( _videoDevice == null ) {
                return;
            }

            // camera props
            int min, max, step, def;
            CameraControlFlags cFlag;
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Exposure, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Exposure, def, CameraControlFlags.Auto);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Focus, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Focus, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Iris, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Iris, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Pan, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Pan, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Roll, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Roll, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Tilt, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Tilt, def, CameraControlFlags.Manual);
            _videoDevice.GetCameraPropertyRange(CameraControlProperty.Zoom, out min, out max, out step, out def, out cFlag);
            _videoDevice.SetCameraProperty(CameraControlProperty.Zoom, def, CameraControlFlags.Manual);

            // video props
            VideoProcAmpFlags vFlag;
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.BacklightCompensation, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.BacklightCompensation, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Brightness, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Brightness, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.ColorEnable, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.ColorEnable, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Contrast, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Contrast, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Gain, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Gain, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Gamma, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Gamma, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Hue, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Hue, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Saturation, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Saturation, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.Sharpness, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Sharpness, def, VideoProcAmpFlags.Manual);
            _videoDevice.GetVideoPropertyRange(VideoProcAmpProperty.WhiteBalance, out min, out max, out step, out def, out vFlag);
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.WhiteBalance, def, VideoProcAmpFlags.Auto);

            // update related UI controls
            int value;
            CameraControlFlags controlFlags;
            _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out value, out controlFlags);
            this.hScrollBarExposure.Value = value;
            VideoProcAmpFlags controlFlagsVideo;
            _videoDevice.GetVideoProperty(VideoProcAmpProperty.Brightness, out value, out controlFlagsVideo);
            this.hScrollBarBrightness.Value = value;
            // needed to update the scroller according to the new value
            this.PerformLayout();
            // save to settings
            updateSettingsFromApp();
            // update labels and tooltips
            updateExposureUI();
            updateBrightnessUI();

            MessageBox.Show("Camera was reset to default values.", "Camera failure");
        }

        // update exposure & brightness UI labels and tooltips
        void updateExposureUI() {
            bool autoMode = getCameraExposureMode() == CameraControlFlags.Auto;
            this.labelCameraExposure.Text = "camera exposure: " + (autoMode ? " auto" : _settings.Exposure.ToString());
            this.toolTip.SetToolTip(this.hScrollBarExposure,
                                    "camera exposure time = " + _settings.Exposure.ToString() +
                                    " (" + this.hScrollBarExposure.Minimum.ToString() +
                                    ".." + this.hScrollBarExposure.Maximum.ToString() + ")" +
                                    (autoMode ? " auto on" : " manual"));
        }
        void updateBrightnessUI() {
            this.labelImageBrightness.Text = "image brightness: " + _settings.Brightness.ToString();
            this.toolTip.SetToolTip(this.hScrollBarBrightness,
                                    "image brightness " +
                                    _settings.Brightness.ToString() +
                                    " (" + this.hScrollBarBrightness.Minimum.ToString() + ".." +
                                    this.hScrollBarBrightness.Maximum.ToString() + ")");
        }

        // show extended camera props dialog
        private void buttonCameraProperties_Click( object sender, EventArgs e )
        {
            if ( _videoDevice != null ) {
                // DisplayPropertyPage is app modal, if parameter is an app handle
                //   !! needs a fix in VideoCaptureDevice.cs "if ( parentWindow == IntPtr.Zero )"
                try {
                    _videoDevice.DisplayPropertyPage(this.Handle);
                } catch {
                    MessageBox.Show("Cannot connect to camera properties.", "Error");
                }
                // since DisplayPropertyPage is app modal, get camera exposure & brightness values could be set after above dlg was closed
                int value;
                CameraControlFlags controlFlags;
                _videoDevice.GetCameraProperty(CameraControlProperty.Exposure, out value, out controlFlags);
                this.hScrollBarExposure.Value = value;
                VideoProcAmpFlags controlFlagsVideo;
                _videoDevice.GetVideoProperty(VideoProcAmpProperty.Brightness, out value, out controlFlagsVideo);
                this.hScrollBarBrightness.Value = value;
                // save to settings
                updateSettingsFromApp();
                // update labels and tooltips
                updateExposureUI();
                updateBrightnessUI();
            }
        }

        // set camera exposure manually via UI scrollers
        private void hScrollBarExposure_Scroll(object sender, ScrollEventArgs e) {
            if ( _videoDevice == null ) {
                return;
            }
            CameraControlFlags ccf = getCameraExposureMode();
            // no need to stress camera settings
            if ( e.OldValue == e.NewValue && ccf == CameraControlFlags.Manual ) {
                return;
            }
            // switching from auto exposure to manual may need a camera restart
            if ( ccf == CameraControlFlags.Auto && _videoDevice.IsRunning ) {
                this.connectButton.PerformClick();
                Task.Run(() => MessageBox.Show("Leaving exposure auto mode, takes effect after re connecting to the camera.", "Note"));
            }
            // update camera
            _videoDevice.SetCameraProperty(CameraControlProperty.Exposure, e.NewValue, CameraControlFlags.Manual);
            // update _settings
            _settings.Exposure = e.NewValue;
            // update label and tooltip
            updateExposureUI();
        }
        // set camera brightness manually via UI scrollers
        private void hScrollBarBrightness_Scroll(object sender, ScrollEventArgs e) {
            if ( _videoDevice == null ) {
                return;
            }
            // no need to stress camera settings
            if ( e.OldValue == e.NewValue ) {
                return;
            }
            // http://www.aforgenet.com/forum/viewtopic.php?f=2&t=2939
            // https://code.google.com/archive/p/aforge/issues/357#makechanges mods were needed to get brightness adjustable
            _videoDevice.SetVideoProperty(VideoProcAmpProperty.Brightness, e.NewValue, VideoProcAmpFlags.Manual);
            _settings.Brightness = e.NewValue;
            // update label and tooltip
            updateBrightnessUI();
        }

        // allows gray images rendered with pseudo colors
        Bitmap bitmapFromPalette(palette pal)
        {
            Bitmap bmp = new Bitmap(256, 20);
            for ( int x = 0; x < 256; x++ ) {
                for ( int y = 0; y < 20; y++ ) {
                    bmp.SetPixel(x, y, Color.FromArgb(pal.mapR[x], pal.mapG[x], pal.mapB[x]));
                }
            }
            return bmp;
        }
        public class palette
        {
            public byte[] mapR = new byte[256];
            public byte[] mapG = new byte[256];
            public byte[] mapB = new byte[256];
        }
        // pseudo color sequence: black .. purple .. blue .. cyan .. green .. yellow .. red .. white
        palette buildPaletteSpectrum()
        {
/*
 *           +   +   +               255 
 *       -               -   -   -     0 blue channel
 *       
 * 
 *                   +   +   +        255
 *       -   -   -               -      0 green channel
 * 
 * 
 *           +               +   +    255
 *       -       -   -   -              0 red channel
 * 
 * 
 *      Bl  Pu  Bl  Cy  Gn  Ye  Re   <-- resulting pseudo colrs
 * 
 *       0  42  85 127 170 212 255   <-- 6 steps each 42.5 levels increasing, when looping i levels
 * 
 * 
*/ 
            palette pal = new palette();
            byte[] slope = new byte[] {1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 11, 11, 12, 11, 11, 10, 9, 9, 8, 8, 7, 7, 6, 6, 5, 5, 4, 4, 3, 3, 2, 2, 1, 1};

            pal.mapR[0] = 40;
            pal.mapG[0] = 40;
            pal.mapB[0] = 40;
            
            int slopeNdx = 0;
            byte R = 0;
            byte G = 0;
            byte B = 0;
             
            for ( int i = 1; i < 256; i++ ) {
             
                // save calculated values
                pal.mapR[i] = R;
                pal.mapG[i] = G;
                pal.mapB[i] = B;

                // calculate color values 
                if ( i < 42 ) {
                    // black .. purple: r.up g.down b.up
                    R = (byte)Math.Min(255, R + slope[slopeNdx]);
                    B = (byte)Math.Min(255, B + slope[slopeNdx]); 
                    slopeNdx++;
                } else {
                    if ( i == 42 ) {
                        slopeNdx = 0;
                    }
                    if ( i < 85 ) {
                        // purple .. blue: r.down g.low b.high
                        R = (byte)Math.Max(0, R - slope[slopeNdx]); 
                        slopeNdx++;
                    } else {
                        if ( i == 85 ) {
                            slopeNdx = 0;
                        }
                        if ( i < 127 ) {
                            // blue .. cyan: r.low g.up b.high
                            G = (byte)Math.Min(255, G + slope[slopeNdx]); 
                            slopeNdx++;
                        } else {
                            if ( i == 127 ) {
                                slopeNdx = 0;
                            }
                            if ( i < 170 ) {
                                // cyan .. green: r.low g.high b.down
                                B = (byte)Math.Max(0, B - slope[slopeNdx]); 
                                slopeNdx++;
                            } else {
                                if ( i == 170 ) {
                                    slopeNdx = 0;
                                }
                                if ( i < 212 ) {
                                    // green .. yellow: r.up g.high b.low
                                    R = (byte)Math.Min(255, R + slope[slopeNdx]); 
                                    slopeNdx++;
                                } else {
                                    if ( i == 212 ) {
                                        slopeNdx = 0;
                                    }
                                    if ( i < 255 ) {
                                        // yellow .. red: r.high g.down b.low
                                        G = (byte)Math.Max(0, G - slope[slopeNdx]); 
                                        slopeNdx++;
                                        // make red slightly more dramatic
                                        if ( i > 230 ) {
                                            R -= 2;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // make sure white
            pal.mapR[253] = 255;
            pal.mapG[253] = 255;
            pal.mapB[253] = 255;
            pal.mapR[254] = 255;
            pal.mapG[254] = 255;
            pal.mapB[254] = 255;
            pal.mapR[255] = 255;
            pal.mapG[255] = 255;
            pal.mapB[255] = 255;

            return pal;
        }
        // pseudo color sequence: black .. blue .. cyan .. green .. yellow .. purple .. red .. white
        palette buildPaletteTemperature()
        {
            palette pal = new palette();
            byte[] slope = new byte[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 11, 11, 12, 11, 11, 10, 9, 9, 8, 8, 7, 7, 6, 6, 5, 5, 4, 4, 3, 3, 2, 2, 1, 1 };
            int slopeNdx = 0;

            pal.mapR[0] = 40;
            pal.mapG[0] = 40;
            pal.mapB[0] = 40;
            
            byte R = 0;
            byte G = 0;
            byte B = 0;
            for ( int i = 1; i < 256; i++ ) {

                // save calculated values
                pal.mapR[i] = R;
                pal.mapG[i] = G;
                pal.mapB[i] = B;

                // calculate color values 
                if ( i < 42 ) {
                    // black .. blue: r.down g.down b.up
                    B = (byte)Math.Min(255, B + slope[slopeNdx]);
                    slopeNdx++;
                } else {
                    if ( i == 42 ) {
                        slopeNdx = 0;
                    }
                    if ( i < 85 ) {
                        // blue .. cyan: r.low g.up b.high
                        G = (byte)Math.Min(255, G + slope[slopeNdx]);
                        slopeNdx++;
                    } else {
                        if ( i == 85 ) {
                            slopeNdx = 0;
                        }
                        if ( i < 127 ) {
                            // cyan .. green: r.low g.high b.down
                            B = (byte)Math.Max(0, B - slope[slopeNdx]);
                            slopeNdx++;
                        } else {
                            if ( i == 127 ) {
                                slopeNdx = 0;
                            }
                            if ( i < 170 ) {
                                // green .. yellow: r.up g.high b.low
                                R = (byte)Math.Min(255, R + slope[slopeNdx]);
                                slopeNdx++;
                            } else {
                                if ( i == 170 ) {
                                    slopeNdx = 0;
                                }
                                if ( i < 212 ) {
                                    // yellow .. purple: r.high g.down b.up
                                    G = (byte)Math.Max(0, G - slope[slopeNdx]);
                                    B = (byte)Math.Min(255, B + slope[slopeNdx]);
                                    slopeNdx++;
                                } else {
                                    if ( i == 212 ) {
                                        slopeNdx = 0;
                                    }
                                    if ( i < 255 ) {
                                        // purple .. red: r.high g.low b.down
                                        B = (byte)Math.Max(0, B - slope[slopeNdx]);
                                        slopeNdx++;
                                        // make red slightly more dramatic
                                        if ( i > 230 ) {
                                            R -= 2;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // make sure white
            pal.mapR[253] = 255;
            pal.mapG[253] = 255;
            pal.mapB[253] = 255;
            pal.mapR[254] = 255;
            pal.mapG[254] = 255;
            pal.mapB[254] = 255;
            pal.mapR[255] = 255;
            pal.mapG[255] = 255;
            pal.mapB[255] = 255;

            return pal;
        }

        // Bitmap ring buffer for 5 images
        static class BmpRingBuffer {
            private static Bitmap[] bmpArray = new Bitmap[] { new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1) };
            private static int bmpNdx = 0;
            // public get & set
            public static Bitmap bmp {
                // always return the penultimate bmp
                get {
                    int prevNdx = bmpNdx - 1;
                    if ( prevNdx < 0 ) {
                        prevNdx = 4;
                    }
                    return bmpArray[prevNdx];
                }
                // override bmp in array and increase array index
                set {
                    bmpArray[bmpNdx].Dispose();
                    bmpArray[bmpNdx] = value;
                    bmpNdx++;
                    if ( bmpNdx > 4 ) {
                        bmpNdx = 0;
                    }
                }
            }
        }

        // camera 'new frame' event handler
        void videoDevice_NewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs)
        {
            try {

                // make sure correct pixel format
                Bitmap bmp = (Bitmap)eventArgs.Frame.Clone();

                if ( bmp.PixelFormat != PixelFormat.Format24bppRgb ) {
                    bmp = GrzTools.BitmapTools.ConvertTo24bpp(bmp);
                }

                // put latest image into ring buffer
                BmpRingBuffer.bmp = bmp;

                // action after the first received image 
                if ( _videoDeviceJustConnected ) {

                    // wrong image format
                    if ( BmpRingBuffer.bmp.PixelFormat != PixelFormat.Format24bppRgb ) {
                        MessageBox.Show("If possible, camera shall provide 'PixelFormat.Format24bppRgb'.", "Note");
                    }

                    //
                    // start image processing outside of UI, no need to wait for completion 
                    //
                    Task.Run(() => cameraImageGrabber());
                }

            } catch (Exception) {
            }
        }

        // image grabber for motion detection
        //       runs independent from camera 'new frame event' to ensure frame rate being exact 2fps
        //       does not block UI due to Task.Run
        async void cameraImageGrabber() {
            // stopwatch
            System.Diagnostics.Stopwatch swFrameProcessing = new System.Diagnostics.Stopwatch();
            DateTime lastFrameTime = DateTime.Now;
            long procMs = 0;
            int excStep = -1;
            bool bmpArrFilled = false;

            //
            // loop as long as camera is running
            //
            while ( _videoDevice != null && _videoDevice.IsRunning ) {

                // calc fps
                DateTime now = DateTime.Now;
                double revFps = (double)(now - lastFrameTime).TotalMilliseconds;
                lastFrameTime = now;
                _fps = 1000.0f / revFps;
                procMs = 0;
                excStep = -1;
                bmpArrFilled = false;

                // prepare to measure consumed time for image processing
                swFrameProcessing.Restart();

                try {

                    // avoid Exception when GC is too slow
                    excStep = 0;
                    if ( _bmp != null ) {
                        _bmp.Dispose();
                    }

                    // get frame from BmpRingBuffer
                    excStep = 1;
                    _bmp = (Bitmap)BmpRingBuffer.bmp.Clone();
                    // the Frame's Bitmap size is needed in dozens of places, this way we avoid to call the Bitmap object in such cases
                    _bmpSize = new Size(_bmp.Width, _bmp.Height);

                    // action after the first received image 
                    excStep = 2;
                    if ( _videoDeviceJustConnected ) {
                        // reset flag
                        _videoDeviceJustConnected = false;
                        // if 1st time connected, adjust the canvas matching to the presumable new aspect ratio of the bmp
                        Invoke(new Action(() => adjustMainFormSize(_bmpSize) ));
                        // init just once
                        _bmpArr = new byte[_bmp.Width * _bmp.Height];
                    }

                    // show native image only
                    if ( this.pictureBox_CmShowNative.Checked ) {
                        _headlineAlert = "native camera image";
                        this.pictureBox.Image = _bmp;
                        continue;
                    }

#if DEBUG
                    long initTime = swFrameProcessing.ElapsedMilliseconds;
                    swFrameProcessing.Restart();
#endif

                    // exec background subtraction and build a new bitmap from it
                    excStep = 3;
                    if ( _settings.SubtractBackgroundImage && _bmpBkgnd != null ) {
                        GrzTools.BitmapTools.SubtractBitmap24bppToBitmap24bpp(ref _bmp, _bmpBkgnd);
                    }
#if DEBUG
                    long bkgndTime = swFrameProcessing.ElapsedMilliseconds;
                    swFrameProcessing.Restart();
#endif
                    // force monochrome camera image
                    excStep = 4; 
                    if ( _settings.ForceGray ) {
                        GrzTools.BitmapTools.Bmp24bppColorToBmp24bppGrayToArray8bppGray(ref _bmp, ref _bmpArr);
                        bmpArrFilled = true;
                    }
# if DEBUG
                    long grayTime = swFrameProcessing.ElapsedMilliseconds;
                    swFrameProcessing.Restart();
#endif
                    // generate pseudo color image if selected
                    excStep = 5;
                    if ( _settings.PseudoColors ) {
                        GrzTools.BitmapTools.Bmp24bppColorToBmp24bppPseudoToArray8bppGray(ref _bmp, _pal, ref _bmpArr);
                        bmpArrFilled = true;
                    }
#if DEBUG
                    long pseudoTime = swFrameProcessing.ElapsedMilliseconds;
                    swFrameProcessing.Restart();
#endif
                    // ensure array 24bpp gray
                    excStep = 6;
                    if ( !bmpArrFilled ) {
                        GrzTools.BitmapTools.Bmp24bppColorToArray8bppGray(_bmp, ref _bmpArr);
                        bmpArrFilled = true;
                    }
#if DEBUG
                    long arrayTime = swFrameProcessing.ElapsedMilliseconds;
                    swFrameProcessing.Restart();
#endif
                    // dispose the previous image
                    excStep = 7;
                    if ( this.pictureBox.Image != null ) {
                        this.pictureBox.Image.Dispose();
                    }

                    // full image processing: heavily modifies _bmp
                    excStep = 8;
                    await Task.Run(() => pictureBox_PaintWorker());
#if DEBUG
                    long workerTime = swFrameProcessing.ElapsedMilliseconds;
                    swFrameProcessing.Restart();
#endif
                    excStep = 9;
                    Invoke(new Action(() => {
                        // set _bmp to pictureBox
                        Task.Run(() => this.pictureBox.Image = (Bitmap)_bmp.Clone());
                    }));

                    // get process time in ms
#if DEBUG
                    long paintTime = swFrameProcessing.ElapsedMilliseconds;
                    procMs = initTime + bkgndTime + grayTime + pseudoTime + arrayTime + workerTime + paintTime;
                    swFrameProcessing.Stop();
#else
                    procMs = swFrameProcessing.ElapsedMilliseconds;
                    swFrameProcessing.Stop();
#endif

                    // update window title
                    excStep = 10;
                    Invoke(new Action(() => {
                        headLine();
                    }));

                } catch ( Exception ex ) {
                    GrzTools.Logger.logTextLnU(now, String.Format("cameraImageGrabber: excStep={0} {1}", excStep, ex.Message));
                } finally {
                    // cooperative sleep for '500ms - process time' to ensure 2fps
                    await Task.Delay(Math.Max(0, 500 - (int)procMs));
                }
            }
        }

        // added to the current video frame output on screen: extended crosshair axis + vertical & horizontal power profiles
        private void tableLayoutPanelGraphs_Paint(object sender, PaintEventArgs e)
        {
            // intensity profile areas will render on a black background to the left and bottom of camera image
            e.Graphics.FillRectangle(Brushes.Black, new Rectangle(0, 0, 104, this.tableLayoutPanelGraphs.ClientSize.Height));
            e.Graphics.FillRectangle(Brushes.Black, new Rectangle(0, this.tableLayoutPanelGraphs.ClientSize.Height - 104, this.tableLayoutPanelGraphs.ClientSize.Width, 104));

            // 2x intensity axis scaling
            Pen pen = new Pen(Brushes.Gray);
            pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
            for ( int i = 0; i < 6; i++ ) {
                e.Graphics.DrawLine(pen, new Point(20 * i, 0), new Point(20 * i, this.tableLayoutPanelGraphs.ClientSize.Height - 104));
                e.Graphics.DrawLine(pen, new Point(104, this.tableLayoutPanelGraphs.ClientSize.Height - 20 * i - 1), new Point(this.tableLayoutPanelGraphs.ClientSize.Width, this.tableLayoutPanelGraphs.ClientSize.Height - 20 * i - 1));
            }

            // render the beam intensity profile arrays _horProf & _verProf contain _bmp coordinates, previously generated in pictureBox_PaintWorker
            int ofs;
            try {
                if ( _epCv.RotatedRect.Center.X > 0 ) {
                    // show intensity profiles along both ellipse's main axis
                    if ( (_horProf != null) && (_horProf.Length > 0) ) {
                        Point[] points = new Point[_horProf.Length];
                        int ndx = 0;
                        double lastX = 0;
                        double gray, xpos, ypos;
                        foreach ( Point pt in _horProf ) {
                            ofs = pt.Y * _bmpSize.Width + pt.X;
                            if ( (ofs > 0) && (ofs < _bmpArr.Length) ) {
                                gray = _bmpArr[ofs];
                                xpos = Math.Round((double)pt.X * _multiplierBmp2Paint) + 104;
                                ypos = Math.Round((double)this.tableLayoutPanelGraphs.ClientSize.Height - (gray / 2.5f) - 1);
                                ypos = Math.Min(this.tableLayoutPanelGraphs.ClientSize.Height, Math.Max(ypos, 0));
                                points[ndx] = new Point((int)xpos, (int)ypos);
                                lastX = xpos;
                            } else {
                                points[ndx] = new Point((int)lastX, this.tableLayoutPanelGraphs.ClientSize.Height);
                            }
                            ndx++;
                        }
                        e.Graphics.DrawLines(Pens.Yellow, points);
                    }
                    if ( (_verProf != null) && (_verProf.Length > 0) ) {
                        Point[] points = new Point[_verProf.Length];
                        int ndx = 0;
                        double lastY = 0;
                        double gray, xpos, ypos;
                        foreach ( Point pt in _verProf ) {
                            ofs = pt.Y * _bmpSize.Width + pt.X;
                            if ( (ofs > 0) && (ofs < _bmpArr.Length) ) {
                                gray = _bmpArr[ofs];
                                xpos = Math.Round(gray / 2.5f);
                                ypos = Math.Round((double)pt.Y * _multiplierBmp2Paint);
                                points[ndx] = new Point((int)xpos, (int)ypos);
                                lastY = ypos;
                            } else {
                                points[ndx] = new Point(0, (int)lastY);
                            }
                            ndx++;
                        }
                        e.Graphics.DrawLines(Pens.Yellow, points);
                    }
                }

                // extend red lines coming from the beam's peak intensity location into the intensity profile scale
                if ( _crosshairCenter.X != -1 ) {
                    e.Graphics.DrawLine(Pens.Red, 
                                        0, 
                                        _crosshairCenter.Y * _multiplierBmp2Paint, 
                                        104, 
                                        _crosshairCenter.Y * _multiplierBmp2Paint);
                    e.Graphics.DrawLine(Pens.Red, 
                                        104 + _crosshairCenter.X * _multiplierBmp2Paint, 
                                        this.tableLayoutPanelGraphs.ClientSize.Height - 104, 
                                        104 + _crosshairCenter.X * _multiplierBmp2Paint, 
                                        this.tableLayoutPanelGraphs.ClientSize.Height);
                }

                // show coordinates & intensity value of cross center in the lower left corner
                e.Graphics.DrawLine(Pens.Yellow, 0, this.tableLayoutPanelGraphs.ClientSize.Height - 1, 100, this.tableLayoutPanelGraphs.ClientSize.Height - 1);
                e.Graphics.DrawLine(Pens.Yellow, 0, this.tableLayoutPanelGraphs.ClientSize.Height - 101, 100, this.tableLayoutPanelGraphs.ClientSize.Height - 101);
                e.Graphics.DrawLine(Pens.Yellow, 0, this.tableLayoutPanelGraphs.ClientSize.Height - 101, 0, this.tableLayoutPanelGraphs.ClientSize.Height - 1);
                e.Graphics.DrawLine(Pens.Yellow, 100, this.tableLayoutPanelGraphs.ClientSize.Height - 101, 100, this.tableLayoutPanelGraphs.ClientSize.Height - 1);

                int yofs = this.tableLayoutPanelGraphs.ClientSize.Height - 98;
                // ellipse center
                string ctrX = "";
                string ctrY = "";
                if ( _epCv.RotatedRect.Center.X > 0 ) {
                    ctrX = ((int)(_epCv.RotatedRect.Center.X)).ToString();
                    ctrY = ((int)(_epCv.RotatedRect.Center.Y)).ToString();
                }
                string addSpace = (ctrX.Length + ctrY.Length) > 7 ? "" : " ";
                e.Graphics.DrawString(String.Format("center   {0}{1} {2}", addSpace, ctrX, ctrY), this.Font, Brushes.Yellow, new Point(5, yofs));
                // beam power
                e.Graphics.DrawString(String.Format("power\t{0:0.#E+00}", _beamEllipsePower), this.Font, Brushes.Yellow, new Point(5, yofs + this.Font.Height + 3));
                // beam peak intensity
                Brush brush = Brushes.Yellow;
                if ( _beamPeakIntensity == 255 || _beamPeakIntensity == 0 ) {
                    brush = Brushes.Red;
                }
                e.Graphics.DrawString(String.Format("gray\t{0} ({1})", _beamPeakIntensity, _settings.BeamIntensityThreshold), this.Font, brush, new Point(5, yofs + 2 * (this.Font.Height + 3)));
                // beam diameter and ellipse axis
                int beamDiameter = (int)(_epCv.RotatedRect.Size.Width / 2 + _epCv.RotatedRect.Size.Height / 2);
                brush = beamDiameter < _settings.BeamMinimumDiameter ? Brushes.Red : Brushes.Yellow;
                e.Graphics.DrawString("diam\t" + beamDiameter.ToString() + " (" + _settings.BeamMinimumDiameter.ToString() + ")", this.Font, brush, new Point(5, yofs + 3 * (this.Font.Height + 3)));
                e.Graphics.DrawString("a\t" + ((int)_epCv.RotatedRect.Size.Height).ToString(), this.Font, Brushes.Yellow, new Point(5, yofs + 4 * (this.Font.Height + 3)));
                e.Graphics.DrawString("b\t" + ((int)_epCv.RotatedRect.Size.Width).ToString(), this.Font, Brushes.Yellow, new Point(5, yofs + 5 * (this.Font.Height + 3)));

                // update pseudo color scale 
                this.pictureBoxPseudo.Invalidate();

            } catch ( Exception ex ) {
            }
        }

        //
        // all image processing takes place here: draw some overlays onto _bmp
        //
        private void pictureBox_PaintWorker() 
        {
            int exStp = 0;
            try {

                // call comes from a non UI thread
                Invoke(new Action(() => {

                    using ( Graphics g = Graphics.FromImage(_bmp) ) {

                        // no image to show but show crosshair
                        if ( _bmp == null ) {
                            g.DrawLine(Pens.Red, new Point(0, _crosshairCenter.Y), new Point(this.pictureBox.ClientSize.Width, _crosshairCenter.Y));
                            g.DrawLine(Pens.Red, new Point(_crosshairCenter.X, 0), new Point(_crosshairCenter.X, this.pictureBox.ClientSize.Height));
                            return;
                        }

                        // no action w/o reasonable crosshair coordinates: happens 'on purpose' after context menu to make overlays disappear 
                        if ( _crosshairCenter.X < 0 || _crosshairCenter.Y < 0 ) {
                            return;
                        }

                        // axis stretching/compressing multiplier between pictureBox / _bmp / tableLayoutPanelGraphs_Paint: one multiplier, because aspect ratio is ALWAYS kept 
                        _multiplierBmp2Paint = (float)this.pictureBox.ClientSize.Width / (float)_bmpSize.Width;

                        // allows Bitmap transfer via socket to another app; _socketBmp is locked unless drawing is ready, so the sending thread always gets a decent image
                        Graphics sg = null;
                        _socketBmp = new Bitmap(1, 1);
                        lock ( _socketBmp ) {
                            if ( _settings.ProvideSocketScreenshots ) {
                                _socketBmp = new Bitmap(_bmp, this.pictureBox.ClientSize.Width, this.pictureBox.ClientSize.Height);
                                _socketBmp = new Bitmap(_bmp, this.pictureBox.ClientSize.Width, this.pictureBox.ClientSize.Height);
                                sg = Graphics.FromImage(_socketBmp);
                            }

                            int blobError = -100;
                            List<Point> beamPolygon = new List<Point>();
                            int crosshairDiameter = _crosshairDiameterLast;

                            // let the crosshair follow the hot spot, aka beam blob 
                            if ( _settings.FollowBeam ) {
                                try {
                                    //
                                    //  main image procesing
                                    //
                                    blobError = getBeamBlob(_bmpSize, _bmpArr, _settings.BeamIntensityThreshold, _settings.BeamMinimumDiameter,
                                                            out beamPolygon, out crosshairDiameter, out _epCv);
                                    //
                                    if ( blobError == -1 ) {
                                        this.Text = "Too much background noise";
                                        return;
                                    }
                                    // needed, if crosshair doesn't follow beam
                                    _crosshairDiameterLast = crosshairDiameter;
                                } catch ( Exception ex ) {
                                    this.Text = "Paint(0): " + ex.Message;
                                    return;
                                }
                                try {
                                    // crosshair center coordinates
                                    if ( (_epCv.RotatedRect.Center.X > 0) && (_epCv.RotatedRect.Center.Y > 0) ) {
                                        _crosshairCenter.X = (int)_epCv.RotatedRect.Center.X;
                                        _crosshairCenter.Y = (int)_epCv.RotatedRect.Center.Y;
                                    }
                                } catch ( Exception ex ) {
                                    this.Text = "Paint(1): " + ex.Message;
                                    return;
                                }
                            }

                            exStp = 1;

                            // crosshairRadius is used to draw an enclosing circle, respectively the cross sections along the ellipse's main axis
                            float crosshairRadius = crosshairDiameter / 2 + 50;
                            // crosshairRadius needs to be limited to the actual image dimensions (assuming height is smaller than width)
                            crosshairRadius = Math.Min(crosshairRadius, _bmpSize.Height / 2);
                            try {
                                if ( (crosshairRadius > 0) && (_epCv.RotatedRect.Center.X != 0) ) {

                                    //
                                    // crosshair follows both gravity axis of the calculated ellipse
                                    //
                                    float theta = _epCv.RotatedRect.Angle;
                                    // horizontal crosshair axis projection to canvas
                                    float horzCanvasX = crosshairRadius * (float)Math.Sin(Math.PI * theta / 180.0);  // x projection of red axis to canvas coordinates
                                    float horzCanvasY = crosshairRadius * (float)Math.Cos(Math.PI * theta / 180.0);  // y projection of red axis to canvas coordinates
                                    // horizontal intersection points of crosshair with crosshair circle
                                    PointF horzLeft = new PointF(_epCv.RotatedRect.Center.X + horzCanvasX, _epCv.RotatedRect.Center.Y + horzCanvasY);
                                    PointF horzRight = new PointF(_epCv.RotatedRect.Center.X - horzCanvasX, _epCv.RotatedRect.Center.Y - horzCanvasY);
                                    // vertical crosshair axis
                                    float vertCanvasX = crosshairRadius * (float)Math.Sin(Math.PI / 2 + Math.PI * theta / 180.0);
                                    float vertCanvasY = crosshairRadius * (float)Math.Cos(Math.PI / 2 + Math.PI * theta / 180.0);
                                    PointF vertTop = new PointF(_epCv.RotatedRect.Center.X + vertCanvasX, _epCv.RotatedRect.Center.Y + vertCanvasY);
                                    PointF vertBott = new PointF(_epCv.RotatedRect.Center.X - vertCanvasX, _epCv.RotatedRect.Center.Y - vertCanvasY);
                                    if ( _settings.Crosshair ) {
                                        // draw the two red crosshair axis
                                        g.DrawLine(_crosshairPen, horzLeft, horzRight);
                                        g.DrawLine(_crosshairPen, vertTop, vertBott);
                                        if ( _settings.ProvideSocketScreenshots ) {
                                            sg.DrawLine(Pens.Red, horzLeft, horzRight);
                                            sg.DrawLine(Pens.Red, vertTop, vertBott);
                                        }
                                    }

                                    //
                                    // beam intensity profiles: get point coordinates along the 2 cross sections thru the beam by using linear function y = mx + n
                                    //      profiles contain just _bmp pixel coordinates, which are later translated to intensity = f(x)
                                    //
                                    if ( _settings.FollowBeam ) {
                                        // clear beam intensity profiles
                                        _horProf = new Point[0];
                                        _verProf = new Point[0];
                                        if ( _settings.SwapProfiles ) {
                                            // an elliptic beam with perpendicular long axis may want to have switched profile sections
                                            float divider = horzRight.X - horzLeft.X;
                                            divider = Math.Abs(divider) < 0.001f ? Math.Max(1, Math.Sign(divider)) * 0.001f : divider;
                                            float m = (horzRight.Y - horzLeft.Y) / divider;
                                            float n = horzRight.Y - horzRight.X * m;
                                            int start = (int)Math.Min(horzLeft.Y, horzRight.Y);
                                            int stop = (int)Math.Max(horzLeft.Y, horzRight.Y);
                                            _verProf = new Point[stop - start];
                                            int ndx = 0;
                                            for ( int y = start; y < stop; y++ ) {
                                                _verProf[ndx++] = new Point((int)(1.0f / m * ((float)y - n)), y);
                                            }
                                            divider = vertBott.X - vertTop.X;
                                            divider = Math.Abs(divider) < 0.001f ? Math.Sign(divider) * 0.001f : divider;
                                            m = (vertBott.Y - vertTop.Y) / divider;
                                            n = vertTop.Y - vertTop.X * m;
                                            start = (int)Math.Min(vertTop.X, vertBott.X);
                                            stop = (int)Math.Max(vertTop.X, vertBott.X);
                                            _horProf = new Point[stop - start];
                                            ndx = 0;
                                            for ( int x = start; x < stop; x++ ) {
                                                _horProf[ndx++] = new Point(x, (int)(m * (float)x + n));
                                            }
                                        } else {
                                            // best for an elliptic beam with horizontal orientation
                                            float divider = horzRight.X - horzLeft.X;
                                            divider = Math.Abs(divider) < 0.001f ? Math.Max(1, Math.Sign(divider)) * 0.001f : divider;
                                            float m = (horzRight.Y - horzLeft.Y) / divider;
                                            float n = horzLeft.Y - horzLeft.X * m;
                                            int start = (int)Math.Min(horzLeft.X, horzRight.X);
                                            int stop = (int)Math.Max(horzLeft.X, horzRight.X);
                                            try {
                                                _horProf = new Point[stop - start];
                                            } catch ( OutOfMemoryException oome ) {
                                                this.Text = "Paint(6): " + oome.Message;
                                                return;
                                            }
                                            int ndx = 0;
                                            for ( int x = start; x < stop; x++ ) {
                                                _horProf[ndx++] = new Point(x, (int)(m * (float)x + n));
                                            }
                                            divider = vertBott.X - vertTop.X;
                                            divider = Math.Abs(divider) < 0.001f ? Math.Sign(divider) * 0.001f : divider;
                                            m = (vertBott.Y - vertTop.Y) / divider;
                                            n = vertTop.Y - vertTop.X * m;
                                            start = (int)Math.Min(vertTop.Y, vertBott.Y);
                                            stop = (int)Math.Max(vertTop.Y, vertBott.Y);
                                            _verProf = new Point[stop - start];
                                            ndx = 0;
                                            for ( int y = start; y < stop; y++ ) {
                                                _verProf[ndx++] = new Point((int)(1.0f / m * (float)(y - n)), y);
                                            }
                                        }
                                    }

                                    exStp = 2;

                                    //
                                    // extend the 4 red crosshair endpoints with surrounding circle
                                    //
                                    if ( _settings.Crosshair ) {
                                        if ( _settings.SwapProfiles ) {
                                            // ... to the left profile area ...
                                            g.DrawLine(Pens.White, horzLeft, new PointF(0, horzLeft.Y));
                                            g.DrawLine(Pens.White, horzRight, new PointF(0, horzRight.Y));
                                            // ... and to the bottom profile area
                                            g.DrawLine(Pens.White, vertTop, new PointF(vertTop.X, _bmp.Height));
                                            g.DrawLine(Pens.White, vertBott, new PointF(vertBott.X, _bmp.Height));
                                        } else {
                                            // ... to the bottom profile area ...
                                            g.DrawLine(Pens.White, horzLeft, new PointF(horzLeft.X, _bmp.Height));
                                            g.DrawLine(Pens.White, horzRight, new PointF(horzRight.X, _bmp.Height));
                                            // ... and to the left profile area
                                            g.DrawLine(Pens.White, vertTop, new PointF(0, vertTop.Y));
                                            g.DrawLine(Pens.White, vertBott, new PointF(0, vertBott.Y));
                                        }
                                    }
                                }
                            } catch ( Exception ex ) {
                                this.Text = "Paint(2): " + ex.Message;
                                return;
                            }

                            exStp = 3;

                            // show real beam border polygon --> according to selected beam threshold
                            Pen penPolygon = _settings.BeamIntensityThreshold < 100 ? Pens.White : Pens.Black;
                            if ( _settings.SearchTraces ) {
                                penPolygon = Pens.White;
                            }
                            try {
                                if ( beamPolygon != null && beamPolygon.Count > 0 ) {
                                    g.DrawLines(penPolygon, beamPolygon.ToArray());
                                    if ( _settings.ProvideSocketScreenshots ) {
                                        sg.DrawLines(penPolygon, beamPolygon.ToArray());
                                    }
                                }
                            } catch ( Exception ex ) {
                                this.Text = "Paint(4): " + ex.Message;
                            }

                            // show beam enclosing ellipse --> according to beam power calculation method (FWHM, D86)
                            if ( _epCv.RotatedRect.Center.X != 0 && _settings.Ellipse ) {
                                // ellipse color is inverted from the color at ellipse perimeter
                                _ellipsePen.Color = _ellipseColorInverted;
                                // draw EmguCV fitted ellipse
                                Rectangle rect = new Rectangle(
                                                     new Point((int)(_epCv.RotatedRect.Center.X - _epCv.RotatedRect.Size.Width / 2),
                                                               (int)(_epCv.RotatedRect.Center.Y - _epCv.RotatedRect.Size.Height / 2)),
                                                     new Size((int)_epCv.RotatedRect.Size.Width,
                                                              (int)_epCv.RotatedRect.Size.Height));
                                g.TranslateTransform((int)_epCv.RotatedRect.Center.X, (int)_epCv.RotatedRect.Center.Y);
                                g.RotateTransform(-1 * _epCv.RotatedRect.Angle);
                                g.TranslateTransform(-1 * (int)_epCv.RotatedRect.Center.X, -1 * (int)_epCv.RotatedRect.Center.Y);
                                g.DrawEllipse(_ellipsePen, rect);
                                g.ResetTransform();
                                if ( _settings.ProvideSocketScreenshots ) {
                                    sg.TranslateTransform((int)_epCv.RotatedRect.Center.X, (int)_epCv.RotatedRect.Center.Y);
                                    sg.RotateTransform(-1 * _epCv.RotatedRect.Angle);
                                    sg.TranslateTransform(-1 * (int)_epCv.RotatedRect.Center.X, -1 * (int)_epCv.RotatedRect.Center.Y);
                                    sg.DrawEllipse(Pens.Black, rect);
                                    sg.ResetTransform();
                                }
                            }

                            exStp = 4;

                            // show circle part of the crosshair around "beam enclosing ellipse"
                            if ( _settings.Crosshair ) {
                                if ( (crosshairRadius > 0) && (_epCv.RotatedRect.Center != new Point(0, 0)) ) {
                                    float x = _epCv.RotatedRect.Center.X - crosshairRadius;
                                    float y = _epCv.RotatedRect.Center.Y - crosshairRadius;
                                    float d = 2 * crosshairRadius;
                                    g.DrawEllipse(Pens.White, x, y, d, d);
                                    if ( _settings.ProvideSocketScreenshots ) {
                                        sg.DrawEllipse(Pens.White, x, y, d, d);
                                    }
                                }
                            }

                            exStp = 5;

                            // show peak intensity
                            if ( _settings.BeamPeak && _peakCenterSub.X > 0 ) {
                                g.DrawLine(Pens.Black, _peakCenterSub.X - 10, _peakCenterSub.Y, _peakCenterSub.X + 10, _peakCenterSub.Y);
                                g.DrawLine(Pens.Black, _peakCenterSub.X, _peakCenterSub.Y - 10, _peakCenterSub.X, _peakCenterSub.Y + 10);
                            }

                            exStp = 6;

                            // hide OR show a small white cross indicating the beam search origin
                            if ( _settings.BeamSearchOrigin != AppSettings.BeamSearchStates.HIDE ) {
                                // start beam search EITHER from an offset to the image center OR from the image center
                                Point imageCenterOffset = _settings.BeamSearchOrigin == AppSettings.BeamSearchStates.MANUAL ? _settings.BeamSearchOffset : new Point();
                                PointF imageCenter = new PointF(_bmpSize.Width / 2 + imageCenterOffset.X, _bmpSize.Height / 2 + imageCenterOffset.Y);
                                g.DrawLine(Pens.White, imageCenter.X - 10, imageCenter.Y, imageCenter.X + 10, imageCenter.Y);
                                g.DrawLine(Pens.White, imageCenter.X, imageCenter.Y - 10, imageCenter.X, imageCenter.Y + 10);
                                if ( _settings.ProvideSocketScreenshots ) {
                                    sg.DrawLine(Pens.White, imageCenter.X - 10, imageCenter.Y, imageCenter.X + 10, imageCenter.Y);
                                    sg.DrawLine(Pens.White, imageCenter.X, imageCenter.Y - 10, imageCenter.X, imageCenter.Y + 10);
                                }
                            }

                            exStp = 7;

                            // allows Bitmap transfer via socket to another listening app                
                            if ( _settings.ProvideSocketScreenshots ) {
                                sg.Dispose();
                            }
                        }

                        // repaint scales left & bottom
                        this.tableLayoutPanelGraphs.Invalidate(false);
                        this.tableLayoutPanelGraphs.Update();

                    } // end of 'using ( Graphics g = Graphics.FromImage(bmp) ) {'

                })); // end of invoke

            } catch ( Exception fe ) {
                _headlineAlert = String.Format("pictureBox_PaintWorker exStp: {0}", exStp);
            }
        }

        // supposed to reset an exception state, sometimes needed for pictureBox and 'red cross exception'
        // ! perhaps not needed anymore, if pictureBox is subclassed with try/catch OnPaint - but doesn't harm
        void ResetExceptionState(Control control)
        {
            typeof(Control).InvokeMember("SetState", System.Reflection.BindingFlags.NonPublic | 
                                                     System.Reflection.BindingFlags.InvokeMethod | 
                                                     System.Reflection.BindingFlags.Instance, 
                                                     null, 
                                                     control, 
                                                     new object[] { 0x400000, false });
        }

        // set crosshair position coordinates relative to pictureBox image == canvas
        private void pictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if ( e.Button == System.Windows.Forms.MouseButtons.Left ) {
                // manually move the beam search origin off of the image center
                if ( ModifierKeys == Keys.Shift && _settings.BeamSearchOrigin == AppSettings.BeamSearchStates.MANUAL ) {
                    _settings.BeamSearchOffset = new Point((int)((float)(e.X) / _multiplierBmp2Paint - _bmpSize.Width / 2), (int)((float)(e.Y) / _multiplierBmp2Paint - _bmpSize.Height / 2));
                }
            }

            // allows to keep pictureBox ContextMenu open (re show)
            if ( e.Button == System.Windows.Forms.MouseButtons.Right ) {
                _mouseButtonDownDown = e.Location;            
            }

            headLine();
        }
        private void pictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if ( e.Button == System.Windows.Forms.MouseButtons.Left ) {
                if ( ModifierKeys == Keys.Shift && _settings.BeamSearchOrigin == AppSettings.BeamSearchStates.MANUAL ) {
                    // manually move the beam search origin off of the image center
                    _settings.BeamSearchOffset = new Point((int)((float)(e.X) / _multiplierBmp2Paint - _bmpSize.Width / 2), (int)((float)(e.Y) / _multiplierBmp2Paint - _bmpSize.Height / 2));
                }
            }
            // show crosshair even in a still image
            if ( !_settings.FollowBeam && ((_bmp == null) || ((_videoDevice != null) && (!_videoDevice.IsRunning))) ) {
                this.pictureBox.Invalidate();
            }
        }

        // IMessageFilter: intercept messages
        const int WM_KEYDOWN = 0x100;
        public bool PreFilterMessage(ref Message m)     
        {
            // handle crosshair appearance with keys
            if ( m.Msg == WM_KEYDOWN ) {
                // <ESC> toggles red cross on/off
                if ( (Keys)m.WParam == Keys.Escape ) {
                    this.pictureBox_CmShowCrosshair.PerformClick();
                    return true;
                }
            }
            return false;
        }

        // there are two Ulam spiral modes:
        enum SpiralMode {
            SEARCH_HOT = 0,  // search a hot pixel from a given start coordinate
            BUILD_BLOB = 1,  // build a blob containing hat pixels of a beam
        }

        // 'pixel precise blob border walker':
        // https://chaosinmotion.blog/2014/08/21/finding-the-boundary-of-a-one-bit-per-pixel-monochrome-blob/
        enum WalkDirection
        {
            ILLEGAL,
            LEFT,
            RIGHT,
            UP,
            DOWN
        }
        bool isBlobPixel(Size bmpSize, byte[] bmpArr, int x, int y, int threshold)
        {
            bool retval = false;
            if ( y < bmpSize.Height && y > 0 && x < bmpSize.Width && x > 0 ) {
                int pos = y * bmpSize.Width + x;
                if ( bmpArr[pos] >= threshold ) {
                    retval = true;
                }
            }
            return retval;
        }
        int getPixelState(Size bmpSize, byte[] bmpArr, Point pt, int thresholdBlob)
        {
            int ret = 0;
            if ( isBlobPixel(bmpSize, bmpArr, pt.X - 1, pt.Y - 1, thresholdBlob) ) ret |= 1;
            if ( isBlobPixel(bmpSize, bmpArr, pt.X, pt.Y - 1, thresholdBlob) ) ret |= 2;
            if ( isBlobPixel(bmpSize, bmpArr, pt.X - 1, pt.Y, thresholdBlob) ) ret |= 4;
            if ( isBlobPixel(bmpSize, bmpArr, pt.X, pt.Y, thresholdBlob) ) ret |= 8;
            return ret;
        }
        List<Point> getFullBeamBorderPolygon(Size bmpSize, byte[] bmpArr, Point ptStart, int thresholdBlob)
        {
            List<Point> polygon = new List<Point>();
            WalkDirection wd = WalkDirection.ILLEGAL;
            Point pt = ptStart;

            do {
                // get state of a '4 pixel matrix'
                int state = getPixelState(bmpSize, bmpArr, pt, thresholdBlob);
                if ( (state == 0) || (state == 15) ) {
                    // all 4 pixels are set (15) OR none is set (0)
                    break;
                } else {
                    // add point to polygon list
                    polygon.Add(pt);
                }
                // the magic logic of moving the '4 pixel matrix' along a border
                switch ( state ) {
                    case 1: 
                        wd = WalkDirection.LEFT;
                        pt.X--;
                        break;
                    case 2: 
                        wd = WalkDirection.UP;
                        pt.Y--;
                        break;
                    case 3: 
                        wd = WalkDirection.LEFT;
                        pt.X--;
                        break;
                    case 4: 
                        wd = WalkDirection.DOWN;
                        pt.Y++;
                        break;
                    case 5: 
                        wd = WalkDirection.DOWN;
                        pt.Y++;
                        break;
                    case 6: 
                        if ( wd == WalkDirection.RIGHT ) {
                            wd = WalkDirection.DOWN;
                            pt.Y++;
                        }
                        if ( wd == WalkDirection.LEFT ) {
                            wd = WalkDirection.UP;
                            pt.Y--;
                        }
                        break;
                    case 7: 
                        wd = WalkDirection.DOWN;
                        pt.Y++;
                        break;
                    case 8: 
                        wd = WalkDirection.RIGHT;
                        pt.X++;
                        break;
                    case 9:
                        if ( wd == WalkDirection.DOWN ) {
                            wd = WalkDirection.LEFT;
                            pt.X--;
                        }
                        if ( wd == WalkDirection.UP ) {
                            wd = WalkDirection.RIGHT;
                            pt.X++;
                        }
                        break;
                    case 10: 
                        wd = WalkDirection.UP;
                        pt.Y--;
                        break;
                    case 11: 
                        wd = WalkDirection.LEFT;
                        pt.X--;
                        break;
                    case 12: 
                        wd = WalkDirection.RIGHT;
                        pt.X++;
                        break;
                    case 13: 
                        wd = WalkDirection.RIGHT;
                        pt.X++;
                        break;
                    case 14: 
                        wd = WalkDirection.UP;
                        pt.Y--;
                        break;
                }
                // break when current point matches to start point, but add last point prior leaving
                if ( pt == ptStart ) {
                    polygon.Add(pt);
                    break;
                }
            } while ( true );

            return polygon;
        }

        // helper class containing points and the comprising area of all collected points
        class Blob
        {
            public PowerDistribution pd { get; set; }
            public List<Point> SearchTrace { get; set; }
            public Blob()
            {
                pd = new PowerDistribution();
                SearchTrace = new List<Point>();
            }
        }

        //
        // collect blob pixels along an Ulam spiral:
        //         - initially start the Ulam spiral at the image center with _settings.BeamSearchOffset
        //         - once a hot pixel (thresholdBlob) is found, the spiraling starts again,
        //                now beginning with the just found hot pixel position as the new spiral center (avoids large spiral diameters)
        //         - now only 'neighbourhood/consecutive' hot pixels are taken into account
        //         - break if no hot pixel was found along a full spiral (aka beam blob is ready)
        //         - break after a certain amount of hot pixels was found (makes method faster)
        //         - or break if the length of the Ulam spiral became too long
        //
        Blob getPixelsAlongUlamSpiral(Size bmpSize, byte[] bmpArr, int thresholdBlob)
        {
            int ULAMGRIDSIZE = bmpSize.Height < 400 ? 1 : 3;
            int width = bmpSize.Width;

            // default beam search starts from image center, both other cases should be rather seldom
            Point imageCenterOffset = new Point();
            if ( _settings.BeamSearchOrigin != AppSettings.BeamSearchStates.CENTER ) {
                switch ( _settings.BeamSearchOrigin ) {
                    // user manually forces to start the beam search from an off center position
                    case AppSettings.BeamSearchStates.MANUAL: imageCenterOffset = _settings.BeamSearchOffset; break;
                    // hide the white cross indicating the beam search origion BUT keep the search offset belonging to the current active status CENTER or MANUAL
                    case AppSettings.BeamSearchStates.HIDE: imageCenterOffset = _beamSearchOriginCurrent; break;
                }
            }
            Point ulamPt = new Point(bmpSize.Width / 2 + imageCenterOffset.X, bmpSize.Height / 2 + imageCenterOffset.Y);

            // focus on the 1st beam blob near to the center of the image
            Blob blob = new Blob();

            // set a limit (length of a ulam edge) depending on the image size
            int lenEdgeLimit = bmpSize.Width / 2 + Math.Abs(imageCenterOffset.X);

            //
            // a 1st step just searches for hot pixels (> thresholdBlob), which triggers the re start of the ulam spiraling
            // ulam spiral mode control:
            //      initially search a hot pixel --> do 'outer blob spiral'
            //      if hot pixel was found       --> do 'inner blob spiral' 
            // 
            SpiralMode spiralMode = SpiralMode.SEARCH_HOT;
            int hitCounter = 0;
            int hitCounterGoal = (int)(Math.Pow(_settings.BeamMinimumDiameter / ULAMGRIDSIZE, 2) * 0.1f);

            // 'blob spiral' specific
            int  blobSpiralPixelCount = 0;   // pixel count in current spiral
            int  blobSpiralFailCount = 0;    // 'blob spiral' collected number of 'non hot pixels'
            bool blobOneSpiralDone = false;  // flag after a full ulam spiral is executed

            // common ulam spiral control vars
            bool dueX = true;                // affect X or Y
            bool down = false;               // if Y is affected, then down or up
            bool left = false;               // if X is affected, then left or right
            int  lenEdge = 1;                // Ulam sequence: 1,1, 2,2, 3,3, 4,4, 5,5, 6,6, ...     --> one sequence per X and one sequence per Y
            int  lenEdgeNdx = 0;             // Ulam sequence index

            // add noise
            thresholdBlob = Math.Min(thresholdBlob + 10, 255);

            // ulam spiral loop
            do {

                // ensure that a pixel is within the bmp borders
                ulamPt.X = Math.Max(Math.Min(ulamPt.X, bmpSize.Width - 1), 0);
                ulamPt.Y = Math.Max(Math.Min(ulamPt.Y, bmpSize.Height - 1), 0);
                
                // trace shows, how the initial search was executed
                if ( _settings.SearchTraces || _beamSearchError ) {
                    blob.SearchTrace.Add(new Point(ulamPt.X, ulamPt.Y));
                }
                
                // calculate current pixel position in the image byte array
                int pos = ulamPt.Y * width + ulamPt.X;

                // check current pixel for a 'hot pixel' threshold hit
                byte pixelIntensity = bmpArr[pos];
                if ( pixelIntensity >= thresholdBlob ) {

                    // do not rely on a random single hot pixel
                    hitCounter++;

                    // outer spiral
                    if ( spiralMode == SpiralMode.SEARCH_HOT ) {
                        if ( hitCounter > hitCounterGoal ) {
                            // once a couple of safe hot pixels are detected, the whole spiral re starts assuming, the found hot pixel belongs to a blob   
                            spiralMode = SpiralMode.BUILD_BLOB;
                            blob.pd = new PowerDistribution();
                            dueX = true;
                            down = false;
                            left = false;
                            lenEdge = 1;
                            lenEdgeNdx = 0;
                            hitCounter = 0;
                        }
                    }
                    // inner spiral
                    if ( spiralMode == SpiralMode.BUILD_BLOB ) {
                        blob.pd.array[pixelIntensity].Add(ulamPt);
                        // let's limit the blob build: if the blob contains ca. nn hot pixel positions at the same intensity, it is 'a good blob'
                        if ( hitCounter > 500 ) {
                            break;
                        }
                    }

                } else {
                    // the pixel is not a hot pixel at all
                    if ( spiralMode == SpiralMode.BUILD_BLOB ) {
                        blobSpiralFailCount++;
                    }
                }

                // common ulam spiral control: move pixel coordinates
                if ( dueX ) {                        // move X
                    if ( left ) {                    // move left
                        ulamPt.X -= ULAMGRIDSIZE;
                    } else {
                        ulamPt.X += ULAMGRIDSIZE;    // move right
                    }
                    lenEdgeNdx++;                    // increment sequence counter  
                    if ( lenEdgeNdx >= lenEdge ) {   // sequence limit reached 
                        dueX = false;                // switch to Y direction 
                        left = !left;                // reverse X direction   
                        lenEdgeNdx = 0;              // reset sequence counter 
                    }
                } else {                             // move Y
                    if ( down ) {                    // move down 
                        ulamPt.Y += ULAMGRIDSIZE;
                    } else {
                        ulamPt.Y -= ULAMGRIDSIZE;   // move up
                        // 'blob mode' specific: conditions after a full spiral is drawn
                        //if ( (spiralMode == SpiralMode.BUILD_BLOB) && (lenEdgeNdx == 0) && (blob.SearchPoints.Count > 2) ) {
                        //    blobOneSpiralDone = true;
                        //}
                    }
                    lenEdgeNdx++;
                    if ( lenEdgeNdx >= lenEdge ) {
                        lenEdge++;                   // increment Ulam sequence length after one X move and the subseqent Y move
                        dueX = true;
                        down = !down;
                        lenEdgeNdx = 0;
                    }
                }

                // 'blob mode' specific: break if a full spiral is drawn and no consecutive hot pixel was found <-- usually means, the blob is ready 
                if ( (spiralMode == SpiralMode.BUILD_BLOB) && blobOneSpiralDone && (blobSpiralFailCount >= blobSpiralPixelCount) ) {
                    break;
                }

                // 'blob mode' specific: do the next spiral, reset spiral pixel counter + fail counter + full spiral flag
                if ( blobOneSpiralDone ) {
                    blobSpiralPixelCount = 0;
                    blobSpiralFailCount = 0;
                    blobOneSpiralDone= false;
                }

                // 'blob mode' specific: each move is a pixel along the spiral
                if ( spiralMode == SpiralMode.BUILD_BLOB ) {
                    blobSpiralPixelCount++;
                }

            // finally give up, if the Ulam length reaches ca. 1/2 of the image width
            } while ( lenEdge < lenEdgeLimit );

            // no beam with requested min diameter was found
            if ( spiralMode == SpiralMode.SEARCH_HOT ) {
                _beamSearchError = true;
                _headlineAlert = String.Format("--- No beam with requested minimum diameter of {0} pixels found ---", _settings.BeamMinimumDiameter);
            } else {
                _beamSearchError = false;
            }

            // 
            return blob;
        }

        //
        // get beam power
        //
        // helper class: array of 256 possible power levels holds matching pixel coordinates in a list per level
        class PowerDistribution {
            public List<Point>[] array = new List<Point>[256];
            public PowerDistribution() {
                for ( int i = 0; i < 256; i++ ) {
                    array[i] = new List<Point>();
                }
            }
        }
        double getBeamPower(
            Size bmpSize,                              // current Bitmap size   
            byte[] bmpArr,                             // Bitmap raw data array
            int thresholdBeam,                         // all above threshold is a beam
            Point centerBeam,                          // beam center coordinates
            out PowerDistribution pd,                  // pixel power distribution
            out int powerCountFull)                    // count of pixels matching >=min power threshold   
        {

            powerCountFull = 0;
            double sumPower = 0;                       // sum of all pixel power values
            Point ulamPt = centerBeam;                 // beam center coordinates  
            pd = new PowerDistribution();

            // Ulam spiral controls
            int width = bmpSize.Width;
            int lenEdgeLimit = bmpSize.Width;          // set a Ulam limit
            int spiralStep = 0;                        // step counter for one spiral
            int blobSpiralFailCount = 0;               // number of power mismatches along one spiral
            int ULAMGRIDSIZE = 1;                      // pixel precise with no jumps
            bool fstSpiral = true;                     // Ulam starting is tricky
            bool fullSpiral = false;                   // one full spiral is drawn
            bool dueX = true;                          // affect X or Y
            bool down = false;                         // if Y is affected, then down or up
            bool left = false;                         // if X is affected, then left or right
            int lenEdge = 1;                           // Ulam sequence: 1,1, 2,2, 3,3, 4,4, 5,5, 6,6, ... 
            int lenEdgeNdx = 0;                        // Ulam sequence index

            // Ulam loop
            do {
                // calculate current pixel position in the byte array
                int pos = ulamPt.Y * width + ulamPt.X;
                // check current pixel for a brightness hit
                byte currentPixelIntensity = 0;
                if ( (pos > 0) && (pos < bmpArr.Length) ) {
                    currentPixelIntensity = bmpArr[pos];
                }
                // intensity hit ?
                if ( currentPixelIntensity >= thresholdBeam ) {
                    // add Point at the matching intensity level
                    pd.array[currentPixelIntensity].Add(ulamPt);
                    blobSpiralFailCount--;
                    sumPower += currentPixelIntensity;
                    powerCountFull++;
                } else {
                    // curent pixel power too low
                    blobSpiralFailCount++;
                }

                //
                // Ulam control: move pixel coordinate
                //
                if ( dueX ) {                        // move X
                    if ( left ) {                    // move left
                        ulamPt.X -= ULAMGRIDSIZE;
                    } else {
                        ulamPt.X += ULAMGRIDSIZE;    // move right
                    }
                    lenEdgeNdx++;                    // increment sequence counter  
                    if ( lenEdgeNdx >= lenEdge ) {   // sequence limit reached 
                        dueX = false;                // switch to Y direction 
                        left = !left;                // reverse X direction   
                        lenEdgeNdx = 0;              // reset sequence counter 
                    }
                } else {                             // move Y
                    if ( down ) {                    // move down 
                        ulamPt.Y += ULAMGRIDSIZE;
                    } else {
                        ulamPt.Y -= ULAMGRIDSIZE;    // move up
                        if ( lenEdge == 3 ) {
                            fstSpiral = false;
                        }
                        // conditions after a full spiral is drawn
                        if ( !fstSpiral && (lenEdgeNdx == 0) ) {
                            fullSpiral = true;
                        }
                    }
                    lenEdgeNdx++;
                    if ( lenEdgeNdx >= lenEdge ) {
                        lenEdge++;                   // increment Ulam sequence length after one X move and the subseqent Y move
                        dueX = true;
                        down = !down;
                        lenEdgeNdx = 0;
                    }
                }
                // break if a full spiral is drawn and no hot pixel was found 
                if ( fullSpiral && (blobSpiralFailCount >= spiralStep) ) {
                    break;
                }
                // next spiral: reset spiral step counter + fail counter + full spiral flag
                if ( fullSpiral ) {
                    blobSpiralFailCount = 0;
                    fullSpiral = false;
                    spiralStep = 0;
                }
                // each move is a pixel along the spiral
                spiralStep++;

            // finally give up, if the Ulam length reaches ca. 1/2 of the image width 
            } while ( lenEdge < lenEdgeLimit );

            // sanity check
            if ( lenEdge >= lenEdgeLimit ) {
                pd = new PowerDistribution();
            }

            return sumPower;
        }

        // find a convex hull to a list of points
        // https://stackoverflow.com/questions/14671206/how-to-compute-convex-hull-in-c-sharp
        class ConvexHull {
            private static double cross(Point O, Point A, Point B) {
                return (A.X - O.X) * (B.Y - O.Y) - (A.Y - O.Y) * (B.X - O.X);
            }
            public static List<Point> GetConvexHull(List<Point> points) {
                if ( points == null )
                    return null;
                if ( points.Count() <= 1 )
                    return points;
                int n = points.Count(), k = 0;
                List<Point> H = new List<Point>(new Point[2 * n]);
                points.Sort((a, b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
                // Build lower hull
                for ( int i = 0; i < n; ++i ) {
                    while ( k >= 2 && cross(H[k - 2], H[k - 1], points[i]) <= 0 )
                        k--;
                    H[k++] = points[i];
                }
                // Build upper hull
                for ( int i = n - 2, t = k + 1; i >= 0; i-- ) {
                    while ( k >= t && cross(H[k - 2], H[k - 1], points[i]) <= 0 )
                        k--;
                    H[k++] = points[i];
                }
                return H.Take(k).ToList();
            }
        }

        // median
        static int getMedian(int[] sourceNumbers) {
            // sanity
            if ( sourceNumbers == null || sourceNumbers.Length == 0 ) {
                return -1;
            }
            // make sure the list is sorted, but use a new array
            int[] sortedNumbers = (int[])sourceNumbers.Clone();
            Array.Sort(sortedNumbers);
            // get the median
            int size = sortedNumbers.Length;
            int mid = size / 2;
            int median = (size % 2 != 0) ? (int)sortedNumbers[mid] : ((int)sortedNumbers[mid] + (int)sortedNumbers[mid - 1]) / 2;
            return median;
        }

        //
        // detect a blob inside an image
        //
        // params:  bitmap size, byte[] - all bitmap pixels as 1 byte per pixel, beam intensity threshold, min. beam diameter
        // out:     List<Point> polygon - blob border/blob  search/blob power/sub beam hull, int crosshairDiameter, Emgu.CV.Structure.Ellipse - ellipse fitted to polygon
        // return:  error code
        int getBeamBlob(Size bmpSize, byte[] bmpArr, int beamIntensityThreshold, int minBeamDiameter,
                        out List<Point> polygon, out int crosshairDiameter, out Emgu.CV.Structure.Ellipse epCv) 
        {
            polygon = null;
            crosshairDiameter = 0;
            epCv = new Emgu.CV.Structure.Ellipse();
            int errorCode = 0;
            int width = bmpSize.Width;

            _headlineAlert = "";

#if DEBUG
            _sw.Restart();
#endif

            //
            // search for a reasonable blob
            //
            // pixels having the same or higher intensity as beamIntensityThreshold
            // begins from a given position in the image following an Ulam spiral
            Blob blob = getPixelsAlongUlamSpiral(bmpSize, bmpArr, beamIntensityThreshold);

#if DEBUG
            double blobTime = _sw.Elapsed.TotalMilliseconds;
            _sw.Restart();
#endif

            //
            // Blob Border Walker
            //
            // - supposed to find a polygon rendering the blob border
            // - kown issue: very rough borders occasionally provide a closed polygon NOT describing the real blob
            //               --> for the affected image, the border polygon/ellipse is MUCH SMALLER than expected 
            //               --> therefore we allow up to 20 repetitions if minBlobBorderPixelCount is not met, then we give up 
            //               --> minBlobBorderPixelCount provides a threshold for a minimal required pixel count inside a blob   
            // get index with largest number of pixel intensities: supposed to be the most trustworthy collection
            int maxPointCountAtIntensity = 0;
            int maxPointCount = 0;
            for( int i=0; i< blob.pd.array.Length; i++ ) {
                if ( blob.pd.array[i].Count > maxPointCount ) {
                    maxPointCount = blob.pd.array[i].Count;
                    maxPointCountAtIntensity = i;
                }
            }
            // get start coordinate: median shall eliminate random hot pixel positions
            int ndx = 0;
            int[] xArr = new int[blob.pd.array[maxPointCountAtIntensity].Count];
            int[] yArr = new int[blob.pd.array[maxPointCountAtIntensity].Count];
            foreach ( Point pt in blob.pd.array[maxPointCountAtIntensity] ) {
                xArr[ndx] = pt.X;
                yArr[ndx] = pt.Y;
                ndx++;
            }
            Point ulamPt = new Point(getMedian(xArr), getMedian(yArr));
            // border walker loop
            int tryCount = 0;
            polygon = new List<Point>();
            List<List<Point>> polygonList = new List<List<Point>>();
            int state;
            do {
                // 1st step:
                //     start with a pixel of known state == 15 (aka pixel belongs to the blob --> ulamPt)
                //     find a blob pixel with 'state != 15' (aka not all 4 pixels are set --> we hit the blob border)
                state = 0;
                do {
                    state = getPixelState(bmpSize, bmpArr, ulamPt, beamIntensityThreshold);
                    if ( state == 15 ) {
                        // simply walk straight up
                        ulamPt.Y--;
                    }
                } while ( state == 15 );
                // 2nd step:
                //     execute the 'blob border walker' algorithm
                polygon = getFullBeamBorderPolygon(bmpSize, bmpArr, ulamPt, beamIntensityThreshold);
                // something is wrong, if the real enclosing polygon has less points than the calculated circumference 'Math.PI * minBlobDiameter'
                //     cause: the state != 15 might come from an isolated pixel
                //     fix:   variation of start point 
                if ( polygon.Count < Math.PI * minBeamDiameter ) {
                    polygonList.Add(polygon);
                    // we try it again for a total of 20 times 
                    tryCount++;
                    if ( tryCount < 10 ) {
                        // slightliy outside the center +
                        if ( ulamPt.X + tryCount < bmpSize.Width && ulamPt.Y + tryCount < bmpSize.Height ) {
                            ulamPt.Offset(tryCount, tryCount);
                        } else {
                            break;
                        }
                    } else {
                        // slightliy outside the center -
                        if ( ulamPt.X - tryCount > 0 && ulamPt.Y - tryCount > 0 ) {
                            ulamPt.Offset(-1 * tryCount, -1 * tryCount);
                        } else {
                            break;
                        }
                    }
                } else {
                    break;
                }
            } while ( tryCount < 20 );
            if ( tryCount >= 20 ) {
                _headlineAlert = String.Format("--- Critical setting: meam minimum diameter of {0} pixels ---", _settings.BeamMinimumDiameter);
            }
            if ( polygon.Count == 0 ) {
                polygon = polygonList.OrderByDescending(x => x.Count).First().ToList();
            }
            if ( !_beamSearchError ) {
                if ( polygon.Count == 0 ) {
                    _headlineAlert = String.Format("--- No beam with minimum diameter of {0} pixels found ---", _settings.BeamMinimumDiameter);
                }
            }
            errorCode += tryCount;

#if DEBUG
            double borderTime = _sw.Elapsed.TotalMilliseconds;
            _sw.Start();
#endif

            //
            // get blob's preliminary center: centroid is the surrounding polygon's gravity center
            //
            Point centroid = getCentroid(polygon);

#if DEBUG
            double centroidTime = _sw.Elapsed.TotalMilliseconds;
            _sw.Restart();
#endif

            //
            // get full beam's enclosing rectangle
            //
            Rectangle fullBeamRect = new Rectangle();
            if ( polygon.Count > 0 ) {
                var minX = polygon.Min(p => p.X);
                var minY = polygon.Min(p => p.Y);
                var maxX = polygon.Max(p => p.X);
                var maxY = polygon.Max(p => p.Y);
                fullBeamRect = new Rectangle(minX, minY, maxX - minX, maxY - minY);
            }
            if ( fullBeamRect.Width > (int)((float)_bmpSize.Width * 0.8f) || fullBeamRect.Height > (int)((float)_bmpSize.Height * 0.8f) ) {
                _headlineAlert = String.Format("--- Camera exposure time to long? Beam to large? ---");
            }

#if DEBUG
            double enclosingTime = _sw.Elapsed.TotalMilliseconds;
            _sw.Restart();
#endif

            //polygon = ConvexHull.GetConvexHull(polygon);

            //
            // beam power
            //
            // get full power along a Ulam spiral; use beamIntensityThreshold as minimal pixel power limit
            PowerDistribution pdFull;
            int powerCountFull;
            double powerFull = getBeamPower(_bmpSize, _bmpArr, beamIntensityThreshold, centroid,
                                            out pdFull, out powerCountFull);

#if DEBUG
            double powerTime = _sw.Elapsed.TotalMilliseconds;
            _sw.Restart();
#endif

            //
            // sub beam is a part of the full beam
            //
            List<Point> subBeam = new List<Point>();                                    // list of subBeam coordinates
            int highestPoulatedIntensityLevel = -1;                                     // beam's largest populated intensity level
            List<Point> peakIntensityPoints = new List<Point>();                        // list of points containing the beam's peak intensity coordinates 
            int powerCumulative = 0;                                                    // cumulative power in subBeam
            double powerFraction =                                                      // D86 vs. FWHM vs. D4SIGMA    
                _settings.BeamDiameterType == AppSettings.BeamDiameter.FWHM ? 0.5f :    // FWHM     50%
                _settings.BeamDiameterType == AppSettings.BeamDiameter.D86  ? 0.865f :  // D86      86.5%
                1.0f;                                                                   // D3SIGMA 100% (ok, that's sort of cheating)  
            double powerSub = powerFull * powerFraction;                                // beam power goal according to beam width calculation method
            int subPowerLevelLowest = 0;                                                // lowest used intensity level  
            int subPowerLevelLatestLoopNdx = 0;                                         // point/pixel index inside of the lowest used intensity level

            // loop pdFull intensity level array backwards to catch the largest intensity levels first
            for ( int intensityLevel = pdFull.array.Length - 1; intensityLevel >= 0; intensityLevel-- ) {
                // iterator for pixels at given intensity level
                int i = 0;
                // loop pdFull list of points belonging to one intensity level
                foreach ( Point p in pdFull.array[intensityLevel] ) {
                    // get highest intensity level
                    if ( highestPoulatedIntensityLevel == -1 && pdFull.array[intensityLevel].Count > 0 ) {
                        highestPoulatedIntensityLevel = intensityLevel;
                        _beamPeakIntensity = (byte)intensityLevel;
                    }
                    if ( highestPoulatedIntensityLevel != -1 ) { 
                        // sum up intensities to a cumulative power
                        powerCumulative += intensityLevel;
                        // collect beam's peak intensity points to be able to later calculate an averaged peak intensity position
                        if ( highestPoulatedIntensityLevel == intensityLevel ) {
                            peakIntensityPoints.Add(p);
                        }
                    }
                    // break if powerCumulative exceeds limit goal
                    if ( powerCumulative >= powerSub ) {
                        subPowerLevelLowest = intensityLevel;
                        subPowerLevelLatestLoopNdx = i;
                        break;
                    }
                    // next pixel index at given intensity level
                    i++;
                }
                // break if power limit according to FWHM or D86 is met
                if ( powerCumulative >= powerSub ) {
                    break;
                }
            }

            // the beam's intensity peak point is the average of peak intensity locations
            _peakCenterSub = new Point();
            foreach ( Point p in peakIntensityPoints ) {
                _peakCenterSub.X += p.X;
                _peakCenterSub.Y += p.Y;
            }
            _peakCenterSub.X = (int)((double)_peakCenterSub.X / (double)peakIntensityPoints.Count);
            _peakCenterSub.Y = (int)((double)_peakCenterSub.Y / (double)peakIntensityPoints.Count);

            // build the subBeam hull
            // use the lowest subPowerD86Level + its loop index (could be incomplete) and the full predecessor to the lowest power level (is complete, but intensity wise too high)
            //     subBeam is almost a hull around the calculated beam power goal
            //     subBeam contains a heavily reduced number of point compared to all coordinates belonging to the sub beam
            // subBeam perimeter #1: might be incomplete due to abortion at subPowerD86LevelLatestLoopNdx
            for ( int i = 0; i < subPowerLevelLatestLoopNdx; i++ ) {
                subBeam.Add(pdFull.array[subPowerLevelLowest][i]);
            }
            // subBeam perimeter #2: use full predecessor to the lowest power level
            int powerLevelMinusOne = Math.Max(0, subPowerLevelLowest - 1);
            foreach ( Point pt in pdFull.array[powerLevelMinusOne] ) {
                subBeam.Add(pt);
            }
            // gray value at ellipse perimeter, supposed to be used to determine the ellipse's drawing color 
            _beamEllipseBorderIntensity = (byte)powerLevelMinusOne;
            // sub beam power value
            _beamEllipsePower = (ulong)powerSub;

            // sanity check: if a beam comprises more than 10 % of the total bitmap area, it's likely that something is worong
            if ( powerCountFull > _bmpArr.Length / 3 ) {
                _headlineAlert = String.Format("--- Camera exposure time to long? ---");
            }

#if DEBUG
            double subTime = _sw.Elapsed.TotalMilliseconds;
            _sw.Restart();
#endif

            //
            // get a convex hull polygon from subBeam pixel coordinates
            // 
            List<Point> beamPointsHull = ConvexHull.GetConvexHull(subBeam);

#if DEBUG
            double hullTime = _sw.Elapsed.TotalMilliseconds;
            _sw.Restart();
#endif

            //
            // EmguCV: fit ellipse to the subBeam hull coordinates
            //
            try {
                _epCv = PointCollection.EllipseLeastSquareFitting(beamPointsHull.Select(p => new PointF(p.X, p.Y)).ToArray());
//                _epCv = PointCollection.EllipseLeastSquareFitting(subBeam.Select(p => new PointF(p.X, p.Y)).ToArray());
            } catch ( Emgu.CV.Util.CvException cve ) {
                _headlineAlert = String.Format("--- Bad ellipse data basis ---");
            }
            if ( !_beamSearchError ) {
                if ( _epCv.RotatedRect.Center.X < 50 || _epCv.RotatedRect.Center.Y < 50 || _epCv.RotatedRect.Size.Width > _bmpSize.Width - 50 || _epCv.RotatedRect.Size.Height > _bmpSize.Height - 50 ) {
                    _headlineAlert = String.Format("--- Bad image ---");
                }
            }

#if DEBUG
            double ellipseTime = _sw.Elapsed.TotalMilliseconds;
            _sw.Stop();
            double sumTime = blobTime + borderTime + centroidTime + enclosingTime + powerTime + subTime + hullTime + ellipseTime;
#endif

            //
            // beam render options
            //
            // crosshair circle diameter needs offset between full beam center and ellipse center
            int hypFullBeam = (int)Math.Sqrt(fullBeamRect.Width * fullBeamRect.Width + fullBeamRect.Height * fullBeamRect.Height);
            int hypOffset = GrzTools.PointTools.GetDistance(_epCv.RotatedRect.Center, 
                                                            new Point(fullBeamRect.X + fullBeamRect.Width / 2, fullBeamRect.Y + fullBeamRect.Height / 2));
            crosshairDiameter = hypFullBeam + hypOffset;
            // show beam border
            polygon = _settings.BeamBorder ? polygon : null;
            // beam search ULAM spirals
            if ( _settings.SearchTraces || _beamSearchError ) {
                polygon = blob.SearchTrace;
            }
            // subBeam ellipse power coordinates
            if ( _settings.EllipsePower ) {
                polygon = subBeam;
            }
            // subBeam points hull as a base for the ellipse
            if ( _settings.EllipsePointsHull ) {
                polygon = beamPointsHull;
            }

            return errorCode;
        }

        // return center of gravity of a polygon
        public static Point getCentroid(List<Point> poly)
        {
            float accumulatedArea = 0.0f;
            float centerX = 0.0f;
            float centerY = 0.0f;

            for ( int i = 0, j = poly.Count - 1; i < poly.Count; j = i++ ) {
                float temp = poly[i].X * poly[j].Y - poly[j].X * poly[i].Y;
                accumulatedArea += temp;
                centerX += (poly[i].X + poly[j].X) * temp;
                centerY += (poly[i].Y + poly[j].Y) * temp;
            }

            if ( Math.Abs(accumulatedArea) < 1E-7f ) {
                return Point.Empty;  // Avoid division by zero
            }

            accumulatedArea *= 3f;
            return new Point((int)(centerX / accumulatedArea), (int)(centerY / accumulatedArea));
        }

        // tricky: match MainForm size to aspect ratio of pictureBox ( default MainForm 656x578 translates to pictureBox 640x480  -->  voodoo static offsets 16,98 )
        void adjustMainFormSize(Size size)
        {
            if ( _bmp == null ) {
                return;
            }

            // 'aspect ratio set value' is given by the image: ??? exception thrown in case _bmp.Width or _bmp.Height is touched ???
            //double aspectRatioSet = (double)_bmpSize.Width / (double)_bmpSize.Height;
            double aspectRatioSet = (double)size.Width / (double)size.Height;

            // 'aspect ratio current value' is given by the current pictureBox dimensions
            double aspectRatioCur = (double)this.pictureBox.ClientSize.Width / (double)this.pictureBox.ClientSize.Height;

            // is there work to do?
            if ( Math.Round(aspectRatioCur, 3) != Math.Round(aspectRatioSet, 3) ) {

                // the screen bounds shall limit the dimensions of the app
                Rectangle screenBounds = new Rectangle();
                Invoke(new Action(() => { screenBounds = Screen.FromControl(this).Bounds; })); // "call from other thread Exception" w/o Invoke 

                // pictureBox width translated to a new MainWindow width is taking into account: "voodoo static offset" = 16 AND graphs area = 104 
                double wndWidth = this.pictureBox.ClientSize.Width + 16 + 104;
                wndWidth = Math.Min(screenBounds.Width, wndWidth);

                // adjust MainWindow height using the 'aspect ratio set value' AND add the static offset between MainWindow and pictureBox
                double wndHeight = ((wndWidth - 16 - 104) / aspectRatioSet) + 98 + 104;

                // take care about the screen height limit - may happen when wndWidth became too large to find a matching wndHeight
                if ( screenBounds.Height < wndHeight ) {
                    wndHeight = Math.Min(screenBounds.Height, wndHeight);
                    wndWidth = ((wndHeight - 98 - 104) * aspectRatioSet) + 16 + 104;
                }

                // 1.0.0.2: canvas height became too short
                if ( wndHeight < this.MinimumSize.Height ) {
                    wndHeight = this.MinimumSize.Height;
                    wndWidth = ((wndHeight - 98 - 104) * aspectRatioSet) + 16 + 104;
                }

                // set corrected size of MainWindow
                Invoke(new Action(() => { 
                    this.Size = new Size((int)Math.Round(wndWidth), (int)Math.Round(wndHeight));
                    this.Invalidate(true);
                    headLine();
                }));

            }

            // take care about the crosshair
            if ( (_crosshairCenter == new Point(-1, -1)) || (_crosshairCenter.X >= this.pictureBox.ClientSize.Width) || (_crosshairCenter.Y >= this.pictureBox.ClientSize.Height) ) {
                _crosshairCenter = new Point(this.pictureBox.ClientSize.Width / 2, this.pictureBox.ClientSize.Height / 2);
            }
        }

        // update title bar info
        void headLine()
        {
            try {
                if ( _headlineAlert.Length == 0 ) {
                    snapshotButton.Enabled = (_bmp != null);
                    string txt = _settings.FollowBeam ? "crosshair follows beam" : "FIX cross";
                    txt += " - " + _settings.BeamDiameterType.ToString();
                    string file = this.timerStillImage.Enabled ? " - " + System.IO.Path.GetFileName((string)this.timerStillImage.Tag) : "";
                    string subtraction = _settings.SubtractBackgroundImage ? " - BACKGROUND subtraction" : "";
                    this.Text = String.Format("Beam Profile  -  screen: {0}x{1}  -  {2:0.0}fps - {3} {4} {5}", this.pictureBox.Width, this.pictureBox.Height, _fps, txt, file, subtraction);
                } else {
                    this.Text = _headlineAlert;
                }
            } catch {
                this.Text = "headLine() Exception";
            }
        }

        // a timer updates title bar info
        private void timerUpdateHeadline_Tick(object sender, EventArgs e)
        {
            headLine();
        }

        // MainForm resize event
        private void MainForm_Resize( object sender, EventArgs e )
        {
            if ( WindowState != FormWindowState.Minimized ) {
                adjustMainFormSize(_bmpSize);
                headLine();
            }
        }

        // show "about" in system menu
        const int WM_DEVICECHANGE = 0x0219;
        const int WM_SYSCOMMAND   = 0x112;
        [DllImport("user32.dll")]
        private static extern int GetSystemMenu( int hwnd, int bRevert );
        [DllImport("user32.dll")]
        private static extern int AppendMenu( int hMenu, int Flagsw, int IDNewItem, string lpNewItem );
        private void SetupSystemMenu()
        {
            // get handle to app system menu
            int menu = GetSystemMenu(this.Handle.ToInt32(), 0);
            // add a separator
            AppendMenu(menu, 0xA00, 0, null);
            // add items with unique message ID
            AppendMenu(menu, 0, 1236, "Still Image");
            AppendMenu(menu, 0, 1235, "Loupe");
            AppendMenu(menu, 0, 1234, "About Beam Profile Analyzer");
        }
        protected override void WndProc( ref System.Windows.Forms.Message m )
        {
            // something happened to USB, not clear whether camera or something else
            if ( m.Msg == WM_DEVICECHANGE ) {
                getCameraBasics();
            } 

            // WM_SYSCOMMAND is 0x112
            if ( m.Msg == WM_SYSCOMMAND ) {
                // open a still image and operate it the same way, as if it were a camera image
                if ( m.WParam.ToInt32() == 1236 ) {
                    if ( _videoDevice != null && _videoDevice.IsRunning ) {
                        return;
                    }
                    OpenFileDialog of = new OpenFileDialog();
                    of.InitialDirectory = System.Windows.Forms.Application.StartupPath;
                    of.Filter = "All Files|*.*|JPeg Image|*.jpg";
                    DialogResult result = of.ShowDialog();
                    if ( result != DialogResult.OK ) {
                        this.timerStillImage.Stop();
                        EnableConnectionControls(true);
                        return;
                    }
                    EnableConnectionControls(false);
                    this.timerStillImage.Tag = of.FileName;
                    this.timerStillImage.Start();
                }
                // Loupe
                if ( m.WParam.ToInt32() == 1235 ) {
                    Loupe.Loupe lp = new Loupe.Loupe();
                    lp.StartPosition = FormStartPosition.Manual;
                    lp.Location = new Point(this.Location.X - lp.Width - 5, this.Location.Y + 5);
                    lp.Show(this);
                }
                // show About box: check for added menu item's message ID
                if ( m.WParam.ToInt32() == 1234 ) {
                    // show About box here...
                    AboutBox dlg = new AboutBox();
                    dlg.ShowDialog();
                    dlg.Dispose();
                }
            }
            // it is essentiell to call the base behaviour
            base.WndProc(ref m);
        }

        // treat a still image the same way, as if it were a camera image
        private void timerStillImage_Tick(object sender, EventArgs e)
        {
            // get file/bmp from timer's Tag
            Bitmap bmp = new Bitmap((string)this.timerStillImage.Tag);
            // wrong image format
            if ( bmp.PixelFormat != PixelFormat.Format24bppRgb ) {
                bmp = GrzTools.BitmapTools.ConvertTo24bpp(bmp);
            }
            // shallow clone is sufficient to avoid exceptions
            _bmp = (Bitmap)bmp.Clone();
            _bmpSize = new Size(bmp.Width, bmp.Height);
            _bmpArr = new byte[bmp.Width * bmp.Height];
            bool bmpArrFilled = false;
            try {
                // show native image only
                if ( this.pictureBox_CmShowNative.Checked ) {
                    _headlineAlert = "native camera image";
                    this.pictureBox.Image = _bmp;
                    return;
                }
                // exec background subtraction and build a new bitmap from it
                if ( _settings.SubtractBackgroundImage && _bmpBkgnd != null ) {
                    GrzTools.BitmapTools.SubtractBitmap24bppToBitmap24bpp(ref _bmp, _bmpBkgnd);
                }
                // force monochrome image 
                if ( _settings.ForceGray ) {
                    GrzTools.BitmapTools.Bmp24bppColorToBmp24bppGrayToArray8bppGray(ref _bmp, ref _bmpArr);
                    bmpArrFilled = true;
                }
                // generate pseudo colors if selected
                if ( _settings.PseudoColors ) {
                    GrzTools.BitmapTools.Bmp24bppColorToBmp24bppPseudoToArray8bppGray(ref _bmp, _pal, ref _bmpArr);
                    bmpArrFilled = true;
                }
                // ensure array 24bpp gray
                if ( !bmpArrFilled ) {
                    GrzTools.BitmapTools.Bmp24bppColorToArray8bppGray(_bmp, ref _bmpArr);
                    bmpArrFilled = true;
                }
                // worker
                pictureBox_PaintWorker();
                // show image in picturebox
                this.pictureBox.Image = _bmp;
            } catch { ;}
        }

        // picture box show the pseudo color
        private void pictureBoxPseudo_Paint(object sender, PaintEventArgs e)
        {
            // multiplier between pixel intensity value and pixture box dimension
            float corr = (float)(this.pictureBoxPseudo.Width / 256.0f);
            // get inverted color at ellipse perimeter
            int xPosEllipse = (int)(_beamEllipseBorderIntensity * corr);
            Color color = ((Bitmap)this.pictureBoxPseudo.Image).GetPixel(xPosEllipse, 10);
            _ellipseColorInverted = Color.FromArgb(color.ToArgb() ^ 0xffffff);
            // show ellipse intensity
            e.Graphics.DrawLine(Pens.Black, new Point(xPosEllipse, 0), new Point(xPosEllipse, 24));
            // threshold intensity level is shown as a marker inside the pseudo color bar
            int xPosLowThreshold = (int)Math.Round((float)_settings.BeamIntensityThreshold * corr);
            Color colorThreshold = Color.FromArgb(((Bitmap)this.pictureBoxPseudo.Image).GetPixel(xPosLowThreshold, 10).ToArgb() ^ 0xffffff);
            _thresholdPen.Color = colorThreshold;
            e.Graphics.DrawLine(_thresholdPen, new Point(xPosLowThreshold, 0), new Point(xPosLowThreshold, 24));
            // mark beam peak intensity
            int xPosHotPixel = (int)Math.Round((float)_beamPeakIntensity * corr);
            e.Graphics.DrawLine(Pens.Black, new Point(xPosHotPixel, 0), new Point(xPosHotPixel, 24));
            // draw scale markers
            byte[] scale = new byte[] {0, 50, 100, 150, 200, 250};
            int xPos;
            for ( int i = 0; i < 6; i++ ) {
                xPos = (int)Math.Round((float)scale[i] * corr);
                e.Graphics.DrawLine(Pens.Black, new Point(xPos, 20), new Point(xPos, 24));
            }
        }

        // access app settings
        private void buttonSettings_Click(object sender, EventArgs e)
        {
            // transfer current app settings to _settings class
            updateSettingsFromApp();
            // start settings dialog
            Settings dlg = new Settings(_settings);
            // memorize settings
            AppSettings oldSettings = new AppSettings();
            AppSettings.CopyAllTo(_settings, out oldSettings);
            if ( dlg.ShowDialog() == DialogResult.OK ) {
                // update app settings
                updateAppFromSettings();
                // INI: write settings to ini
                _settings.WriteToIni();
            } else {
                AppSettings.CopyAllTo(oldSettings, out _settings);
            }
        }

        // change camera model
        private void devicesCombo_Click(object sender, EventArgs e)
        {
            // if camera model changes, therefore better reset
            _settings.BeamSearchOrigin = AppSettings.BeamSearchStates.CENTER;
        }

        // change camera resolution
        private void videoResolutionsCombo_Click(object sender, EventArgs e)
        {
            // if camera resolution changes, better reset
            _settings.BeamSearchOrigin = AppSettings.BeamSearchStates.CENTER;
        }

        // app acts as a server providing screenshots via TCP/IP socket connection
        // https://stackoverflow.com/questions/5527670/socket-is-not-working-as-it-should-help/5575287#5575287
        async Task sendSocketScreenshotAsync()
        {
            using ( var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) ) {
                socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 23456));
                socket.Listen(100);
                while ( _runSocketTask ) {
                    try {
                        using ( var client = socket.Accept() ) {
                            try {
                                while ( _runSocketTask ) {
                                    byte[] imageData;
                                    lock ( _socketBmp ) {
                                        if ( _socketBmp == null ) {
                                            continue;
                                        }
                                        using ( var stream = new System.IO.MemoryStream() ) {
                                            _socketBmp.Save(stream, ImageFormat.Png);
                                            imageData = stream.ToArray();
                                        }
                                    }
                                    var lengthData = BitConverter.GetBytes(imageData.Length);
                                    if ( client.Send(lengthData) < lengthData.Length ) break;
                                    if ( client.Send(imageData) < imageData.Length ) break;
                                    // non UI blocking
                                    await Task.Delay(100);
                                }
                            }
                            catch ( Exception ex ) {
                                break;
                            }
                        }
                    }
                    catch {
                        ;
                    }
                }
            }
        }

        // capture a background image, useful only w/o beam :)
        private void buttonCaptureBackground_Click(object sender, EventArgs e)
        {
            if ( MessageBox.Show(
                            "Turn beam providing source off.\n\n" +
                            "Click 'beam appearance', then 'native image'.\n\n" +
                            "Make sure Settings --> 'Subtract Backround Image' = False.\nOtherwise results become unpredictable.\n\n" +
                            "An already existing Background Image will be overridden.\n\n" +
                            "Continue?",
                            "Capture a Background Image",
                            MessageBoxButtons.OKCancel) != DialogResult.OK ) {
                return;
            }

            if ( _bmp != null ) {
                try {
                    if ( _bmpBkgnd != null ) {
                        _bmpBkgnd.Dispose();
                    }
                    System.IO.File.Delete(@"BackGroundImage.bmp");
                    _bmpBkgnd = new Bitmap((Bitmap)_bmp.Clone());
                    if ( _bmpBkgnd.PixelFormat != PixelFormat.Format24bppRgb ) {
                        _bmpBkgnd = GrzTools.BitmapTools.ConvertTo24bpp(_bmpBkgnd);
                    }
                    _bmpBkgnd.Save("BackGroundImage.bmp", ImageFormat.Bmp);
                    MessageBox.Show("'BackGroundImage.bmp' was saved", "Background Image");
                } catch ( Exception ) {
                    MessageBox.Show("'BackGroundImage.bmp' could not be saved saved.\n\nRetry after manual deletion.", "Background Image save error");
                }
            } else {
                MessageBox.Show("No image available. Please connect to camera.", "Capture Background Image");
            }
        }

    }

    // app settings
    public class AppSettings {

        // INI-Files CLass : easiest (though outdated) way to administer app specific setup data
        class IniFile {
            private string path;
            [DllImport("kernel32")]
            private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);
            [DllImport("kernel32")]
            private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);
            public IniFile(string path) {
                this.path = path;
            }
            public void IniWriteValue(string Section, string Key, string Value) {
                try {
                    WritePrivateProfileString(Section, Key, Value, this.path);
                } catch ( Exception ) {
                    MessageBox.Show("INI-File could not be saved. Please copy app folder to a writeable location.", "Error");
                }
            }
            public string IniReadValue(string Section, string Key, string DefaultValue) {
                StringBuilder retVal = new StringBuilder(255);
                int i = GetPrivateProfileString(Section, Key, DefaultValue, retVal, 255, this.path);
                return retVal.ToString();
            }
        }

        // make a copy of all class properties
        public static void CopyAllTo(AppSettings source, out AppSettings target) {
            target = new AppSettings();
            var type = typeof(AppSettings);
            foreach ( var sourceProperty in type.GetProperties() ) {
                var targetProperty = type.GetProperty(sourceProperty.Name);
                targetProperty.SetValue(target, sourceProperty.GetValue(source, null), null);
            }
            foreach ( var sourceField in type.GetFields() ) {
                var targetField = type.GetField(sourceField.Name);
                targetField.SetValue(target, sourceField.GetValue(source));
            }
        }

        // a string containing the source code sample, how to receive socket screenshot from this app
        string source = @"// example app: how to receive Bitmap screenshots from socket connection
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace ReceiveSocketImage
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if ( this.checkBox1.Checked ) {
                ThreadPool.QueueUserWorkItem(GetSnapshots);
            }
        }

        private void GetSnapshots(object state)
        {
            using ( var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) ) {
                try {
                    socket.Connect(new IPEndPoint(IPAddress.Loopback, 23456));
                    while ( this.checkBox1.Checked ) {
                        var lengthData = new byte[4];
                        var lengthBytesRead = 0;
                        while ( lengthBytesRead < lengthData.Length ) {
                            var read = socket.Receive(lengthData, lengthBytesRead, lengthData.Length - lengthBytesRead, SocketFlags.None);
                            if ( read == 0 ) {
                                Invoke(new Action(() => { this.checkBox1.Checked = false; }));
                                return;
                            }
                            lengthBytesRead += read;
                        }
                        var length = BitConverter.ToInt32(lengthData, 0);
                        var imageData = new byte[length];
                        var imageBytesRead = 0;
                        while ( imageBytesRead < imageData.Length ) {
                            var read = socket.Receive(imageData, imageBytesRead, imageData.Length - imageBytesRead, SocketFlags.None);
                            if ( read == 0 ) {
                                Invoke(new Action(() => { this.checkBox1.Checked = false; }));
                                return;
                            }
                            imageBytesRead += read;
                        }
                        using ( var stream = new MemoryStream(imageData) ) {
                            var bitmap = new Bitmap(stream);
                            Invoke(new ImageCompleteDelegate(ImageComplete), new object[] { bitmap });
                        }
                    }
                } catch {
                    Invoke(new Action(() => { this.checkBox1.Checked = false; }));
                }
            }
        }

        private delegate void ImageCompleteDelegate(Bitmap bitmap);
        private void ImageComplete(Bitmap bitmap)
        {
            if ( bitmap != null ) {
                pictureBox1.Image = bitmap;
            }
        }
    }
}
";
        // the literal name of the ini section
        private string iniSection = "GrzBeamProfiler";

        // beam search status: HIDE = don't show white cross AND keep previous search origin, CENTER = search from image center, MANUAL = search from a user chosen position
        public enum BeamSearchStates {
            HIDE = 0,
            CENTER = 1,
            MANUAL = 2
        }

        // color table enum
        public enum ColorTable {
            TEMPERATURE = 0,
            RAINBOW = 1
        }

        // beam diameter algorithm
        public enum BeamDiameter {
            FWHM = 0,                 // FWHM:    beam width at 50% (-3 dB) of beam's peak irradiance [W / m2] 
            D4SIGMA = 1,              // D4SIGMA: beam width at 4 SIGMA of beam's power standard deviation
            D86 = 2                   // D86:     beam width at 86,5% of beam's total power, aka TEM 00 Gaussian beam’s 1/e 2 value (13.5%) [W / m2] 
        }

        // custom form to show the source code sample 'receive network socket bmp' inside the property grid
        [Editor(typeof(SampleSourceForm), typeof(System.Drawing.Design.UITypeEditor))]
        class TextEditor : System.Drawing.Design.UITypeEditor {
            public override System.Drawing.Design.UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) {
                return System.Drawing.Design.UITypeEditorEditStyle.Modal;
            }
            public override object EditValue(ITypeDescriptorContext context, System.IServiceProvider provider, object value) {
                System.Windows.Forms.Design.IWindowsFormsEditorService svc = provider.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)) as System.Windows.Forms.Design.IWindowsFormsEditorService;
                String foo = value as String;
                if ( svc != null && foo != null ) {
                    using ( SampleSourceForm form = new SampleSourceForm() ) {
                        form.Value = foo;
                        if ( svc.ShowDialog(form) == DialogResult.OK ) {
                            foo = form.Value;
                        }
                    }
                }
                return value;
            }
        }
        class SampleSourceForm : Form {
            private System.Windows.Forms.TextBox textbox;
            private System.Windows.Forms.Button okButton;
            public SampleSourceForm() {
                textbox = new System.Windows.Forms.TextBox();
                textbox.Multiline = true;
                textbox.Dock = DockStyle.Fill;
                textbox.WordWrap = false;
                textbox.Font = new Font(FontFamily.GenericMonospace, textbox.Font.Size);
                textbox.ScrollBars = ScrollBars.Both;
                Controls.Add(textbox);
                okButton = new System.Windows.Forms.Button();
                okButton.Text = "OK";
                okButton.Dock = DockStyle.Bottom;
                okButton.DialogResult = DialogResult.OK;
                Controls.Add(okButton);
            }
            public string Value {
                get { return textbox.Text; }
                set { textbox.Text = value; }
            }
        }

        [Browsable(false)]
        public bool FollowBeam { get; set; }
        [Browsable(false)]
        public bool Crosshair { get; set; }
        [Browsable(false)]
        public bool ForceGray { get; set; }
        [Browsable(false)]
        public bool PseudoColors { get; set; }
        [Browsable(false)]
        public bool Ellipse { get; set; }
        [Browsable(false)]
        public bool BeamBorder { get; set; }
        [Browsable(false)]
        public bool BeamPeak { get; set; }
        [Browsable(false)]
        public bool SearchTraces { get; set; }
        [Browsable(false)]
        public bool EllipsePower { get; set; }
        [Browsable(false)]
        public bool EllipsePointsHull { get; set; }
        [Browsable(false)]
        public bool SwapProfiles { get; set; }

        [ReadOnly(true)]
        public string CameraMoniker { get; set; }
        [ReadOnly(true)]
        public Size CameraResolution { get; set; }
        [ReadOnly(true)]
        public int Exposure { get; set; }
        [ReadOnly(true)]
        public int ExposureMin { get; set; }
        [ReadOnly(true)]
        public int ExposureMax { get; set; }
        [ReadOnly(true)]
        public int Brightness { get; set; }
        [ReadOnly(true)]
        public int BrightnessMin { get; set; }
        [ReadOnly(true)]
        public int BrightnessMax { get; set; }
        [ReadOnly(true)]
        public Size FormSize { get; set; }
        [ReadOnly(true)]
        public Point FormLocation { get; set; }
        [ReadOnly(false)]
        [Description("Minimum pixel intensity level indicating a beam")]
        public int BeamIntensityThreshold { get; set; }
        [Description("Minimum required beam diameter in pixels, lower limit is 4")]
        public int BeamMinimumDiameter { get; set; }
        [Description("Beam diameter calculation type - determines power reduction")]
        public BeamDiameter BeamDiameterType { get; set; }
        [Description("Styles temperature vs. rainbow")]
        public ColorTable PseudoColorTable { get; set; }
        [ReadOnly(false)]
        [Description("Start coordinates to search a beam")]
        public BeamSearchStates BeamSearchOrigin { get; set; }
        [Description("Beam search origin offset relative to center")]
        public Point BeamSearchOffset { get; set; }
        [Description("Subtract background image")]
        public bool SubtractBackgroundImage { get; set; }
        [Description("Send screenshots to a listening app on 127.0.0.1 - see source code below")]
        public bool ProvideSocketScreenshots { get; set; }
        [Description("See sample application source code to connect to screenshot server")]
        [Editor(typeof(TextEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public String SampleSocketSource { get; set; }

        // INI: read from ini
        public void ReadFromIni() {
            int tmp;
            IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            // camera moniker string
            CameraMoniker = ini.IniReadValue(iniSection, "CameraMoniker", "empty");
            // camera resolution width
            if ( int.TryParse(ini.IniReadValue(iniSection, "CameraResolutionWidth", "100"), out tmp) ) {
                CameraResolution = new Size(tmp, 0);
            }
            // camera resolution height
            if ( int.TryParse(ini.IniReadValue(iniSection, "CameraResolutionHeight", "200"), out tmp) ) {
                CameraResolution = new Size(CameraResolution.Width, tmp);
            }
            // camera exposure 
            if ( int.TryParse(ini.IniReadValue(iniSection, "Exposure", "-5"), out tmp) ) {
                Exposure = tmp;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "ExposureMin", "-10"), out tmp) ) {
                ExposureMin = tmp;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "ExposureMax", "10"), out tmp) ) {
                ExposureMax = tmp;
            }
            // camera brightness
            if ( int.TryParse(ini.IniReadValue(iniSection, "Brightness", "0"), out tmp) ) {
                Brightness = tmp;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "BrightnessMin", "-10"), out tmp) ) {
                BrightnessMin = tmp;
            }
            if ( int.TryParse(ini.IniReadValue(iniSection, "BrightnessMax", "10"), out tmp) ) {
                BrightnessMax = tmp;
            }
            // form width
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormWidth", "657"), out tmp) ) {
                FormSize = new Size(tmp, 0);
            }
            // form height
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormHeight", "588"), out tmp) ) {
                FormSize = new Size(FormSize.Width, tmp);
            }
            // form x
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormX", "10"), out tmp) ) {
                FormLocation = new Point(tmp, 0);
            }
            // form y
            if ( int.TryParse(ini.IniReadValue(iniSection, "FormY", "10"), out tmp) ) {
                FormLocation = new Point(FormLocation.X, tmp);
            }
            // autocenter on/off
            string str = ini.IniReadValue(iniSection, "BeamSearchOrigin", "empty");
            Array values = Enum.GetValues(typeof(BeamSearchStates));
            foreach ( BeamSearchStates val in values ) {
                if ( val.ToString() == str ) {
                    BeamSearchOrigin = val;
                    break;
                }
                BeamSearchOrigin = BeamSearchStates.CENTER;
            }
            // force gray on/off
            bool check = false;
            if ( bool.TryParse(ini.IniReadValue(iniSection, "ForceGray", "false"), out check) ) {
                ForceGray = check;
            }
            // pseudo colors on/off + scrollers
            if ( bool.TryParse(ini.IniReadValue(iniSection, "PseudoColors", "false"), out check) ) {
                PseudoColors = check;
                if ( ForceGray ) {
                    PseudoColors = false;
                }
            }
            // swap profile sections
            if ( bool.TryParse(ini.IniReadValue(iniSection, "SwapProfiles", "false"), out check) ) {
                SwapProfiles = check;
            }
            // show beam render options
            FollowBeam = true;
            Crosshair = true;
            Ellipse = true;
            BeamBorder = true;
            BeamPeak = true;
            SearchTraces = false;
            EllipsePower = false;
            EllipsePointsHull = false;
            // brightness threshold
            if ( int.TryParse(ini.IniReadValue(iniSection, "BeamIntensityThreshold", "50"), out tmp) ) {
                BeamIntensityThreshold = tmp;
            }
            // pseudo color table
            str = ini.IniReadValue(iniSection, "PseudoColorTable", "empty");
            values = Enum.GetValues(typeof(ColorTable));
            foreach ( ColorTable val in values ) {
                if ( val.ToString() == str ) {
                    PseudoColorTable = val;
                    break;
                }
                PseudoColorTable = ColorTable.TEMPERATURE;
            }
            // beam diameter type
            str = ini.IniReadValue(iniSection, "BeamDiameterType", "empty");
            values = Enum.GetValues(typeof(BeamDiameter));
            foreach ( BeamDiameter val in values ) {
                if ( val.ToString() == str ) {
                    BeamDiameterType = val;
                    break;
                }
                BeamDiameterType = BeamDiameter.FWHM;
            }
            // SubtractBackgroundImage
            if ( bool.TryParse(ini.IniReadValue(iniSection, "SubtractBackgroundImage", "false"), out check) ) {
                SubtractBackgroundImage = check;
            }
            // minimal required beam diameter
            if ( int.TryParse(ini.IniReadValue(iniSection, "BeamMinimumDiameter", "10"), out tmp) ) {
                BeamMinimumDiameter = tmp;
            }
            // center image offset
            str = ini.IniReadValue(iniSection, "BeamSearchOffset", "{X=0,Y=0}");
            string[] coords = System.Text.RegularExpressions.Regex.Replace(str, @"[\{\}a-zA-Z=]", "").Split(',');
            Point point = new Point(int.Parse(coords[0]), int.Parse(coords[1]));
            BeamSearchOffset = point;
            // socket screenshots
            if ( bool.TryParse(ini.IniReadValue(iniSection, "ProvideSocketScreenshots", "false"), out check) ) {
                ProvideSocketScreenshots = check;
            }
            // source code for screenshot connector  
            SampleSocketSource = source;
        }

        // INI: write to ini
        public void WriteToIni() {
            IniFile ini = new IniFile(System.Windows.Forms.Application.ExecutablePath + ".ini");
            // camera moniker string
            ini.IniWriteValue(iniSection, "CameraMoniker", CameraMoniker);
            // camera resolution width
            ini.IniWriteValue(iniSection, "CameraResolutionWidth", CameraResolution.Width.ToString());
            // camera resolution height
            ini.IniWriteValue(iniSection, "CameraResolutionHeight", CameraResolution.Height.ToString());
            // camera exposure
            ini.IniWriteValue(iniSection, "Exposure", Exposure.ToString());
            ini.IniWriteValue(iniSection, "ExposureMin", ExposureMin.ToString());
            ini.IniWriteValue(iniSection, "ExposureMax", ExposureMax.ToString());
            // camera brightness
            ini.IniWriteValue(iniSection, "Brightness", Brightness.ToString());
            ini.IniWriteValue(iniSection, "BrightnessMin", BrightnessMin.ToString());
            ini.IniWriteValue(iniSection, "BrightnessMax", BrightnessMax.ToString());
            // form width
            ini.IniWriteValue(iniSection, "FormWidth", FormSize.Width.ToString());
            // form height
            ini.IniWriteValue(iniSection, "FormHeight", FormSize.Height.ToString());
            // form width
            ini.IniWriteValue(iniSection, "FormX", FormLocation.X.ToString());
            // form height
            ini.IniWriteValue(iniSection, "FormY", FormLocation.Y.ToString());
            // auto center
            ini.IniWriteValue(iniSection, "BeamSearchOrigin", BeamSearchOrigin.ToString());
            // force gray
            ini.IniWriteValue(iniSection, "ForceGray", ForceGray.ToString());
            // pseudo colors
            ini.IniWriteValue(iniSection, "PseudoColors", PseudoColors.ToString());
            // swap profile sections
            ini.IniWriteValue(iniSection, "SwapProfiles", SwapProfiles.ToString());
            // brightness threshold
            ini.IniWriteValue(iniSection, "BeamIntensityThreshold", BeamIntensityThreshold.ToString());
            // pseudo color table 
            ini.IniWriteValue(iniSection, "PseudoColorTable", PseudoColorTable.ToString());
            // beam diameter type 
            ini.IniWriteValue(iniSection, "BeamDiameterType", BeamDiameterType.ToString());
            // SubtractBackgroundImage
            ini.IniWriteValue(iniSection, "SubtractBackgroundImage", SubtractBackgroundImage.ToString());
            // minimal required blob diameter
            ini.IniWriteValue(iniSection, "BeamMinimumDiameter", BeamMinimumDiameter.ToString());
            // center image offset
            ini.IniWriteValue(iniSection, "BeamSearchOffset", BeamSearchOffset.ToString());
            // socket screenshots
            ini.IniWriteValue(iniSection, "ProvideSocketScreenshots", ProvideSocketScreenshots.ToString());
        }

    }

}

