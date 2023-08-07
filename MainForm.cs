using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices; // DLLImport
using System.Linq;
using AForge.Video.DirectShow;
using System.Threading.Tasks;
using System.Diagnostics;
using AForge.Controls;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
using GrzBeamProfiler.Properties;

namespace GrzBeamProfiler
{
    public partial class MainForm : Form, IMessageFilter
    {
        AppSettings _settings = new AppSettings();       // app settings

        private FilterInfoCollection _videoDevices;      // AForge collection of camera devices
        private VideoCaptureDevice _videoDevice = null;  // AForge camera device
        private int _videoDeviceRestartCounter = 0;      // video device restart counter per app session

        private string _buttonConnectString;             // button text, a lame method to distinguish between camera "connect" vs. "- stop -" 

        Bitmap _bmp = null;                              // copy of current camera frame
        Size _bmpSize = new Size();                      // dimensions of current camera frame 
        byte[] _bmpArr;                                  // all bmp pixel values in a separate array; allows fast access
        ImageType _bmpImageType = ImageType.RGBColor;    // _bmp could be either 'colored RGB' or 'R=G=B=gray RGB'  
        enum ImageType {                                 
            RGBColor,
            RGBGray,
        }

        Point _redCross = new Point();                   // red cross is the beam's centroid position, relative to canvas 
        Point _redCrossLast = new Point();               //  - " - memorize red cross position after <ESC> / right click 
        bool _redCrossFollowsBeam = true;                // flag controls whether red cross/ellipse will follow a moving beam
        byte _centerGrayByte;                            // beam's center pixel's gray value 

        Point _beamSearchOriginCurrent = new Point();    // memorize the current beam search offset, needed if white cross is hidden 
        
        bool _justConnected = false;                     // just connected means: the stage right after the camera was started, it ends after the first image was processed
        double _fps = 0;                                 // camera fps

        bool _paintBusy = false;                         // busy flag in this.pictureBox_PaintWorker method
        int _beamDiameter = 0;                           // beam diameter, actually made from the two ellipse axis  
        float _multiplierBmp2Paint;                      // multiplier between _bmp and tableLayoutPanelGraphs_Paint / pictureBox_MouseDown
        Point[] _horProf;                                // beam horizontal pixel power profile
        Point[] _verProf;                                // beam vertical pixel power profile

        Task _socketTask;                                // another app can connect via network socket to GrzBeamProfiler to receive screenshots
        bool _runSocketTask = false;
        Bitmap _socketBmp = null;

        EllipseParam _ep = new EllipseParam();           // the beam's standardized ellipse shape 

        byte[] _bmpBkgnd = null;                         // background image to reduce the nose level around a beam

        palette _pal = new palette();                    // pseudo color palette
        AForge.Imaging.Filters.ColorRemapping _filter;   // pseudo color filter

        // one time init pens
        Pen _penEllipse = new Pen(Color.FromArgb(255, 255, 0, 0), 3);
        Pen _thickRedPen = new Pen(Color.FromArgb(255, 255, 0, 0), 3);
        Pen _thickYellowPen = new Pen(Color.FromArgb(255, 255, 255, 0), 3);

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

