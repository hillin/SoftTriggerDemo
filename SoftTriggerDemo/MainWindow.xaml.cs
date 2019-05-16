using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentech.GenApiDotNET;
using Sentech.StApiDotNET;

namespace SoftTriggerDemo
{
    public partial class MainWindow : Window, IDisposable
    {
        public MainWindow()
        {
            this.InitializeComponent();

            this.Api = new CStApiAutoInit();
            this.SentechSystem = new CStSystem(eStSystemVendor.Sentech);
            this.Device = this.SentechSystem.CreateFirstStDevice();
            this.DataStream = this.Device.CreateStDataStream(0);
            this.ImageBuffer = CStApiDotNet.CreateStImageBuffer();
            this.PixelFormatConverter = new CStPixelFormatConverter
            {
                DestinationPixelFormat = eStPixelFormatNamingConvention.BGR8
            };

            var nodeMap = this.Device.GetRemoteIStPort().GetINodeMap();
            Trace.Assert(nodeMap.SetEnumValue("TriggerSelector", "FrameStart"));
            Trace.Assert(nodeMap.SetEnumValue("TriggerMode", "On"));
            Trace.Assert(nodeMap.SetEnumValue("TriggerSource", "Software"));
            this.TriggerCommand = nodeMap.GetNode<ICommand>("TriggerSoftware");

            this.DataStream.RegisterCallbackMethod(this.OnCallback);
            this.DataStream.StartAcquisition();
            this.Device.AcquisitionStart();
        }

        private CStSystem SentechSystem { get; }
        private CStDataStream DataStream { get; }
        private CStDevice Device { get; }
        private CStApiAutoInit Api { get; }
        private CStImageBuffer ImageBuffer { get; }
        private CStPixelFormatConverter PixelFormatConverter { get; }
        private ICommand TriggerCommand { get; }
        private Stopwatch Stopwatch { get; } = new Stopwatch();

        public void Dispose()
        {
            this.Device.AcquisitionStop();
            this.DataStream.StopAcquisition();

            this.PixelFormatConverter.Dispose();
            this.ImageBuffer.Dispose();
            this.SentechSystem.Dispose();
            this.DataStream.Dispose();
            this.Device.Dispose();
            this.Api.Dispose();
        }

        private void OnCallback(IStCallbackParamBase paramBase, object[] param)
        {
            if (paramBase.CallbackType != eStCallbackType.TL_DataStreamNewBuffer)
            {
                return;
            }

            if (!(paramBase is IStCallbackParamGenTLEventNewBuffer callbackParam))
            {
                return;
            }

            var dataStream = callbackParam.GetIStDataStream();
            using (var streamBuffer = dataStream.RetrieveBuffer(0))
            {
                if (!streamBuffer.GetIStStreamBufferInfo().IsImagePresent)
                {
                    return;
                }

                this.Stopwatch.Stop();
                this.Dispatcher.Invoke(() => LatencyText.Text = $"Latency: {this.Stopwatch.ElapsedMilliseconds:F0} ms");


                var rawImage = streamBuffer.GetIStImage();

                this.ImageBuffer.CreateBuffer(
                    rawImage.ImageWidth,
                    rawImage.ImageHeight,
                    eStPixelFormatNamingConvention.BGR8);

                this.PixelFormatConverter.Convert(rawImage, this.ImageBuffer);

                var bgr8Image = this.ImageBuffer.GetIStImage();

                var image = BitmapSource.Create(
                    (int) rawImage.ImageWidth,
                    (int) rawImage.ImageHeight,
                    96,
                    96,
                    PixelFormats.Bgr24,
                    null,
                    bgr8Image.GetByteArray(),
                    (int) bgr8Image.ImageLinePitch);

                image.Freeze();

                this.Dispatcher.Invoke(() => CapturedImage.Source = image);
            }
        }

        private void TriggerButton_Click(object sender, RoutedEventArgs e)
        {
            this.TriggerCommand.Execute();
            this.Stopwatch.Restart();
        }
    }
}