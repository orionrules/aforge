// AForge Direct Show Library
// AForge.NET framework
//
// Copyright � Andrew Kirillov, 2007-2008
// andrew.kirillov@gmail.com
//

namespace AForge.Video.DirectShow
{
    using System;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Threading;
    using System.Runtime.InteropServices;

    using AForge.Video;
    using AForge.Video.DirectShow.Internals;

    /// <summary>
    /// Video source for local video capture device (for example USB webcam).
    /// </summary>
    /// 
    /// <remarks><para>The video source captures video data from local video capture device.
    /// DirectShow is used for capturing.</para>
    /// 
    /// <para>Sample usage:</para>
    /// <code>
    /// // enumerate video devices
    /// videoDevices = new FilterInfoCollection( FilterCategory.VideoInputDevice );
    /// // create video source
    /// VideoCaptureDevice videoSource = new VideoCaptureDevice( videoDevices[0].MonikerString );
    /// // set NewFrame event handler
    /// videoSource.NewFrame += new NewFrameEventHandler( video_NewFrame );
    /// // start the video source
    /// videoSource.Start( );
    /// // ...
    /// // signal to stop
    /// videoSource.SignalToStop( );
    /// // ...
    /// 
    /// private void video_NewFrame( object sender, NewFrameEventArgs eventArgs )
    /// {
    ///     // get new frame
    ///     Bitmap bitmap = eventArgs.Frame;
    ///     // process the frame
    /// }
    /// </code>
    /// </remarks>
    /// 
    public class VideoCaptureDevice : IVideoSource
    {
        // moniker string of video capture device
        private string deviceMoniker;
        // user data associated with the video source
        private object userData = null;
        // received frames count
        private int framesReceived;
        // recieved byte count
        private int bytesReceived;
        // specifies desired size of captured frames
        private Size desiredFrameSize = new Size( 0, 0 );
        // specifies desired capture frame rate
        private int desiredFrameRate = 0;

        private Thread thread = null;
        private ManualResetEvent stopEvent = null;

        /// <summary>
        /// New frame event.
        /// </summary>
        /// 
        /// <remarks><para>Notifies clients about new available frame from video source.</para>
        /// 
        /// <para><note>Since video source may have multiple clients, each client is responsible for
        /// making a copy (cloning) of the passed video frame, because the video source disposes its
        /// own original copy after notifying of clients.</note></para>
        /// </remarks>
        /// 
        public event NewFrameEventHandler NewFrame;

        /// <summary>
        /// Video source error event.
        /// </summary>
        /// 
        /// <remarks>This event is used to notify clients about any type of errors occurred in
        /// video source object, for example internal exceptions.</remarks>
        /// 
        public event VideoSourceErrorEventHandler VideoSourceError;

        /// <summary>
        /// Video source.
        /// </summary>
        /// 
        /// <remarks>Video source is represented by moniker string of video capture device.</remarks>
        /// 
        public virtual string Source
        {
            get { return deviceMoniker; }
            set { deviceMoniker = value; }
        }

        /// <summary>
        /// Received frames count.
        /// </summary>
        /// 
        /// <remarks>Number of frames the video source provided from the moment of the last
        /// access to the property.
        /// </remarks>
        /// 
        public int FramesReceived
        {
            get
            {
                int frames = framesReceived;
                framesReceived = 0;
                return frames;
            }
        }

        /// <summary>
        /// Received bytes count.
        /// </summary>
        /// 
        /// <remarks>Number of bytes the video source provided from the moment of the last
        /// access to the property.
        /// </remarks>
        /// 
        public int BytesReceived
        {
            get
            {
                int bytes = bytesReceived;
                bytesReceived = 0;
                return bytes;
            }
        }

        /// <summary>
        /// User data.
        /// </summary>
        /// 
        /// <remarks>The property allows to associate user data with video source object.</remarks>
        /// 
        public object UserData
        {
            get { return userData; }
            set { userData = value; }
        }

        /// <summary>
        /// State of the video source.
        /// </summary>
        /// 
        /// <remarks>Current state of video source object - running or not.</remarks>
        /// 
        public bool IsRunning
        {
            get
            {
                if ( thread != null )
                {
                    // check thread status
                    if ( thread.Join( 0 ) == false )
                        return true;

                    // the thread is not running, free resources
                    Free( );
                }
                return false;
            }
        }

