using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using WindowsPreview.Kinect;
using System.ComponentModel;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Windows.UI.Xaml.Shapes;

namespace Kinect2Sample
{
    public enum DisplayFrameType
    {
        Infrared,
        Color,
        Depth,
        BodyJoints
    }

    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private const DisplayFrameType DEFAULT_DISPLAYFRAMETYPE = DisplayFrameType.Infrared;
        /// <summary> 
        /// This is the setup for using the infrared views, cannot be deleted without causing errors in
        /// the rest of the project
        /// </summary>
        private const float InfraredSourceValueMaximum = (float)ushort.MaxValue; ///<summary> 
                                                                                 ///The maximum value infrared can
                                                                                 ///return</summary>
        private const float InfraredOutputValueMinimum = 0.01f;

        /// <summary>
        /// maximum value that  infrared can return
        /// </summary>
        private const float InfraredOutputValueMaximum = 1.0f;

        /// <summary>
        /// average infrared reading of what is captured
        /// </summary>
        private const float InfraredSceneValueAverage = 0.08f;

        /// <summary>
        /// number of standard deviations for the readings of what is captured
        /// </summary>
        private const float InfraredSceneStandardDeviations = 3.0f;

        // Size of the RGB pixel in the bitmap
        private const int BytesPerPixel = 4;
        // initialization of variables
        private KinectSensor kinectSensor = null;
        private string statusText = null;
        private WriteableBitmap bitmap = null;
        private FrameDescription currentFrameDescription;
        private DisplayFrameType currentDisplayFrameType;
        private MultiSourceFrameReader multiSourceFrameReader = null;
        private CoordinateMapper coordinateMapper = null;
        private BodiesManager bodiesManager = null;

        //Infrared Frame initialization
        private ushort[] infraredFrameData = null;
        private byte[] infraredPixels = null;

        //Depth Frame initialization
        private ushort[] depthFrameData = null;
        private byte[] depthPixels = null;
        private DepthSpacePoint[] colorMappedToDepthPoints = null;

        //Initialization to draw body index points
        private Canvas drawingCanvas;