        // pictureBox context menu vars
        ContextMenu pictureBox_Cm = new ContextMenu();
        MenuItem pictureBox_CmShowCrosshair = new MenuItem("show crosshair (ESC toggles too)");
        MenuItem pictureBox_CmCrosshairFollowsBeam = new MenuItem("crosshair follows beam");
        MenuItem pictureBox_CmForceGray = new MenuItem("show gray color");
        MenuItem pictureBox_CmPseudoColors = new MenuItem("show pseudo color");
        MenuItem pictureBox_CmShowSearchOrigin = new MenuItem("show beam search origin");
        MenuItem pictureBox_CmSearchCenter = new MenuItem("beam search center");
        MenuItem pictureBox_CmSearchManual = new MenuItem("beam search manual (SHIFT+left mouse)");
        MenuItem pictureBox_CmBeamDiameter = new MenuItem("");
        MenuItem pictureBox_CmBeamThreshold = new MenuItem("beam power threshold: ");
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
            this.pictureBox_Cm.MenuItems.Add("-");
            cmi = new GrzTools.CustomMenuItem();
            cmi.Text = "Beam appearance";
            cmi.Font = cmFont;
            this.pictureBox_Cm.MenuItems.Add(cmi);
            this.pictureBox_CmShowCrosshair.Checked = true;
            this.pictureBox_CmShowCrosshair.Click += pictureBox_CmClickShowCrosshair;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmShowCrosshair);
            this.pictureBox_CmForceGray.Click += pictureBox_CmClickForceGray;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmForceGray);
            this.pictureBox_CmPseudoColors.Click += pictureBox_CmClickPseudoColors;
            this.pictureBox_Cm.MenuItems.Add(this.pictureBox_CmPseudoColors);
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

            // memorize the connect button text
            _buttonConnectString = this.connectButton.Text;

            // add "about entry" to system menu
            SetupSystemMenu();

            // get background image from file, if existing
            if ( System.IO.File.Exists("BackGroundImage.bin") ) {
                _bmpBkgnd = System.IO.File.ReadAllBytes("BackGroundImage.bin");
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

// export pseudo color table
//#if DEBUG
//            string text = "";
//            for ( int i = 0; i < 256; i++ ) {
//                text += _pal.mapR[i].ToString() + ";" + _pal.mapG[i].ToString() + ";" + _pal.mapB[i].ToString() + "\r";
//            }
//            System.IO.File.WriteAllText("test.col", text);
//#endif

        }

        // update app from settings
        void updateAppFromSettings()
        {
            // essential app settings
            this.Size = _settings.FormSize;
            this.Location = _settings.FormLocation;

            // context menu
            this.pictureBox_CmForceGray.Checked = _settings.ForceGray;
            this.pictureBox_CmPseudoColors.Checked = _settings.PseudoColors;

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

            // pseudo color table is special
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
            this.pictureBox_CmBeamThreshold.Text = "beam power threshold: " + _settings.BeamPowerThreshold.ToString();

            // init logger
            GrzTools.Logger.FullFileNameBase = System.Windows.Forms.Application.ExecutablePath; // option: if useful, could be set to another location
            GrzTools.Logger.WriteToLog = true;                                                  // option: if useful, could be set otherwise    
            
            // SubtractBackgroundImage
            if ( _settings.SubtractBackgroundImage ) {
                if ( _bmpBkgnd == null ) {
                    MessageBox.Show("Please capture a background image, otherwise backgound subtraction doesn't work.");
                }
            } else {
                _bmpBkgnd = new byte[0];
                _bmpBkgnd = null;
                if ( System.IO.File.Exists("BackGroundImage.bin") ) {
                    System.IO.File.Delete("BackGroundImage.bin");
                }
            }
        }

        // update settings from app
        void updateSettingsFromApp()
        {
            _settings.FormSize = this.Size;
            _settings.FormLocation = this.Location;
            _settings.ForceGray = this.pictureBox_CmForceGray.Checked;
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
            DialogResult dr = GrzTools.InputDialog.GetText("Beam power threshold", _settings.BeamPowerThreshold.ToString(), this, out retVal);
            int intVal;
            int.TryParse(retVal, out intVal);
            _settings.BeamPowerThreshold = Math.Min(250, Math.Max(10, intVal));
            this.pictureBox_CmBeamThreshold.Text = "beam power threshold: " + _settings.BeamPowerThreshold.ToString();
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
        }
        void pictureBox_CmClickShowCrosshair(object sender, EventArgs e) {
            if ( _videoDevice != null && _videoDevice.IsRunning ) {
                toggleCrossHairVisibility();
            } else {
                MessageBox.Show("Only changeable, if camera is running.", "Info");
            }
        }
        void pictureBox_CmClickCrosshaisFollowsBeam(object sender, EventArgs e) {
            _redCrossFollowsBeam = !_redCrossFollowsBeam;
            ((MenuItem)sender).Checked = _redCrossFollowsBeam;
            headLine();
        }
        void pictureBox_CmClickForceGray(object sender, EventArgs e) {
            _settings.ForceGray = !_settings.ForceGray;
            ((MenuItem)sender).Checked = _settings.ForceGray;
        }
        void pictureBox_CmClickPseudoColors(object sender, EventArgs e) {
            _settings.PseudoColors = !_settings.PseudoColors;
            ((MenuItem)sender).Checked = _settings.PseudoColors;
        }
        // hide beam search origin BUT keep the previously selected origin
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
        // settings from beam search status
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
            // crossHair location
            _redCrossLast = new Point(this.pictureBox.Size.Width/2, this.pictureBox.Size.Height/2);
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

            // only null at 1st enter
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
                        MessageBox.Show("No camera according to app settings was found.", "Note");
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

            // immediately stop rendering images
            _paintBusy = true;

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
            string change = "";
            // _videoDevice moniker may not match to app _settings
            if ( _settings.CameraMoniker != _videoDevices[devicesCombo.SelectedIndex].MonikerString ) {
                _settings.CameraMoniker = _videoDevices[devicesCombo.SelectedIndex].MonikerString;
                change = "model";
            }
            // _videoDevice resolution may not match to app _settings
            if ( !evaluateCameraFrameSizes(_videoDevice) ) {
                change += change.Length > 0 ? ", " : "";
                change += "resolution";
            }
            // _videoDevice exposure / brightness may not match to app _settings
            if ( !evaluateCameraExposureBrightnessProps() ) {
                change += change.Length > 0 ? ", " : "";
                change += "exposure";
            }
            // camera vs. _settings mismatch note
            if ( change.Length > 0 ) {
                MessageBox.Show(String.Format("Camera '{0}' did not match to app settings, latter are now updated.", change), "Note");
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
                _settings.CameraResolution = new Size(_videoDevice.VideoCapabilities[this.videoResolutionsCombo.SelectedIndex].FrameSize.Width, _videoDevice.VideoCapabilities[this.videoResolutionsCombo.SelectedIndex].FrameSize.Height);
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
                // connect to camera if feasible
                if ( (_videoDevice == null) || (_videoDevice.VideoCapabilities == null) || (_videoDevice.VideoCapabilities.Length == 0) || (this.videoResolutionsCombo.Items.Count == 0) ) {
                    return;
                }
                _paintBusy = false;
                _videoDevice.VideoResolution = _videoDevice.VideoCapabilities[videoResolutionsCombo.SelectedIndex];
                _videoDevice.Start();
                _justConnected = true;
                _videoDevice.NewFrame += new AForge.Video.NewFrameEventHandler(videoDevice_NewFrame);

                // fire & forget: in case, the _videoDevice won't start within 10s or _justConnected is still true (aka no videoDevice_NewFrame event)
                Task.Delay(10000).ContinueWith(t => {
                    Invoke(new Action(async () => {
                        // trigger for delayed action: camera is clicked on, aka  shows '- stop -'  AND camera is not running OR no new frame event happened
                        if ( _buttonConnectString != this.connectButton.Text && (!_videoDevice.IsRunning || _justConnected) ) {
                            if ( _videoDeviceRestartCounter < 5 ) {
                                _videoDeviceRestartCounter++;
                                if ( !_videoDevice.IsRunning ) {
                                    GrzTools.Logger.logTextLn(DateTime.Now, String.Format("connectButton_Click: _videoDevice is not running"));
                                }
                                if ( _justConnected ) {
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
        class palette
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
            pal.mapR[255] = 255;
            pal.mapG[255] = 255;
            pal.mapB[255] = 255;

            return pal;
        }
        System.Drawing.Image convertGrayToPseudoColors(Bitmap bitmap, bool wantPseudoColor, AForge.Imaging.Filters.ColorRemapping filter, out ImageType imageType, out byte[] bmpArr)
        {
            // do we have a monochrome RGB image OR a colored RGB OR whatever
            imageType = bmpType(bitmap);

            // generate pseudo colors from a gray image
            if ( wantPseudoColor ) {
                // if bitmap is not yet gray, convert it to gray 
                if ( imageType == ImageType.RGBColor ) {
                    bitmap = MakeGrayscaleGDI(bitmap);
                }
                // have all bitmap data in a separate byte array 
                bmpArr = GrzTools.BitmapTools.Bitmap24bppToByteArray(bitmap);

                // exec background subtraction and build a new bitmap from it
                if ( _settings.SubtractBackgroundImage && (_bmpBkgnd != null) && (bmpArr.Length == _bmpBkgnd.Length) ) {
                    for ( int i = 0; i < bmpArr.Length; i++ ) {
                        bmpArr[i] = (byte)Math.Max(0, (int)bmpArr[i] - (int)_bmpBkgnd[i]);
                    }
                    bitmap = GrzTools.BitmapTools.ByteArrayToBitmap(_bmpSize.Width, _bmpSize.Height, bmpArr);
                }

                // apply AForge pseudo color filter
                bitmap = filter.Apply(bitmap);
            } else {
                // leave bitmap untouched and return bitmap's native pixel values as array with color data
                bmpArr = GrzTools.BitmapTools.Bitmap24bppToByteArray(bitmap);
                // exec background subtraction and build a new bitmap from it
                if ( _settings.SubtractBackgroundImage && (_bmpBkgnd != null) && (bmpArr.Length == _bmpBkgnd.Length) ) {
                    for ( int i = 0; i < bmpArr.Length; i++ ) {
                        bmpArr[i] = (byte)Math.Max(0, (int)bmpArr[i] - (int)_bmpBkgnd[i]);
                    }
                    bitmap = GrzTools.BitmapTools.ByteArrayToBitmap(_bmpSize.Width, _bmpSize.Height, bmpArr);
                }
            }

            return bitmap;
        }

        // lame 5 pixel test whether a bitmap is "3x identical gray" or colored RGB 
        ImageType bmpType(Bitmap bitmap)
        {
            ImageType type = ImageType.RGBColor;
            Color c = bitmap.GetPixel(10, 10);
            if ( (c.R == c.G) && (c.R == c.B) ) {
                type = ImageType.RGBGray;
                c = bitmap.GetPixel(bitmap.Width-10, 10);
                if ( (c.R == c.G) && (c.R == c.B) ) {
                    type = ImageType.RGBGray;
                    c = bitmap.GetPixel(bitmap.Width-10, bitmap.Height-10);
                    if ( (c.R == c.G) && (c.R == c.B) ) {
                        type = ImageType.RGBGray;
                        c = bitmap.GetPixel(10, bitmap.Height-10);
                        if ( (c.R == c.G) && (c.R == c.B) ) {
                            type = ImageType.RGBGray;
                            c = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
                            if ( (c.R == c.G) && (c.R == c.B) ) {
                                type = ImageType.RGBGray;
                            } else {
                                type = ImageType.RGBColor;
                            }
                        } else {
                            type = ImageType.RGBColor;
                        }
                    } else {
                        type = ImageType.RGBColor;
                    }
                } else {
                    type = ImageType.RGBColor;
                }
            } else {
                type = ImageType.RGBColor; ;
            }
            return type;
        }

        // Bitmap ring buffer for 5 images
        static class BmpRingBuffer {
            private static Bitmap[] bmpArr = new Bitmap[] { new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1), new Bitmap(1, 1) };
            private static int bmpNdx = 0;
            // public get & set
            public static Bitmap bmp {
                // always return the penultimate bmp
                get {
                    int prevNdx = bmpNdx - 1;
                    if ( prevNdx < 0 ) {
                        prevNdx = 4;
                    }
                    return bmpArr[prevNdx];
                }
                // override bmp in array and increase array index
                set {
                    bmpArr[bmpNdx].Dispose();
                    bmpArr[bmpNdx] = value;
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
                // put latest image into ring buffer
                BmpRingBuffer.bmp = (Bitmap)eventArgs.Frame.Clone();

                // action after the first received image 
                if ( _justConnected ) {
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

            //
            // loop as long as camera is running
            //
            while ( _videoDevice != null && _videoDevice.IsRunning ) {

                // calc fps
                DateTime now = DateTime.Now;
                double revFps = (double)(now - lastFrameTime).TotalMilliseconds;
                lastFrameTime = now;
                _fps = 1000.0f / revFps;
                long procMs = 0;
                int excStep = -1;

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
                    if ( _justConnected ) {
                        // reset flag
                        _justConnected = false;
                        // if 1st time connected, adjust the canvas matching to the presumable new aspect ratio of the bmp
                        Invoke(new Action(() => { adjustMainFormSize(_bmpSize); }));
                    }

                    // force monochrome camera image
                    excStep = 3; 
                    if ( _settings.ForceGray ) {
                        _bmp = MakeGrayscaleGDI(_bmp);
                    }

                    // generate pseudo color image if selected
                    excStep = 4;
                    if ( _settings.PseudoColors ) {
                        _bmp = (Bitmap)convertGrayToPseudoColors(_bmp, true, _filter, out _bmpImageType, out _bmpArr);
                    } else {
                        excStep = 5;
                        _bmpArr = GrzTools.BitmapTools.Bitmap24bppToByteArray(_bmp);
                    }

                    // _bmpArr is null, if both previous steps 3 and 4 are unchecked
                    if ( _bmpArr == null ) {
                        excStep = 5;
                        _bmpArr = GrzTools.BitmapTools.Bitmap24bppToByteArray(_bmp);
                    }

                    // dispose the previous image
                    excStep = 6;
                    if ( this.pictureBox.Image != null ) {
                        this.pictureBox.Image.Dispose();
                    }

                    //
                    // show current image in pictureBox: all image processing takes place in pictureBox_PaintWorker
                    //
                    excStep = 7;
                    Invoke(new Action(() => {
                        // image processing needs at least gray scale, pseudo color is optional 
                        if ( _settings.ForceGray ) {
                            // full image processing: heavily modifies _bmp
                            pictureBox_PaintWorker();
                        } else {
                            // w/o gray scale only show native frame, with empty profile scales
                            this.tableLayoutPanelGraphs.Invalidate(false);
                            this.tableLayoutPanelGraphs.Update();
                        }
                        // set _bmp to pictureBox
                        this.pictureBox.Image = (Bitmap)_bmp.Clone();
                    }));

                    // get process time in ms
                    swFrameProcessing.Stop();
                    procMs = swFrameProcessing.ElapsedMilliseconds;

                    // update window title
                    excStep = 8;
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

        // added to the current video frame output on screen: extended red cross axis + vertical & horizontal power profiles
        private void tableLayoutPanelGraphs_Paint(object sender, PaintEventArgs e)
        {
            // power profile areas will render on a black background to the left and bottom of camera image
            e.Graphics.FillRectangle(Brushes.Black, new Rectangle(0, 0, 104, this.tableLayoutPanelGraphs.ClientSize.Height));
            e.Graphics.FillRectangle(Brushes.Black, new Rectangle(0, this.tableLayoutPanelGraphs.ClientSize.Height - 104, this.tableLayoutPanelGraphs.ClientSize.Width, 104));

            // 2x power intensity axis scaling
            Pen pen = new Pen(Brushes.Gray);
            pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
            for ( int i = 0; i < 6; i++ ) {
                e.Graphics.DrawLine(pen, new Point(20 * i, 0), new Point(20 * i, this.tableLayoutPanelGraphs.ClientSize.Height - 104));
                e.Graphics.DrawLine(pen, new Point(104, this.tableLayoutPanelGraphs.ClientSize.Height - 20 * i - 1), new Point(this.tableLayoutPanelGraphs.ClientSize.Width, this.tableLayoutPanelGraphs.ClientSize.Height - 20 * i - 1));
            }

            // get out in case of native, unmodified frame
            if ( !_settings.ForceGray && !_settings.PseudoColors ) {
                return;
            }

            // render the beam power profiles _horProf & _verProf, previously generated in pictureBox_PaintWorker
            int ofs;
            try {
                // show power profiles along both ellipse's main axis
                if ( (_horProf != null) && (_horProf.Length > 0) ) {
                    Point[] points = new Point[_horProf.Length];
                    int ndx = 0;
                    double lastX = 0;
                    double gray, xpos, ypos;
                    foreach ( Point pt in _horProf ) {
                        ofs = pt.Y * _bmpSize.Width * 3 + pt.X * 3;
                        if ( (ofs > 0) && (ofs < _bmpArr.Length) ) {
                            gray = _bmpArr[ofs];
                            if ( _bmpImageType == ImageType.RGBColor ) {
                                gray = 0.2125 * (double)(_bmpArr[ofs + 2]) + 0.7154 * (double)(_bmpArr[ofs + 1]) + 0.0721 * (double)(_bmpArr[ofs + 0]);
                            }
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
                        ofs = pt.Y * _bmpSize.Width * 3 + pt.X * 3;
                        if ( (ofs > 0) && (ofs < _bmpArr.Length) ) {
                            gray = _bmpArr[ofs];
                            if ( _bmpImageType == ImageType.RGBColor ) {
                                gray = 0.2125 * (double)(_bmpArr[ofs + 2]) + 0.7154 * (double)(_bmpArr[ofs + 1]) + 0.0721 * (double)(_bmpArr[ofs + 0]);
                            }
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

                // always (regardless whether ellipse or circle) extend red lines coming from the beam centroid into the power profile scale
                if ( _redCross.X != -1 ) {
                    e.Graphics.DrawLine(Pens.Red, 0, _redCross.Y * _multiplierBmp2Paint, 104, _redCross.Y * _multiplierBmp2Paint);
                    e.Graphics.DrawLine(Pens.Red, 104 + _redCross.X * _multiplierBmp2Paint, this.tableLayoutPanelGraphs.ClientSize.Height - 104, 104 + _redCross.X * _multiplierBmp2Paint, this.tableLayoutPanelGraphs.ClientSize.Height);
                }

                // show coordinates & intensity value of cross center in the lower left corner
                e.Graphics.DrawLine(Pens.Yellow, 0, this.tableLayoutPanelGraphs.ClientSize.Height - 1, 100, this.tableLayoutPanelGraphs.ClientSize.Height - 1);
                e.Graphics.DrawLine(Pens.Yellow, 0, this.tableLayoutPanelGraphs.ClientSize.Height - 101, 100, this.tableLayoutPanelGraphs.ClientSize.Height - 101);
                e.Graphics.DrawLine(Pens.Yellow, 0, this.tableLayoutPanelGraphs.ClientSize.Height - 101, 0, this.tableLayoutPanelGraphs.ClientSize.Height - 1);
                e.Graphics.DrawLine(Pens.Yellow, 100, this.tableLayoutPanelGraphs.ClientSize.Height - 101, 100, this.tableLayoutPanelGraphs.ClientSize.Height - 1);
                int crossCenterPositionInGrayArr;
                string ctrX = "";
                string ctrY = "";
                if ( (_ep.center != new Point(0, 0)) && (_ep.center.X > 0) ) {
                    // ellipse based
                    crossCenterPositionInGrayArr = (int)_ep.center.Y * _bmpSize.Width * 3 + (int)_ep.center.X * 3;
                    try {
                        _centerGrayByte = _bmpArr[crossCenterPositionInGrayArr];
                    } catch ( IndexOutOfRangeException ) {
                        return;
                    }
                    ctrX = ((int)(_ep.center.X)).ToString();
                    ctrY = ((int)(_ep.center.Y)).ToString();
                }
                int yofs = this.tableLayoutPanelGraphs.ClientSize.Height - 98;
                e.Graphics.DrawString("ctrX\t" + ctrX, this.Font, Brushes.Yellow, new Point(5, yofs));
                e.Graphics.DrawString("ctrY\t" + ctrY, this.Font, Brushes.Yellow, new Point(5, yofs + this.Font.Height + 3));
                e.Graphics.DrawString("gray\t" + _centerGrayByte.ToString(), this.Font, Brushes.Yellow, new Point(5, yofs + 2 * (this.Font.Height + 3)));
//                e.Graphics.DrawString("power\t" + _ep.power.ToString(), this.Font, Brushes.Yellow, new Point(5, yofs + 2 * (this.Font.Height + 3)));
                e.Graphics.DrawString("diam\t" + _beamDiameter.ToString() + " (" + _settings.BeamMinimumDiameter.ToString() + ")", this.Font, Brushes.Yellow, new Point(5, yofs + 3 * (this.Font.Height + 3)));
                e.Graphics.DrawString("a\t" + _ep.a.ToString(), this.Font, Brushes.Yellow, new Point(5, yofs + 4 * (this.Font.Height + 3)));
                e.Graphics.DrawString("b\t" + _ep.b.ToString(), this.Font, Brushes.Yellow, new Point(5, yofs + 5 * (this.Font.Height + 3)));
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
            try {

                using ( Graphics g = Graphics.FromImage(_bmp) ) {

                    // busy
                    if ( _paintBusy ) {
                        this.Text = "Paint(busy): ";
                        return;
                    }
                    _paintBusy = true;

                    // no image to show but show red cross
                    if ( _bmp == null ) {
                        g.DrawLine(Pens.Red, new Point(0, _redCross.Y), new Point(this.pictureBox.ClientSize.Width, _redCross.Y));
                        g.DrawLine(Pens.Red, new Point(_redCross.X, 0), new Point(_redCross.X, this.pictureBox.ClientSize.Height));
                        _paintBusy = false;
                        return;
                    }

                    // no action w/o reasonable red cross coordinates: happens 'on purpose' after right click to make overlay disappear 
                    if ( _redCross.X < 0 || _redCross.Y < 0 ) {
                        _paintBusy = false;
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
                        PointF beamCentroid = new PointF();

                        // let the red cross follow the hot spot, aka beam blob 
                        if ( _redCrossFollowsBeam ) {
                            try {
                                //
                                //  main image procesing
                                //
                                blobError = getBeamBlob(_bmpSize, _bmpArr, _settings.BeamPowerThreshold, _settings.BeamMinimumDiameter, out beamPolygon, out beamCentroid, out _ep);
                                //
                            } catch ( Exception ex ) {
                                this.Text = "Paint(0): " + ex.Message;
                                _paintBusy = false;
                                return;
                            }
                            try {
                                // red cross center coordinates
                                if ( (_ep.center.X > 0) && (_ep.center.Y > 0) ) {
                                    _redCross.X = _ep.center.X;
                                    _redCross.Y = _ep.center.Y;
                                }
                                // approximate blob diameter
                                _beamDiameter = _ep.a + _ep.b;
                                // don't render ellipse, if blob is too small
                                if ( _beamDiameter < _settings.BeamMinimumDiameter ) {
                                    _ep = new EllipseParam();
                                }
                            } catch ( Exception ex ) {
                                this.Text = "Paint(1): " + ex.Message;
                                _paintBusy = false;
                                return;
                            }
                        }

                        // clear beam power profiles
                        _horProf = new Point[0];
                        _verProf = new Point[0];

                        // delta is used to determine an enclosing circle, respectively the cross sections along the ellipse's main axis
                        float delta = (_ep.a > _ep.b) ? _ep.a + 150 : _ep.b + 150;
                        // delta needs to be limited to the actual image dimensions (assuming height is smaller than width)
                        if ( delta > _bmpSize.Height / 2 - 20 ) {
                            delta -= 120;
                        }
                        
                        // show red cross: rendering here allows the profile data to override the red cross
                        try {
                            if ( (delta > 0) && (_ep.center != new Point(0, 0)) ) {
                                //
                                // overlay beam as ellipse: the red cross orientatation shall follow both gravity axis of the ellipse
                                //
                                double theta = _ep.theta;
                                double adjacentH = delta * Math.Cos(Math.PI * theta / 180.0);
                                double oppositeH = delta * Math.Sin(Math.PI * theta / 180.0);
                                double xIntersecCircleH1 = _ep.center.X + adjacentH;
                                double yIntersecCircleH1 = _ep.center.Y + oppositeH;
                                double xIntersecCircleH2 = _ep.center.X - adjacentH;
                                double yIntersecCircleH2 = _ep.center.Y - oppositeH;
                                double adjacentV = delta * Math.Cos(Math.PI / 2 + Math.PI * theta / 180.0);
                                double oppositeV = delta * Math.Sin(Math.PI / 2 + Math.PI * theta / 180.0);
                                double xIntersecCircleV1 = _ep.center.X + adjacentV;
                                double yIntersecCircleV1 = _ep.center.Y + oppositeV;
                                double xIntersecCircleV2 = _ep.center.X - adjacentV;
                                double yIntersecCircleV2 = _ep.center.Y - oppositeV;
                                g.DrawLine(Pens.Red, new Point((int)xIntersecCircleH1, (int)yIntersecCircleH1), new Point((int)xIntersecCircleH2, (int)yIntersecCircleH2));
                                g.DrawLine(Pens.Red, new Point((int)xIntersecCircleV1, (int)yIntersecCircleV1), new Point((int)xIntersecCircleV2, (int)yIntersecCircleV2));
                                if ( _settings.ProvideSocketScreenshots ) {
                                    sg.DrawLine(Pens.Red, new Point((int)xIntersecCircleH1, (int)yIntersecCircleH1), new Point((int)xIntersecCircleH2, (int)yIntersecCircleH2));
                                    sg.DrawLine(Pens.Red, new Point((int)xIntersecCircleV1, (int)yIntersecCircleV1), new Point((int)xIntersecCircleV2, (int)yIntersecCircleV2));
                                }

                                // beam power profiles: get Points along the 2 sections thru ellipse by using linear function y = mx + n
                                double divider = (xIntersecCircleH2 - xIntersecCircleH1) == 0 ? 1 : (xIntersecCircleH2 - xIntersecCircleH1);
                                double m = (double)(yIntersecCircleH2 - yIntersecCircleH1) / divider;
                                double n = (double)yIntersecCircleH1 - (double)xIntersecCircleH1 * m;
                                int start = (int)Math.Min(xIntersecCircleH1, xIntersecCircleH2);
                                int stop = (int)Math.Max(xIntersecCircleH1, xIntersecCircleH2);
                                try {
                                    _horProf = new Point[stop - start];
                                } catch ( OutOfMemoryException oome ) {
                                    this.Text = "Paint(6): " + oome.Message;
                                    _paintBusy = false;
                                    return;
                                }
                                int ndx = 0;
                                for ( int x = start; x < stop; x++ ) {
                                    _horProf[ndx++] = new Point(x, (int)(m * (double)x + (double)n));
                                }
                                divider = (xIntersecCircleV2 - xIntersecCircleV1) == 0 ? 1 : (xIntersecCircleV2 - xIntersecCircleV1);
                                m = (double)(yIntersecCircleV2 - yIntersecCircleV1) / divider;
                                n = (double)yIntersecCircleV1 - (double)xIntersecCircleV1 * m;
                                start = (int)Math.Min(yIntersecCircleV1, yIntersecCircleV2);
                                stop = (int)Math.Max(yIntersecCircleV1, yIntersecCircleV2);
                                _verProf = new Point[stop - start];
                                ndx = 0;
                                for ( int y = start; y < stop; y++ ) {
                                    _verProf[ndx++] = new Point((int)(1.0f / m * ((double)y - (double)n)), (int)y);
                                }

                                // extend red cross endpoints with surrounding circle to left and bottom profile areas
                                float[] dashValues = { 1, 5 };
                                Pen dashPen = new Pen(Color.White);
                                dashPen.DashPattern = dashValues;
                                g.DrawLine(Pens.White, new Point((int)xIntersecCircleH1, (int)yIntersecCircleH1), new Point((int)xIntersecCircleH1, _bmp.Height));
                                g.DrawLine(Pens.White, new Point((int)xIntersecCircleH2, (int)yIntersecCircleH2), new Point((int)xIntersecCircleH2, _bmp.Height));
                                g.DrawLine(Pens.White, new Point((int)xIntersecCircleV1, (int)yIntersecCircleV1), new Point(0, (int)yIntersecCircleV1));
                                g.DrawLine(Pens.White, new Point((int)xIntersecCircleV2, (int)yIntersecCircleV2), new Point(0, (int)yIntersecCircleV2));
                            }
                        } catch ( Exception ex ) {
                            this.Text = "Paint(2): " + ex.Message;
                            _paintBusy = false;
                            return;
                        }

                        // show real beam border polygon --> according to beam threshold
                        Pen penPolygon = _settings.BeamPowerThreshold < 100 ? Pens.White : Pens.Black;
                        if ( _settings.DebugSearchTrace ) {
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

                        // show beam enclosing ellipse --> according to beam power calculation method (FWHM, D86, Euler)
                        if ( _ep.center != new Point(0, 0) ) {
//                        Pen penEllipse = _ep.pixelPower < 80 ? Pens.White : Pens.Black;
                            _penEllipse = _penEllipse == _thickRedPen ? _thickYellowPen : _thickRedPen;
                            Rectangle rect = new Rectangle(
                                new Point(_ep.center.X - _ep.a, _ep.center.Y - _ep.b),
                                new Size(2 * _ep.a, 2 * _ep.b));
                            g.TranslateTransform(_ep.center.X, _ep.center.Y);
                            g.RotateTransform((float)_ep.theta);
                            g.TranslateTransform(-1 *_ep.center.X, -1 * _ep.center.Y);
                            g.DrawEllipse(_penEllipse, rect);
                            g.ResetTransform();
                            if ( _settings.ProvideSocketScreenshots ) {
                                sg.TranslateTransform(_ep.center.X, _ep.center.Y);
                                sg.RotateTransform((float)_ep.theta);
                                sg.TranslateTransform(-1 *_ep.center.X, -1 * _ep.center.Y);
                                sg.DrawEllipse(Pens.Black, rect);
                                sg.ResetTransform();
                            }

                        }

                        // show circle (part of the crosshair) around "beam enclosing ellipse"
                        if ( (delta > 0) && (_ep.center != new Point(0, 0)) ) {
                            float x = _ep.center.X - delta;
                            float y = _ep.center.Y - delta;
                            float w = 2 * delta;
                            float h = 2 * delta;
                            g.DrawEllipse(Pens.Gray, x, y, w, h);
                            if ( _settings.ProvideSocketScreenshots ) {
                                sg.DrawEllipse(Pens.Gray, x, y, w, h);
                            }
                        }

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

                        // allows Bitmap transfer via socket to another listening app                
                        if ( _settings.ProvideSocketScreenshots ) {
                            sg.Dispose();
                        }
                    }

                    // done here
                    _paintBusy = false;

                    // repaint scales left & bottom
                    this.tableLayoutPanelGraphs.Invalidate(false);
                    this.tableLayoutPanelGraphs.Update();

                } // end of 'using ( Graphics g = Graphics.FromImage(bmp) ) {'

            } catch ( Exception fe ) {
                this.Text = "Paint(5): " + fe.Message;
                _paintBusy = false;
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

        // set red cross position coordinates relative to pictureBox image == canvas
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
            // show red cross even in a still image
            if ( !_redCrossFollowsBeam && ((_bmp == null) || ((_videoDevice != null) && (!_videoDevice.IsRunning))) ) {
                this.pictureBox.Invalidate();
            }
        }

        // toggle red crosshair on/off
        void toggleCrossHairVisibility() {
            if ( _redCross == new Point(-1, -1) ) {
                // crosshair becomes visible
                _redCross = _redCrossLast;
                this.pictureBox_CmShowCrosshair.Checked = true;
            } else {
                // crosshair becomes invisible
                _redCrossLast = _redCross;
                _redCross = new Point(-1, -1);
                this.pictureBox_CmShowCrosshair.Checked = false;
            }
            headLine();
            if ( !_redCrossFollowsBeam && ((_bmp == null) || ((_videoDevice != null) && (!_videoDevice.IsRunning))) ) {
                this.Invalidate(true);
            }
        }

        // IMessageFilter: intercept messages
        const int WM_KEYDOWN = 0x100;
        public bool PreFilterMessage(ref Message m)     
        {
            // handle red cross with keys
            if ( m.Msg == WM_KEYDOWN ) {
                // <ESC> toggles red cross on/off
                if ( (Keys)m.WParam == Keys.Escape ) {
                    toggleCrossHairVisibility();
                    return true;
                }
            }
            return false;
        }

        // fast & correct (other than AForge):
        // https://web.archive.org/web/20110827032809/http://www.switchonthecode.com/tutorials/csharp-tutorial-convert-a-color-image-to-grayscale
        public static Bitmap MakeGrayscaleGDI(Bitmap original)
        {
            // create a blank bitmap the same size as original
            Bitmap newBitmap = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);

            // get a graphics object from the new image
            Graphics g = Graphics.FromImage(newBitmap);

            //create the grayscale ColorMatrix
            ColorMatrix colorMatrix = new ColorMatrix( new float[][]
            {
               // BT709 simplified
               new float[] {.2f, .2f, .2f, 0, 0},
               new float[] {.7f, .7f, .7f, 0, 0},
               new float[] {.1f, .1f, .1f, 0, 0},
               new float[] {0, 0, 0, 1, 0},
               new float[] {0, 0, 0, 0, 1}
            });

            // create image attributes object
            ImageAttributes attributes = new ImageAttributes();

            // set the color matrix attribute
            attributes.SetColorMatrix(colorMatrix);

            // draw the original image on the new image using the grayscale color matrix
            g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);

            // dispose the Graphics object
            g.Dispose();
            return newBitmap;
        }

        // there are two ulam spiral modes:
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
                int pos = y * bmpSize.Width * 3 + x * 3;
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
        List<Point> getBlobBorderPolygon(Size bmpSize, byte[] bmpArr, Point ptStart, int thresholdBlob)
        {
            List<Point> polygon = new List<Point>();
            WalkDirection wd = WalkDirection.ILLEGAL;
            Point pt = ptStart;

            do {
                // get state of a '4 pixel matrix'
                int state = getPixelState(bmpSize, bmpArr, pt, thresholdBlob);
                if ( (state == 0) || (state == 15) ) {
                    // severe failure, meaning all 4 pixels are set OR none is set
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

        // helper class containing points
        class Trace
        {
            public List<Point> Points { get; set; }
            public Trace()
            {
                Points = new List<Point>();
            }
        }
        // helper class containing points and the comprising area of all collected points
        class Blob
        {
            public List<Point> Points { get; set; }
            public Rectangle Area { get; set; }
            public Blob()
            {
                Points = new List<Point>();
                Area = new Rectangle();
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
        List<Point> getPixelsAlongUlamSpiral(Size bmpSize, byte[] bmpArr, int thresholdBlob)
        {
            int ULAMGRIDSIZE = bmpSize.Height < 400 ? 1 : 3;
            int width = bmpSize.Width * 3;

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

            // only for DEBUG / INFO purposes 
            Trace trace = new Trace();

            // set a limit (length of a ulam edge) depending on the image size
            int lenEdgeLimit = bmpSize.Width / 2 + Math.Abs(imageCenterOffset.X);

            //
            // a 1st step just just searches for a hot pixel (thresholdBlob), which triggers the re start of the ulam spiraling
            // ulam spiral mode control:
            //      initially search a hot pixel --> do 'outer spiral'
            //      if hot pixel was found       --> do 'blob mode' 
            // 
            SpiralMode spiralMode = SpiralMode.SEARCH_HOT;         

            // 'blob spiral' specific
            int  blobSpiralPixelCount = 0;   // pixel count in current spiral
            int  blobSpiralFailCount = 0;    // 'blob spiral' collected number of either 'non hot pixels' or 'blob non consecutive hot pixels' 
            bool blobOneSpiralDone = false;  // flag after a full ulam spiral is executed

            // common ulam spiral control vars
            bool dueX = true;                // affect X or Y
            bool down = false;               // if Y is affected, then down or up
            bool left = false;               // if X is affected, then left or right
            int  lenEdge = 1;                // Ulam sequence: 1,1, 2,2, 3,3, 4,4, 5,5, 6,6, ...     --> one sequence per X and one sequence per Y
            int  lenEdgeNdx = 0;             // Ulam sequence index

            // ulam spiral loop
            do {

                // ensure that a pixel is within the bmp borders
                ulamPt.X = Math.Max(Math.Min(ulamPt.X, bmpSize.Width - 1), 0);
                ulamPt.Y = Math.Max(Math.Min(ulamPt.Y, bmpSize.Height - 1), 0);
                
                // trace shows, how the search was executed
                if ( _settings.DebugSearchTrace ) {
                    trace.Points.Add(new Point(ulamPt.X, ulamPt.Y));
                }
                
                // calculate current pixel position in the image byte array
                int pos = ulamPt.Y * width + ulamPt.X * 3;

                // check current pixel for a 'hot pixel' threshold hit
                byte value = bmpArr[pos];
                if ( value >= thresholdBlob ) {

                    // once a 1st hot pixel is detected, the whole spiral re starts: it's to assume, the found hot pixel belongs to a blob   
                    if ( spiralMode == SpiralMode.SEARCH_HOT ) {
                        spiralMode = SpiralMode.BUILD_BLOB;
                        blob.Points.Clear();
                        dueX = true;
                        down = false;
                        left = false;
                        lenEdge = 1;
                        lenEdgeNdx = 0;
                    }

                    // build an area (Rectangle) containing the hot pixel, needed to check whether a hot pixel is a consecutive hot pixel to the blob
                    Rectangle blobArea = new Rectangle();
                    if ( blob.Points.Count == 0 ) {
                        blobArea = new Rectangle(ulamPt.X - ULAMGRIDSIZE, ulamPt.Y - ULAMGRIDSIZE, 2 * ULAMGRIDSIZE, 2 * ULAMGRIDSIZE);
                    } else {
                        blobArea = blob.Area;
                    }

                    // add hot pixel position & its area to the blob
                    if ( blobArea.Contains(ulamPt) ) {
                        blobSpiralFailCount--;
                        blob.Points.Add(ulamPt);
                        blob.Area = blobArea;
                        // increase blob area, taking the just added hot pixel into account
                        if ( blob.Area.Left + ULAMGRIDSIZE == ulamPt.X ) {
                            blob.Area = new Rectangle(Math.Max(0, blob.Area.Left - ULAMGRIDSIZE), blob.Area.Top, blob.Area.Width + ULAMGRIDSIZE, blob.Area.Height);
                        }
                        if ( blob.Area.Right - ULAMGRIDSIZE == ulamPt.X ) {
                            blob.Area = new Rectangle(blob.Area.Left, blob.Area.Top, blob.Area.Width + ULAMGRIDSIZE, blob.Area.Height);
                        }
                        if ( blob.Area.Top + ULAMGRIDSIZE == ulamPt.Y ) {
                            blob.Area = new Rectangle(blob.Area.Left, Math.Max(0, blob.Area.Top - ULAMGRIDSIZE), blob.Area.Width, blob.Area.Height + ULAMGRIDSIZE);
                        }
                        if ( blob.Area.Bottom - ULAMGRIDSIZE == ulamPt.Y ) {
                            blob.Area = new Rectangle(blob.Area.Left, blob.Area.Top, blob.Area.Right, blob.Area.Height + ULAMGRIDSIZE);
                        }
                    } else {
                        // it's a hot pixel, but it does not belong to the blob (it's a non consecutive hot pixel)
                        if ( spiralMode == SpiralMode.BUILD_BLOB ) {
                            blobSpiralFailCount++;
                        }
                    }

                    //
                    // let's limit the blob build: if the blob contains ca. nn hot pixel positions at the same intensity, it is 'a good blob'
                    //
                    if ( blob.Points.Count > 500 ) {
                        break;
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
                        if ( (spiralMode == SpiralMode.BUILD_BLOB) && (lenEdgeNdx == 0) && (blob.Points.Count > 2) ) {
                            blobOneSpiralDone = true;
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

            } while ( lenEdge < lenEdgeLimit ); // finally give up, if the Ulam length reaches ca. 1/2 of the image width

            // normally one would like to see the blob, but the search trace is a good debugging tool
            if ( _settings.DebugSearchTrace ) {
                return trace.Points;
            } else {
                return blob.Points;
            }
        }

        //
        // analyze the found beam blob
        //
        double getPowerAlongUlamSpiral(
            Size bmpSize,                // current Bitmap size   
            byte[] bmpArr,               // Bitmap raw data array
            int thresholdBlob,           // all above threshold is a beam
            Point center,                // beam center coordinates
            double stopPower,            //
            int maxDiameter,             //
            out byte pixelPower,         // 
            out int diameter,            //
            out List<Point> allPts,      //
            out List<Point> subPolygon)  //
        {

            int ULAMGRIDSIZE = 1;
            int width = bmpSize.Width * 3;
            pixelPower = 0;
            double power = 0;
            diameter = 0;
            allPts = null;
            bool getPts = false;
            if ( stopPower != double.MaxValue ) {
                getPts = true;
                allPts = new List<Point>();
            }

            // set a limit
            int lenEdgeLimit = bmpSize.Width / 2;
            bool fstSpiral = true;
            int blobSpiralFailCount = 0;
            bool fullSpiral = false;
            int spiral = 0;
            Point ulamPt = center;
            bool dueX = true;        // affect X or Y
            bool down = false;       // if Y is affected, then down or up
            bool left = false;       // if X is affected, then left or right
            int lenEdge = 1;         // Ulam sequence: 1,1, 2,2, 3,3, 4,4, 5,5, 6,6, ... 
            int lenEdgeNdx = 0;      // Ulam sequence index
            bool triggerShape = true;
            subPolygon = new List<Point>();
            do {
                // calculate current pixel position in the byte array
                int pos = ulamPt.Y * width + ulamPt.X * 3;
                // check current pixel for a brightness hit
                byte value = 0;
                if ( (pos > 0) && (pos < bmpArr.Length) ) {
                    value = bmpArr[pos];
                }
                if ( value >= thresholdBlob ) {
                    if ( getPts ) {
                        allPts.Add(ulamPt);
                    }
                    blobSpiralFailCount--;
                    power += value;
                    if ( power >= stopPower ) {
                        pixelPower = value;
                        diameter = lenEdge;
                        if ( getPts ) {
                            int stop = Math.Min(4 * diameter, allPts.Count - 1);
                            int ofs = allPts.Count - 1;
                            for ( int i = 0; i < stop; i++ ) {
                                subPolygon.Add(allPts[ofs - i]);
                            }
                        }
                        break;
                    }
                    if ( !triggerShape ) {
                        subPolygon.Add(ulamPt);
                        triggerShape = true;
                    }
                } else {
                    blobSpiralFailCount++;
                    if ( triggerShape ) {
                        subPolygon.Add(ulamPt);
                        triggerShape = false;
                    }
                }
                // move pixel coordinate
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
                if ( fullSpiral && (blobSpiralFailCount >= spiral) ) {
                    pixelPower = value;
                    diameter = lenEdge;
                    break;
                }
                // break if lenEdge (aka blob diameter) exceeds the blob surrounding circle
                if ( lenEdge >= maxDiameter ) {
                    pixelPower = value;
                    diameter = lenEdge;
                    break;
                }
                // next spiral: reset spiral counter + fail counter + full spiral flag
                if ( fullSpiral ) {
                    blobSpiralFailCount = 0;
                    fullSpiral = false;
                    spiral = 0;
                }
                // each move is a pixel along the spiral
                spiral++;

            } while ( lenEdge < lenEdgeLimit );      // finally give up, if the Ulam length reaches ca. 1/2 of the image width

            // sanity check
            if ( lenEdge >= lenEdgeLimit ) {
                diameter = -1;
            }

            return power;
        }

        //
        // detect a blob inside an image
        //
        // params:  bitmap size, byte[] - all bitmap pixels as 3 bytes per pixel, beam power threshold, min. beam diameter
        // out:     List<Point> - polygon enclosing blob, PointF - centroid of the polygon blob, EllipseParam - best fit ellipse to polygon in canonical form
        // return:  error code
        int getBeamBlob(Size bmpSize, byte[] bmpArr, int beamPowerThreshold, int minBeamDiameter, out List<Point> polygon, out PointF centroid, out EllipseParam ep) {

            polygon = null;
            centroid = new PointF();
            ep = new EllipseParam();
            int errorCode = 0;
            int width = bmpSize.Width * 3;

            //
            // search for a reasonable blob (pixel positions having the same or higher intensity) beginning from a given center of the image following an Ulam spiral
            //
            List<Point> centerBlob = getPixelsAlongUlamSpiral(bmpSize, bmpArr, beamPowerThreshold);
            // sort pixels by Y and after that by X
            if ( !_settings.DebugSearchTrace || !_settings.DebugUlamSpiral ) {
                centerBlob = centerBlob.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
            }

            //
            // Blob Border Walker - supposed to find a polygon matching the blob border
            //                    - kown issue: very rough borders occasionally provide a closed polygon NOT describing the real blob
            //                                  --> for the affected image, the border polygon/ellipse is MUCH SMALLER than expected 
            //                                  --> therefore we allow up to 20 repititions if minBlobBorderPixelCount is not met, then we give up 
            //                                  --> minBlobBorderPixelCount provides a threshold for a minimal required pixel count inside a blob   
            //
            if ( centerBlob.Count == 0 ) {
                return -2;
            }
            int tryCount = 0;
            Point ulamPt = centerBlob[centerBlob.Count / 2];
            polygon = new List<Point>();
            int state;
            // border walker loop
            do {
                // 1st step: we start with a state == 15 pixel; now find a blob border pixel with 'state != 15'; means not all 4 pixels are set --> makes sure we hit the blob border
                state = 0;
                do {
                    state = getPixelState(bmpSize, bmpArr, ulamPt, beamPowerThreshold);
                    if ( state == 15 ) {
                        ulamPt.Y--;
                    }
                } while ( state == 15 );
                // 2nd step: execute the 'blob border walker' algorithm
                polygon = getBlobBorderPolygon(bmpSize, bmpArr, ulamPt, beamPowerThreshold);
                //
                // something is wrong, if the real enclosing polygon has less points than the calculated circumference 'Math.PI * minBlobDiameter'
                //
                if ( polygon.Count < Math.PI * minBeamDiameter ) {
                    // we try it again for a total of 20 times 
                    tryCount++;
                    if ( tryCount < 10 ) {
                        // slightliy outside the center +
                        if ( centerBlob.Count / 2 + tryCount < centerBlob.Count ) {
                            ulamPt = centerBlob[centerBlob.Count / 2 + tryCount];
                        } else {
                            break;
                        }
                    } else {
                        // slightliy outside the center -
                        if ( centerBlob.Count / 2 - tryCount > 0 ) {
                            ulamPt = centerBlob[centerBlob.Count / 2 - tryCount];
                        } else {
                            break;
                        }
                    }
                } else {
                    break;
                }
            } while ( tryCount < 20 );
            if ( tryCount >= 20 ) {
                ;
            }
            errorCode += tryCount;

            //
            // get blob's centroid (aka center of gravity)
            //
            centroid = getCentroid(polygon);

            //
            // compute power of the blob and its diameter
            //
            // initially set to max int; it only gets smaller if a comprising circle (instead of Ulam) is requested 
            int maxDiameter = int.MaxValue;

            // get full power along a Ulam spiral; use beamPowerThreshold as minimal pixel power limit; maxDiameter as geometrical limit; no total power limit;
            Point centerPt = new Point((int)centroid.X, (int)centroid.Y);
            List<Point> subBlob = null;
            List<Point> subPolygon = null;
            int allDiam = 0;
            byte pixelPower = 0;
            double powerAll = getPowerAlongUlamSpiral(_bmpSize, _bmpArr, beamPowerThreshold, centerPt, double.MaxValue, maxDiameter, out pixelPower, out allDiam, out subBlob, out subPolygon);
            
            // calculate the beam power according to a selected algorithm --> powerSub
            double powerSub = powerAll;
            switch ( _settings.BeamDiameterType ) {
                case AppSettings.BeamDiameter.FWHM: powerSub = powerAll * 0.5; break;
                case AppSettings.BeamDiameter.ReverseEulerSquare: powerSub = powerAll * 0.865f; ; break;
                case AppSettings.BeamDiameter.D86: powerSub = powerAll * 0.86f; break;
            }

            // get power along an Ulam spiral other than above:
            // now use subPower as a total power limit AND return last Ulam length as the circle diameter at reduced total power AND return subBlob points
            int finDiam = 0;
            double powerFin = getPowerAlongUlamSpiral(_bmpSize, _bmpArr, beamPowerThreshold, centerPt, powerSub, maxDiameter, out pixelPower, out finDiam, out subBlob, out subPolygon);
            _ep.power = (ulong)powerFin;  // ellipse has a total power value
            _ep.pixelPower = pixelPower;  // gray value at ellipse diameter shall be used to determine the ellipse color 

            // take take the subPolygon and fit an ellipse to it
            FitEllipse.PointCollection points = new FitEllipse.PointCollection();
            foreach ( Point pt in subPolygon ) {
                points.Add(new FitEllipse.Point() { X = pt.X, Y = pt.Y });
            }
            try {
                FitEllipse.EllipseFit fit = new FitEllipse.EllipseFit();
                FitEllipse.Matrix ellipse = null;
                ellipse = fit.Fit(points);
                double A = ellipse.data[0, 0];
                double B = ellipse.data[1, 0];
                double C = ellipse.data[2, 0];
                double D = ellipse.data[3, 0];
                double E = ellipse.data[4, 0];
                double F = ellipse.data[5, 0];
                int cX = (int)ellipse.data[6, 0];
                int cY = (int)ellipse.data[7, 0];
                // https://en.wikipedia.org/wiki/Ellipse#Canonical_form
                // http://mathworld.wolfram.com/Ellipse.html - introduces corrected theta calculation 
                double term1 = 2 * (A * E * E + C * D * D - B * D * E + (B * B - 4 * A * C) * F);
                double term2 = Math.Sqrt((A - C) * (A - C) + B * B);
                double term3 = B * B - 4 * A * C;
                if ( term3 == 0 ) {
                    term3 = 1;
                }
                double a = -1 * Math.Sqrt(term1 * (A + C + term2)) / term3;
                double b = -1 * Math.Sqrt(term1 * (A + C - term2)) / term3;
                if ( B == 0 ) {
                    B = 1;
                }
                double tangent = (C - A - Math.Sqrt((A - C) * (A - C) + B * B)) / B;
                double theta = 0;
                if ( B == 0.0f ) {
                    theta = (A < C) ? 0 : 90;
                } else {
                    if ( A == C ) {
                        theta = 0;
                    } else {
                        if ( A > C ) {
                            theta = (Math.Atan(2 * B / (A - C)) / 2) * (180.0 / Math.PI);
                            double tmp = a;
                            a = b;
                            b = tmp;
                        } else {
                            theta = (Math.PI / 2 + Math.Atan(2 * B / (A - C)) / 2) * (180.0 / Math.PI);
                            theta += 90;
                        }
                    }
                }
                _ep.center = new Point(cX, cY);
                _ep.a = (int)a;
                _ep.b = (int)b;
                _ep.theta = Math.Round(theta);
            } catch ( Exception fe ) {
                ;
            }

// debug show sub Ulam spiral's recently built polygon
//polygon = subPolygon;

            // show result of ULAM spiral instead of blob border polygon
            if ( _settings.DebugUlamSpiral ) {
                polygon = centerBlob;
            }

            return errorCode;
        }

        // helper class 
        class EllipseParam 
        {
            public byte pixelPower;
            public ulong power;
            public Point center;
            public int a;
            public int b;
            public double theta;
        }

        // return center of gravity of a polygon
        public static PointF getCentroid(List<Point> poly)
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
                return PointF.Empty;  // Avoid division by zero
            }

            accumulatedArea *= 3f;
            return new PointF(centerX / accumulatedArea, centerY / accumulatedArea);
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

            // take care about the red cross
            if ( (_redCross == new Point(-1, -1)) || (_redCross.X >= this.pictureBox.ClientSize.Width) || (_redCross.Y >= this.pictureBox.ClientSize.Height) ) {
                _redCross = new Point(this.pictureBox.ClientSize.Width / 2, this.pictureBox.ClientSize.Height / 2);
            }
        }

        // update title bar info
        void headLine()
        {
            try {
                snapshotButton.Enabled = (_bmp != null);
                string spot = _redCrossFollowsBeam ? "red cross follows beam" : "FIX cross";
                string file = this.timerStillImage.Enabled ? " - " + System.IO.Path.GetFileName((string)this.timerStillImage.Tag) : "";
                this.Text = String.Format("Beam Profile  -  screen: {0}x{1}  -  @{2:0.#}fps - {3} {4}", this.pictureBox.Width, this.pictureBox.Height, _fps, spot, file);
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
            // shallow clone is sufficient to avoid exceptions
            _bmp = (Bitmap)bmp.Clone();
            _bmpSize = new Size(bmp.Width, bmp.Height);
            // force monochrome image 
            if ( _settings.ForceGray ) {
                _bmp = MakeGrayscaleGDI(_bmp);
            }
            try {
                // generate pseudo colors if selected
                _bmp = (Bitmap)convertGrayToPseudoColors(_bmp, _settings.PseudoColors, _filter, out _bmpImageType, out _bmpArr);
                // show image in picturebox
                this.pictureBox.Image = _bmp;
            } catch { ;}
        }

        // picture box show the pseudo color
        private void pictureBoxPseudo_Paint(object sender, PaintEventArgs e)
        {
            // the current gray level is shown as a marker inside the pseudo color bar
            float corr = (float)(this.pictureBoxPseudo.Width / 256.0f);
            int xPosLowThreshold = (int)Math.Round((float)_settings.BeamPowerThreshold * corr);
            e.Graphics.DrawLine(Pens.White, new Point(xPosLowThreshold, 0), new Point(xPosLowThreshold, 24));
            int xPosHotPixel = (int)Math.Round((float)_centerGrayByte * corr);
            e.Graphics.DrawLine(Pens.Black, new Point(xPosHotPixel, 0), new Point(xPosHotPixel, 24));
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
            _paintBusy = true;
            // transfer current app settings to _settings class
            updateSettingsFromApp();
            // start settings dialog
            Settings dlg = new Settings(_settings);
            // memorize settings
            AppSettings oldSettings = new AppSettings();
            AppSettings.CopyAllTo(_settings, out oldSettings);
            _paintBusy = false;
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
            // if camera model changes, the following settings are likely wrong - therefore better reset them
            _settings.BeamSearchOrigin = AppSettings.BeamSearchStates.CENTER;
            _bmpBkgnd = new byte[0];
            _bmpBkgnd = null;
        }

        // change camera resolution
        private void videoResolutionsCombo_Click(object sender, EventArgs e)
        {
            // if camera resolution changes, the following settings are likely wrong - therefore better reset them
            _settings.BeamSearchOrigin = AppSettings.BeamSearchStates.CENTER;
            _bmpBkgnd = new byte[0];
            _bmpBkgnd = null;
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
            if ( _settings.SubtractBackgroundImage ) {
                if ( MessageBox.Show(
                                "Turn beam providing source off.\n\n" +
                                "Make sure Settings --> 'Subtract Backround Image' = False.\nOtherwise results become unpredictable.\n\n" +
                                "Continue anyway?", 
                                "Capture a Background Image", 
                                MessageBoxButtons.OKCancel) != DialogResult.OK ) {
                    return;
                }
            }
            if ( _bmpArr != null && _bmpArr.Length > 0 ) {
                if ( _videoDevice != null  && _videoDevice.IsRunning ) {
                    // disconnect means stop video device
                    if ( _videoDevice.IsRunning ) {
                        _videoDevice.NewFrame -= new AForge.Video.NewFrameEventHandler(videoDevice_NewFrame);
                        _videoDevice.SignalToStop();
                        _fps = 0;
                    }
                    // some controls
                    EnableConnectionControls(true);
                }
                _bmpBkgnd = new byte[_bmpArr.Length];
                Array.Copy(_bmpArr, _bmpBkgnd, _bmpArr.Length);
                System.IO.File.WriteAllBytes("BackGroundImage.bin", _bmpBkgnd);
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
            FWHM = 0,
            ReverseEulerSquare = 1,
            D86 = 2
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
        [Description("Minimum pixel power level indicating a beam")]
        public int BeamPowerThreshold { get; set; }
        [Description("Minimum required beam diameter in pixels, lower limit is 4")]
        public int BeamMinimumDiameter { get; set; }
        public BeamDiameter BeamDiameterType { get; set; }
        [Description("Beam diameter calculation type - determines power reduction")]
        public bool ForceGray { get; set; }
        public bool PseudoColors { get; set; }
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
        [Description("INFO: show 2nd Ulam spiral result instead of blob border")]
        public bool DebugUlamSpiral { get; set; }
        [Description("INFO: show both Ulam search traces; needs DebugUlamSpiral = True")]
        public bool DebugSearchTrace { get; set; }

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
            }
            // brightness threshold
            if ( int.TryParse(ini.IniReadValue(iniSection, "BeamPowerThreshold", "50"), out tmp) ) {
                BeamPowerThreshold = tmp;
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
            // blob: show search traces
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DebugSearchTrace", "false"), out check) ) {
                DebugSearchTrace = check;
            }
            // blob: Ulam vs. Polygon
            if ( bool.TryParse(ini.IniReadValue(iniSection, "DebugUlamSpiral", "false"), out check) ) {
                DebugUlamSpiral = check;
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
            // brightness threshold
            ini.IniWriteValue(iniSection, "BeamPowerThreshold", BeamPowerThreshold.ToString());
            // pseudo color table 
            ini.IniWriteValue(iniSection, "PseudoColorTable", PseudoColorTable.ToString());
            // beam diameter type 
            ini.IniWriteValue(iniSection, "BeamDiameterType", BeamDiameterType.ToString());
            // SubtractBackgroundImage
            ini.IniWriteValue(iniSection, "SubtractBackgroundImage", SubtractBackgroundImage.ToString());
            // minimal required blob diameter
            ini.IniWriteValue(iniSection, "BeamMinimumDiameter", BeamMinimumDiameter.ToString());
            // blob: Ulam vs. Polygon
            ini.IniWriteValue(iniSection, "DebugUlamSpiral", DebugUlamSpiral.ToString());
            // blob: show search traces
            ini.IniWriteValue(iniSection, "DebugSearchTrace", DebugSearchTrace.ToString());
            // center image offset
            ini.IniWriteValue(iniSection, "BeamSearchOffset", BeamSearchOffset.ToString());
            // socket screenshots
            ini.IniWriteValue(iniSection, "ProvideSocketScreenshots", ProvideSocketScreenshots.ToString());
        }

    }

}

