﻿using fad.Backend;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace fad2.UI
{
    public partial class Fad : MetroFramework.Forms.MetroForm
    {
        private List<string> _imageLoop;
        Timer _imageSwitchTimer;
        Random _random = new Random();
        private int _failCount = 0;
        private bool _connected = false;

        public Fad()
        {
            InitializeComponent();
            ImageSwitcher();
            Application.DoEvents();
            StartConnection();
        }

        private void ImageSwitcher()
        {
            // TODO: Allow local Images
            _imageLoop = Directory.GetFiles($"{Application.StartupPath}\\examplepix").ToList();
            _imageSwitchTimer = new Timer();
            _imageSwitchTimer.Interval = 10000;  // TODO: From Settings
            _imageSwitchTimer.Tick += _imageSwitchTimer_Tick;
            _imageSwitchTimer.Start();
            ChangeImage();
        }

        private void _imageSwitchTimer_Tick(object sender, EventArgs e)
        {
            ChangeImage();
        }

        private void ChangeImage()
        {
            int randomImageId= _random.Next(_imageLoop.Count);
            ChangeImage(_imageLoop[randomImageId]);
        }

        private Bitmap ResizedImage(string path)
        {
            int alpha = 150;
            var bitmap = new Bitmap(path);
            double aspectRatio =  bitmap.Width / (double)bitmap.Height;
            int maxWidth = BackPicture.Width;
            int maxHeight = BackPicture.Height;
            int imageWidth = maxWidth;
            int imageHeight = (int)(imageWidth / aspectRatio);
            if (imageHeight<maxHeight)
            {
                imageHeight = maxHeight;
                imageWidth = (int)(imageHeight * aspectRatio);
            }

            var image = new Bitmap(bitmap, new Size(imageWidth, imageHeight));

            using (Graphics g = Graphics.FromImage(image))
            {
                Pen pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), image.Width);
                g.DrawLine(pen, -1, -1, image.Width, image.Height);
                g.Save();
            }

            return image;
        }

        private void ChangeImage(string path)
        {
            BackPicture.Image = ResizedImage(path);
        }

        private void Fad_Resize(object sender, EventArgs e)
        {
            ChangeImage();
        }


        private void StartConnection()
        {
            _failCount = 1;
            RetryConnection();
           
        }

        private void RetryConnection()
        {
            _connected = false;
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (sender, e) => TryToConnect();
            worker.RunWorkerCompleted += (sender, e) => DisplayConnectionSuccess(sender, e);
            worker.RunWorkerAsync();
        }
       

        private void DisplayConnectionSuccess(object sender, RunWorkerCompletedEventArgs e)
        {
             if (_connected)
            {
                Action tileAction = () => ConnectionTile.Text = "Connection succeeded";
                ConnectionTile.Invoke(tileAction);

                Action helpAction = () => ConnectionHelp.Visible = false;
                ConnectionHelp.Invoke(helpAction);

                // TODO: Switch Panel
            }
            else
            {
                _failCount++;

                Action tileAction = () => ConnectionTile.Text = $"Connecting (Attempt {_failCount})";
                ConnectionTile.Invoke(tileAction);
                if (_failCount > 1)
                {
                    Action helpAction = () => ConnectionHelp.Visible = true;
                    ConnectionHelp.Invoke(helpAction);
                }
                RetryConnection();
            }
        }

      

        private void TryToConnect() {
            var connection = new Connection();
            _connected = connection.TestConnection();
           
        }
    }
}
