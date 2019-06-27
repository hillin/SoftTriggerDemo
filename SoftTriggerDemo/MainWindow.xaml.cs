using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentech.GenApiDotNET;
using Sentech.StApiDotNET;

namespace SoftTriggerDemo
{
    public class BitmapSaveInfo
    {

        private string _fileName;
        private BitmapSource _bitmap;

        public string FileName { get => _fileName; set => _fileName = value; }
        public BitmapSource Bitmap { get => _bitmap; set => _bitmap = value; }

        public BitmapSaveInfo(BitmapSource bitmap, string fileName)
        {
            _fileName = fileName;
            _bitmap = bitmap;
        }
    }
    public class AsyncSaveBitmapTask {
        private ConcurrentQueue<BitmapSaveInfo> queue;
        private BitmapSaveInfo bsi = null;
        public AsyncSaveBitmapTask()
        {
            queue = new ConcurrentQueue<BitmapSaveInfo>();
        }
        public void Enqueue(BitmapSaveInfo bsi)
        {
            queue.Enqueue(bsi);
        }
        public void Dequeue()
        {
            if (queue.TryDequeue(out bsi))
            {
                var frame = BitmapFrame.Create(bsi.Bitmap);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(frame);

                using (var stream = File.Create(bsi.FileName))
                {
                    encoder.Save(stream);
                }
            }
        }
        public int ImagesRemain
        {
            get
            {
                return queue.Count;
            }
        }
    }
    public partial class MainWindow : IDisposable
    {
        public MainWindow()
        {
            this.InitializeComponent();
            this.TaskAsyncSaveBmp = new AsyncSaveBitmapTask();

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
            //Trace.Assert(nodeMap.SetEnumValue("TriggerMode", "Off"));
            Trace.Assert(nodeMap.SetEnumValue("TriggerMode", "On"));
            Trace.Assert(nodeMap.SetEnumValue("TriggerSource", "Line0"));
            //this.TriggerCommand = nodeMap.GetNode<ICommand>("TriggerSoftware");

            this.DataStream.RegisterCallbackMethod(this.OnCallback);
            this.DataStream.StartAcquisition();
            this.Device.AcquisitionStart();

            //
        }

        private AsyncSaveBitmapTask TaskAsyncSaveBmp { get; }
        private CStSystem SentechSystem { get; }
        private CStDataStream DataStream { get; }
        private CStDevice Device { get; }
        private CStApiAutoInit Api { get; }
        private CStImageBuffer ImageBuffer { get; }
        private CStPixelFormatConverter PixelFormatConverter { get; }
        private ICommand TriggerCommand { get; }
        private Stopwatch Stopwatch { get; } = new Stopwatch();
        private int ImageCounter { get; set; }

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
                this.Dispatcher.Invoke(() => LatencyText.Text = $"Latency: {this.Stopwatch.ElapsedMilliseconds} ms");

                var rawImage = streamBuffer.GetIStImage();

                this.ImageBuffer.CreateBuffer(
                    rawImage.ImageWidth,
                    rawImage.ImageHeight,
                    eStPixelFormatNamingConvention.BGR8);

                this.PixelFormatConverter.Convert(rawImage, this.ImageBuffer);

                var bgr8Image = this.ImageBuffer.GetIStImage();

                var image = BitmapSource.Create(
                    (int)rawImage.ImageWidth,
                    (int)rawImage.ImageHeight,
                    96,
                    96,
                    PixelFormats.Bgr24,
                    null,
                    bgr8Image.GetByteArray(),
                    (int)bgr8Image.ImageLinePitch);

                image.Freeze();

                var frame = BitmapFrame.Create(image);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(frame);

                if (!Directory.Exists("captures"))
                {
                    Directory.CreateDirectory("captures");
                }

                var fileName = $"captures/{this.ImageCounter:0000}.png";
                ++this.ImageCounter;

                /*
                using (var stream = File.Create(fileName))
                {
                    encoder.Save(stream);
                }*/
                this.TaskAsyncSaveBmp.Enqueue(new BitmapSaveInfo(image, fileName));

                this.Dispatcher.Invoke(() => SaveFileNameText.Text = $"Saved: {fileName}");
                
                this.Dispatcher.Invoke(() => CapturedImage.Source = image);
            }
        }

        private void TriggerButton_Click(object sender, RoutedEventArgs e)
        {
            //this.TriggerCommand.Execute();
            //this.Stopwatch.Restart();
            
            var qImg = this.TaskAsyncSaveBmp.ImagesRemain;
            
            for (var countIndex = 0; countIndex < qImg; countIndex++) {
                this.Dispatcher.Invoke(() => SaveFileNameText.Text = $"Remain: {qImg}");
                this.TaskAsyncSaveBmp.Dequeue();
            }
        }
    }
}