        // handler for changing views
        public event PropertyChangedEventHandler PropertyChanged;
        public string StatusText
        {
            get { return this.statusText; }
            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        //what frame you are in
        public FrameDescription CurrentFrameDescription
        {
            get { return this.currentFrameDescription; }
            set
            {
                if (this.currentFrameDescription != value)
                {
                    this.currentFrameDescription = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("CurrentFrameDescription"));
                    }
                }
            }
        }

        public MainPage()
        {
            // connecting sensor
            this.kinectSensor = KinectSensor.GetDefault();

            // sets the default screen
            SetupCurrentDisplay(DEFAULT_DISPLAYFRAMETYPE);

            // initializing mapper for coordinates
            this.coordinateMapper = this.kinectSensor.CoordinateMapper;

            // sets current frame reader
            this.multiSourceFrameReader = this.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Infrared | FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.BodyIndex | FrameSourceTypes.Body);

            this.multiSourceFrameReader.MultiSourceFrameArrived += this.Reader_MultiSourceFrameArrived;

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // open the sensor
            this.kinectSensor.Open();

            this.InitializeComponent();
        }

        //This function takes the frame type, and creates the view for the 
        // new window to be rendered in the view selected
        private void SetupCurrentDisplay(DisplayFrameType newDisplayFrameType)
        {
            currentDisplayFrameType = newDisplayFrameType;
            // Frames used by more than one type are declared outside the switch
            FrameDescription colorFrameDescription = null;
            // reset the display methods
            if (this.BodyJointsGrid != null)
            {
                this.BodyJointsGrid.Visibility = Visibility.Collapsed;
            }
            if (this.FrameDisplayImage != null)
            {
                this.FrameDisplayImage.Source = null;
            }
            switch (currentDisplayFrameType) // setting data between different views
            {
                    //The case for infrared, not used in experiment
                case DisplayFrameType.Infrared:
                    FrameDescription infraredFrameDescription = this.kinectSensor.InfraredFrameSource.FrameDescription;
                    this.CurrentFrameDescription = infraredFrameDescription;
                    // allocate space to put the pixels being received and converted
                    this.infraredFrameData = new ushort[infraredFrameDescription.Width * infraredFrameDescription.Height];
                    this.infraredPixels = new byte[infraredFrameDescription.Width * infraredFrameDescription.Height * BytesPerPixel];
                    this.bitmap = new WriteableBitmap(infraredFrameDescription.Width, infraredFrameDescription.Height);
                    break;

                    // case for Color playback, control for experiment
                case DisplayFrameType.Color:
                    colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
                    this.CurrentFrameDescription = colorFrameDescription;
                    // create the bitmap to display
                    this.bitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height);
                    break;

                    //case for Depth playback, not used for experiment
                case DisplayFrameType.Depth:
                    FrameDescription depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;
                    this.CurrentFrameDescription = depthFrameDescription;
                    // allocate space to put the pixels being received and converted
                    this.depthFrameData = new ushort[depthFrameDescription.Width * depthFrameDescription.Height];
                    this.depthPixels = new byte[depthFrameDescription.Width * depthFrameDescription.Height * BytesPerPixel];
                    this.bitmap = new WriteableBitmap(depthFrameDescription.Width, depthFrameDescription.Height);
                    break;


                    // case for Body Indexing, used to test in experiment
                case DisplayFrameType.BodyJoints:
                    // instantiate a new Canvas
                    this.drawingCanvas = new Canvas();
                    // set the clip rectangle to prevent rendering outside the canvas
                    this.drawingCanvas.Clip = new RectangleGeometry();
                    this.drawingCanvas.Clip.Rect = new Rect(0.0, 0.0, this.BodyJointsGrid.Width, this.BodyJointsGrid.Height);
                    this.drawingCanvas.Width = this.BodyJointsGrid.Width;
                    this.drawingCanvas.Height = this.BodyJointsGrid.Height;
                    // reset the body joints grid
                    this.BodyJointsGrid.Visibility = Visibility.Visible;
                    this.BodyJointsGrid.Children.Clear();
                    // add canvas to DisplayGrid
                    this.BodyJointsGrid.Children.Add(this.drawingCanvas);
                    bodiesManager = new BodiesManager(this.coordinateMapper, this.drawingCanvas, this.kinectSensor.BodyFrameSource.BodyCount);
                    break;
                default:
                    break;
            }
        }

        // checks to see if sensor is avalable
        private void Sensor_IsAvailableChanged(KinectSensor sender, IsAvailableChangedEventArgs args)
        {
            this.StatusText = this.kinectSensor.IsAvailable ? "Running" : "Not Available";
        }
        //used for multiple video sources
        private void Reader_MultiSourceFrameArrived(MultiSourceFrameReader sender, MultiSourceFrameArrivedEventArgs e)
        {
            
            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();

            // If the Frame has expired by the time we process this event, return.
            if (multiSourceFrame == null)
            {
                return;
            }
            //resets all frames
            DepthFrame depthFrame = null;
            ColorFrame colorFrame = null;
            InfraredFrame infraredFrame = null;
            BodyFrame bodyFrame = null;
            BodyIndexFrame bodyIndexFrame = null;
            IBuffer depthFrameData = null;
            IBuffer bodyIndexFrameData = null;
            // Com interface for unsafe byte manipulation
            IBufferByteAccess bodyIndexByteAccess = null;

            switch (currentDisplayFrameType)
            {
                case DisplayFrameType.Infrared:
                    using (infraredFrame = multiSourceFrame.InfraredFrameReference.AcquireFrame())
                    {
                        ShowInfraredFrame(infraredFrame);
                    }
                    break;
                case DisplayFrameType.Color:
                    using (colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame())
                    {
                        ShowColorFrame(colorFrame);
                    }
                    break;
                case DisplayFrameType.Depth:
                    using (depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame())
                    {
                        ShowDepthFrame(depthFrame);
                    }
                    break;


                case DisplayFrameType.BodyJoints:
                    using (bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame())
                    {
                        ShowBodyJoints(bodyFrame);
                    }
                    break;
                default:
                    break;
            }
        }


        // Pieces joints together takes frame data that has been collected
        private void ShowBodyJoints(BodyFrame bodyFrame)
        {
            Body[] bodies = new Body[this.kinectSensor.BodyFrameSource.BodyCount];
            bool dataReceived = false;
            if (bodyFrame != null)
            {
                bodyFrame.GetAndRefreshBodyData(bodies);
                dataReceived = true;
            }

            if (dataReceived)
            {
                this.bodiesManager.UpdateBodiesAndEdges(bodies);
            }
        }

        // displays the joints and their connections on the window
        unsafe private void ShowMappedBodyFrame(int depthWidth, int depthHeight, IBuffer bodyIndexFrameData, IBufferByteAccess bodyIndexByteAccess)
        {
            bodyIndexByteAccess = (IBufferByteAccess)bodyIndexFrameData;
            byte* bodyIndexBytes = null;
            bodyIndexByteAccess.Buffer(out bodyIndexBytes);

            fixed (DepthSpacePoint* colorMappedToDepthPointsPointer = this.colorMappedToDepthPoints)
            {
                IBufferByteAccess bitmapBackBufferByteAccess = (IBufferByteAccess)this.bitmap.PixelBuffer;

                byte* bitmapBackBufferBytes = null;
                bitmapBackBufferByteAccess.Buffer(out bitmapBackBufferBytes);

                // Treat the color data as 4-byte pixels
                uint* bitmapPixelsPointer = (uint*)bitmapBackBufferBytes;

                // Loop over each row and column of the color image
                // Zero out any pixels that don't correspond to a body index
                int colorMappedLength = this.colorMappedToDepthPoints.Length;
                for (int colorIndex = 0; colorIndex < colorMappedLength; ++colorIndex)
                {
                    float colorMappedToDepthX = colorMappedToDepthPointsPointer[colorIndex].X;
                    float colorMappedToDepthY = colorMappedToDepthPointsPointer[colorIndex].Y;

                    // The sentinel value is -inf, -inf, meaning that no depth pixel corresponds to this color pixel.
                    if (!float.IsNegativeInfinity(colorMappedToDepthX) &&
                        !float.IsNegativeInfinity(colorMappedToDepthY))
                    {
                        // Make sure the depth pixel maps to a valid point in color space
                        int depthX = (int)(colorMappedToDepthX + 0.5f);
                        int depthY = (int)(colorMappedToDepthY + 0.5f);

                        // If the point is not valid, there is no body index there.
                        if ((depthX >= 0) && (depthX < depthWidth) && (depthY >= 0) && (depthY < depthHeight))
                        {
                            int depthIndex = (depthY * depthWidth) + depthX;

                            // If we are tracking a body for the current pixel, do not zero out the pixel
                            if (bodyIndexBytes[depthIndex] != 0xff)
                            {
                                // this bodyIndexByte is good and is a body, loop again.
                                continue;
                            }
                        }
                    }
                    // this pixel does not correspond to a body so make it black and transparent
                    bitmapPixelsPointer[colorIndex] = 0;
                }
            }

            this.bitmap.Invalidate();
            FrameDisplayImage.Source = this.bitmap;

        }

        // processed data for the depth view
        private void ShowDepthFrame(DepthFrame depthFrame)
        {
            bool depthFrameProcessed = false;
            ushort minDepth = 0;
            ushort maxDepth = 0;

            if (depthFrame != null)
            {
                FrameDescription depthFrameDescription = depthFrame.FrameDescription;

                // verify data and write the new infrared frame data to the display bitmap
                if (((depthFrameDescription.Width * depthFrameDescription.Height)
                    == this.infraredFrameData.Length) &&
                    (depthFrameDescription.Width == this.bitmap.PixelWidth) &&
                    (depthFrameDescription.Height == this.bitmap.PixelHeight))
                {
                    // Copy the pixel data from the image to a temporary array
                    depthFrame.CopyFrameDataToArray(this.depthFrameData);

                    minDepth = depthFrame.DepthMinReliableDistance;
                    maxDepth = depthFrame.DepthMaxReliableDistance;
                    //maxDepth = 8000;

                    depthFrameProcessed = true;
                }
            }

            // we got a frame, convert and render
            if (depthFrameProcessed)
            {
                ConvertDepthDataToPixels(minDepth, maxDepth);
                RenderPixelArray(this.depthPixels);
            }
        }
        // converts data from above function into pixels to be shown
        private void ConvertDepthDataToPixels(ushort minDepth, ushort maxDepth)
        {
            int colorPixelIndex = 0;
            // Shape the depth to the range of a byte
            int mapDepthToByte = maxDepth / 256;

            for (int i = 0; i < this.depthFrameData.Length; ++i)
            {
                // Get the depth for this pixel
                ushort depth = this.depthFrameData[i];

                // To convert to a byte, we're mapping the depth value to the byte range.
                // Values outside the reliable depth range are mapped to 0 (black).
                byte intensity = (byte)(depth >= minDepth &&
                    depth <= maxDepth ? (depth / mapDepthToByte) : 0);

                this.depthPixels[colorPixelIndex++] = intensity; //Blue
                this.depthPixels[colorPixelIndex++] = intensity; //Green
                this.depthPixels[colorPixelIndex++] = intensity; //Red
                this.depthPixels[colorPixelIndex++] = 255; //Alpha
            }
        }

        //sets data for the color view
        private void ShowColorFrame(ColorFrame colorFrame)
        {
            bool colorFrameProcessed = false;

            if (colorFrame != null)
            {
                FrameDescription colorFrameDescription = colorFrame.FrameDescription;

                // verify data and write the new color frame data to the Writeable bitmap
                if ((colorFrameDescription.Width == this.bitmap.PixelWidth) && (colorFrameDescription.Height == this.bitmap.PixelHeight))
                {
                    if (colorFrame.RawColorImageFormat == ColorImageFormat.Bgra)
                    {
                        colorFrame.CopyRawFrameDataToBuffer(this.bitmap.PixelBuffer);
                    }
                    else
                    {
                        colorFrame.CopyConvertedFrameDataToBuffer(this.bitmap.PixelBuffer, ColorImageFormat.Bgra);
                    }

                    colorFrameProcessed = true;
                }
            }

            if (colorFrameProcessed)
            {
                this.bitmap.Invalidate();
                FrameDisplayImage.Source = this.bitmap;
            }
        }

        // Collects data for the Infrared View
        private void ShowInfraredFrame(InfraredFrame infraredFrame)
        {
            bool infraredFrameProcessed = false;

            if (infraredFrame != null)
            {
                FrameDescription infraredFrameDescription = infraredFrame.FrameDescription;

                // verify data and write the new infrared frame data to the display bitmap
                if (((infraredFrameDescription.Width * infraredFrameDescription.Height)
                    == this.infraredFrameData.Length) &&
                    (infraredFrameDescription.Width == this.bitmap.PixelWidth) &&
                    (infraredFrameDescription.Height == this.bitmap.PixelHeight))
                {
                    // Copy the pixel data from the image to a temporary array
                    infraredFrame.CopyFrameDataToArray(this.infraredFrameData);

                    infraredFrameProcessed = true;
                }
            }

            // we got a frame, convert and render
            if (infraredFrameProcessed)
            {
                this.ConvertInfraredDataToPixels();
                this.RenderPixelArray(this.infraredPixels);
            }
        }

        //Converts above data into pixels to be viewed
        private void ConvertInfraredDataToPixels()
        {
            // Convert the infrared to RGB
            int colorPixelIndex = 0;
            for (int i = 0; i < this.infraredFrameData.Length; ++i)
            {
                // normalize the incoming infrared data (ushort) to a float ranging from 
                // [InfraredOutputValueMinimum, InfraredOutputValueMaximum] by
                // 1. dividing the incoming value by the source maximum value
                float intensityRatio = (float)this.infraredFrameData[i] / InfraredSourceValueMaximum;

                // 2. dividing by the (average scene value * standard deviations)
                intensityRatio /= InfraredSceneValueAverage * InfraredSceneStandardDeviations;

                // 3. limiting the value to InfraredOutputValueMaximum
                intensityRatio = Math.Min(InfraredOutputValueMaximum, intensityRatio);

                // 4. limiting the lower value InfraredOutputValueMinimum
                intensityRatio = Math.Max(InfraredOutputValueMinimum, intensityRatio);

                // 5. converting the normalized value to a byte and using the result
                // as the RGB components required by the image
                byte intensity = (byte)(intensityRatio * 255.0f);
                this.infraredPixels[colorPixelIndex++] = intensity; //Blue
                this.infraredPixels[colorPixelIndex++] = intensity; //Green
                this.infraredPixels[colorPixelIndex++] = intensity; //Red
                this.infraredPixels[colorPixelIndex++] = 255;       //Alpha
            }
        }
        //renders an array of pixels to the window
        private void RenderPixelArray(byte[] pixels)
        {
            pixels.CopyTo(this.bitmap.PixelBuffer);
            this.bitmap.Invalidate();
            this.FrameDisplayImage.Source = this.bitmap;
        }


        // Event handlers for the different views
        private void InfraredButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Infrared);
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Color);
        }

        private void DepthButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Depth);
        }

        private void BodyJointsButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.BodyJoints);
        }

        //Gui information
        [Guid("905a0fef-bc53-11df-8c49-001e4fc686da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IBufferByteAccess
        {
            unsafe void Buffer(out byte* pByte);
        }



    }
}