        /// <summary>
        /// Desired size of captured frames.
        /// </summary>
        /// 
        /// <remarks><para>The property sets desired frame size. However capture
        /// device may not always provide frame with configured size due to the fact
        /// that the size is not supported by it.</para>
        /// 
        /// <para>If the property is set to size (0, 0), then capture device uses its own
        /// default frame size configuration.</para>
        /// 
        /// <para>Default value of the property is set to (0, 0).</para>
        /// 
        /// <para><note>The property should be configured before video source is started
        /// to take effect.</note></para></remarks>
        /// 
        public Size DesiredFrameSize
        {
            get { return desiredFrameSize; }
            set { desiredFrameSize = value; }
        }

        /// <summary>
        /// Desired capture frame rate.
        /// </summary>
        /// 
        /// <remarks><para>The property sets desired capture frame rate. However capture
        /// device may not always provide the exact configured frame rate due to its
        /// capabilities, system performance, etc.</para>
        /// 
        /// <para>If the property is set to 0, then capture device uses its own default
        /// frame rate.</para>
        /// 
        /// <para>Default value of the property is set to 0.</para>
        /// 
        /// <para><note>The property should be configured before video source is started
        /// to take effect.</note></para></remarks>
        /// 
        public int DesiredFrameRate
        {
            get { return desiredFrameRate; }
            set { desiredFrameRate = value; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoCaptureDevice"/> class.
        /// </summary>
        /// 
        public VideoCaptureDevice( ) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoCaptureDevice"/> class.
        /// </summary>
        /// 
        /// <param name="deviceMoniker">Moniker string of video capture device.</param>
        /// 
        public VideoCaptureDevice( string deviceMoniker )
        {
            this.deviceMoniker = deviceMoniker;
        }

        /// <summary>
        /// Start video source.
        /// </summary>
        /// 
        /// <remarks>Starts video source and return execution to caller. Video source
        /// object creates background thread and notifies about new frames with the
        /// help of <see cref="NewFrame"/> event.</remarks>
        /// 
        public void Start( )
        {
            if ( thread == null )
            {
                // check source
                if ( ( deviceMoniker == null ) || ( deviceMoniker == string.Empty ) )
                    throw new ArgumentException( "Video source is not specified" );

                framesReceived = 0;
                bytesReceived = 0;

                // create events
                stopEvent = new ManualResetEvent( false );

                // create and start new thread
                thread = new Thread( new ThreadStart( WorkerThread ) );
                thread.Name = deviceMoniker; // mainly for debugging
                thread.Start( );
            }
        }

        /// <summary>
        /// Signal video source to stop its work.
        /// </summary>
        /// 
        /// <remarks>Signals video source to stop its background thread, stop to
        /// provide new frames and free resources.</remarks>
        /// 
        public void SignalToStop( )
        {
            // stop thread
            if ( thread != null )
            {
                // signal to stop
                stopEvent.Set( );
            }
        }

        /// <summary>
        /// Wait for video source has stopped.
        /// </summary>
        /// 
        /// <remarks>Waits for source stopping after it was signalled to stop using
        /// <see cref="SignalToStop"/> method.</remarks>
        /// 
        public void WaitForStop( )
        {
            if ( thread != null )
            {
                // wait for thread stop
                thread.Join( );

                Free( );
            }
        }

        /// <summary>
        /// Stop video source.
        /// </summary>
        /// 
        /// <remarks>Stops video source aborting its thread.</remarks>
        /// 
        public void Stop( )
        {
            if ( this.IsRunning )
            {
                thread.Abort( );
                WaitForStop( );
            }
        }

        /// <summary>
        /// Free resource.
        /// </summary>
        /// 
        private void Free( )
        {
            thread = null;

            // release events
            stopEvent.Close( );
            stopEvent = null;
        }

        /// <summary>
        /// Display property window for the video capture device providing its configuration
        /// capabilities.
        /// </summary>
        /// 
        /// <param name="parentWindow">Handle of parent window.</param>
        /// 
        public void DisplayPropertyPage( IntPtr parentWindow )
        {
            // check source
            if ( ( deviceMoniker == null ) || ( deviceMoniker == string.Empty ) )
                throw new ArgumentException( "Video source is not specified" );

            // create source device's object
            object sourceObject = FilterInfo.CreateFilter( deviceMoniker );
            if ( sourceObject == null )
                throw new ApplicationException( "Failed creating device object for moniker" );

            // retrieve ISpecifyPropertyPages interface of the device
            ISpecifyPropertyPages pPropPages = (ISpecifyPropertyPages) sourceObject;

            // get property pages from the property bag
            CAUUID caGUID;
            pPropPages.GetPages( out caGUID );

            // get filter info
            FilterInfo filterInfo = new FilterInfo( deviceMoniker );

            //Create and display the OlePropertyFrame
            // object device = sourceObject;
            Win32.OleCreatePropertyFrame( parentWindow, 0, 0, filterInfo.Name, 1, ref sourceObject, caGUID.cElems, caGUID.pElems, 0, 0, IntPtr.Zero );

            // release COM objects
            Marshal.FreeCoTaskMem( caGUID.pElems );
            Marshal.ReleaseComObject( sourceObject );
        }

        /// <summary>
        /// Worker thread.
        /// </summary>
        /// 
        private void WorkerThread( )
        {
            // grabber
            Grabber grabber = new Grabber( this );

            // objects
            object captureGraphObject = null;
            object graphObject = null;
            object sourceObject = null;
            object grabberObject = null;

            // interfaces
            ICaptureGraphBuilder2 captureGraph = null;
            IFilterGraph2   graph = null;
            IBaseFilter     sourceBase = null;
            IBaseFilter     grabberBase = null;
            ISampleGrabber  sampleGrabber = null;
            IMediaControl   mediaControl = null;

            try
            {
                // get type of capture graph builder
                Type type = Type.GetTypeFromCLSID( Clsid.CaptureGraphBuilder2 );
                if ( type == null )
                    throw new ApplicationException( "Failed creating capture graph builder" );

                // create capture graph builder
                captureGraphObject = Activator.CreateInstance( type );
                captureGraph = (ICaptureGraphBuilder2) captureGraphObject;

                // get type of filter graph
                type = Type.GetTypeFromCLSID( Clsid.FilterGraph );
                if ( type == null )
                    throw new ApplicationException( "Failed creating filter graph" );

                // create filter graph
                graphObject = Activator.CreateInstance( type );
                graph = (IFilterGraph2) graphObject;

                // set filter graph to the capture graph builder
                captureGraph.SetFiltergraph( (IGraphBuilder) graph );

                // create source device's object
                sourceObject = FilterInfo.CreateFilter( deviceMoniker );
                if ( sourceObject == null )
                    throw new ApplicationException( "Failed creating device object for moniker" );

                // get base filter interface of source device
                sourceBase = (IBaseFilter) sourceObject;

                // get type of sample grabber
                type = Type.GetTypeFromCLSID( Clsid.SampleGrabber );
                if ( type == null )
                    throw new ApplicationException( "Failed creating sample grabber" );

                // create sample grabber
                grabberObject = Activator.CreateInstance( type );
                sampleGrabber = (ISampleGrabber) grabberObject;
                grabberBase = (IBaseFilter) grabberObject;

                // add source and grabber filters to graph
                graph.AddFilter( sourceBase, "source" );
                graph.AddFilter( grabberBase, "grabber" );

                // set media type
                AMMediaType mediaType = new AMMediaType( );
                mediaType.MajorType = MediaType.Video;
                mediaType.SubType   = MediaSubType.RGB24;

                sampleGrabber.SetMediaType( mediaType );

                // configure sample grabber
                sampleGrabber.SetBufferSamples( false );
                sampleGrabber.SetOneShot( false );
                sampleGrabber.SetCallback( grabber, 1 );

                // check if it is required to change capture settings
                if ( ( desiredFrameRate != 0 ) || ( ( desiredFrameSize.Width != 0 ) && ( desiredFrameSize.Height != 0 ) ) )
                {
                    object streamConfigObject;
                    // get stream configuration object
                    captureGraph.FindInterface( PinCategory.Capture, MediaType.Video, sourceBase, typeof( IAMStreamConfig ).GUID, out streamConfigObject );

                    if ( streamConfigObject != null )
                    {
                        IAMStreamConfig streamConfig = (IAMStreamConfig) streamConfigObject;

                        // get current format
                        streamConfig.GetFormat( out mediaType );
                        VideoInfoHeader infoHeader = (VideoInfoHeader) Marshal.PtrToStructure( mediaType.FormatPtr, typeof( VideoInfoHeader ) );

                        // change frame size if required
                        if ( ( desiredFrameSize.Width != 0 ) && ( desiredFrameSize.Height != 0 ) )
                        {
                            infoHeader.BmiHeader.Width  = desiredFrameSize.Width;
                            infoHeader.BmiHeader.Height = desiredFrameSize.Height;
                        }
                        // change frame rate if required
                        if ( desiredFrameRate != 0 )
                        {
                            infoHeader.AverageTimePerFrame = 10000000 / desiredFrameRate;
                        }

                        // Copy the media structure back
                        Marshal.StructureToPtr( infoHeader, mediaType.FormatPtr, false );

                        // set the new format
                        streamConfig.SetFormat( mediaType );

                        mediaType.Dispose( );
                    }
                }

                // render source device on sample grabber
                captureGraph.RenderStream( PinCategory.Capture, MediaType.Video, sourceBase, null, grabberBase );

                // get media type
                if ( sampleGrabber.GetConnectedMediaType( mediaType ) == 0 )
                {
                    VideoInfoHeader vih = (VideoInfoHeader) Marshal.PtrToStructure( mediaType.FormatPtr, typeof( VideoInfoHeader ) );

                    grabber.Width = vih.BmiHeader.Width;
                    grabber.Height = vih.BmiHeader.Height;
                    mediaType.Dispose( );
                }

                // get media control
                mediaControl = (IMediaControl) graphObject;

                // run
                mediaControl.Run( );

                while ( !stopEvent.WaitOne( 0, true ) )
                {
                    Thread.Sleep( 100 );
                }
                mediaControl.StopWhenReady( );
            }
            catch ( Exception exception )
            {
                // provide information to clients
                if ( VideoSourceError != null )
                {
                    VideoSourceError( this, new VideoSourceErrorEventArgs( exception.Message ) );
                }
            }
            finally
            {
                // release all objects
                captureGraph    = null;
                graph           = null;
                sourceBase      = null;
                grabberBase     = null;
                sampleGrabber   = null;
                mediaControl    = null;

                if ( graphObject != null )
                {
                    Marshal.ReleaseComObject( graphObject );
                    graphObject = null;
                }
                if ( sourceObject != null )
                {
                    Marshal.ReleaseComObject( sourceObject );
                    sourceObject = null;
                }
                if ( grabberObject != null )
                {
                    Marshal.ReleaseComObject( grabberObject );
                    grabberObject = null;
                }
                if ( captureGraphObject != null )
                {
                    Marshal.ReleaseComObject( captureGraphObject );
                    captureGraphObject = null;
                }
            }
        }

        /// <summary>
        /// Notifies client about new frame.
        /// </summary>
        /// 
        /// <param name="image">New frame's image.</param>
        /// 
        protected void OnNewFrame( Bitmap image )
        {
            framesReceived++;
            if ( ( !stopEvent.WaitOne( 0, true ) ) && ( NewFrame != null ) )
                NewFrame( this, new NewFrameEventArgs( image ) );
        }

        //
        // Video grabber
        //
        private class Grabber : ISampleGrabberCB
        {
            private VideoCaptureDevice parent;
            private int width, height;

            // Width property
            public int Width
            {
                get { return width; }
                set { width = value; }
            }
            // Height property
            public int Height
            {
                get { return height; }
                set { height = value; }
            }

            // Constructor
            public Grabber( VideoCaptureDevice parent )
            {
                this.parent = parent;
            }

            // Callback to receive samples
            public int SampleCB( double sampleTime, IntPtr sample )
            {
                return 0;
            }

            // Callback method that receives a pointer to the sample buffer
            public int BufferCB( double sampleTime, IntPtr buffer, int bufferLen )
            {
                // create new image
                System.Drawing.Bitmap image = new Bitmap( width, height, PixelFormat.Format24bppRgb );

                // lock bitmap data
                BitmapData imageData = image.LockBits(
                    new Rectangle( 0, 0, width, height ),
                    ImageLockMode.ReadWrite,
                    PixelFormat.Format24bppRgb );

                // copy image data
                int srcStride = imageData.Stride;
                int dstStride = imageData.Stride;

                int dst = imageData.Scan0.ToInt32( ) + dstStride * ( height - 1 );
                int src = buffer.ToInt32( );

                for ( int y = 0; y < height; y++ )
                {
                    Win32.memcpy( dst, src, srcStride );
                    dst -= dstStride;
                    src += srcStride;
                }

                // unlock bitmap data
                image.UnlockBits( imageData );

                // notify parent
                parent.OnNewFrame( image);

                // release the image
                image.Dispose( );

                return 0;
            }
        }
    }
}