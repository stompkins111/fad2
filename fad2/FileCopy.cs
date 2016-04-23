﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Windows.Forms;
using fad2.Backend;
using fad2.UI.Properties;
using log4net;
using MetroFramework;
using MetroFramework.Controls;

namespace fad2.UI
{
    public partial class FileCopy : MetroUserControl
    {
        private readonly bool _autoMode;
        private readonly Connection _connection;
        private readonly string[] _imageFileTypes = Properties.Settings.Default.ImageFileTypes.Split(',');
        private readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly long _maxThumbnailSize = Properties.Settings.Default.MaxImageThumbSize;
        private readonly int _metroTileMargin = Properties.Settings.Default.MetroTileMargin;
        private readonly int _metroTileSize = Properties.Settings.Default.MetroTileSize;
        private readonly string[] _movieFilesTypes = Properties.Settings.Default.VideoFileTypes.Split(',');

        private readonly string _programSettingsFile = $"{Application.StartupPath}\\{Properties.Settings.Default.ProgramSettingsFile}";

        private readonly BackgroundWorker _workerCopyFiles = new BackgroundWorker();
        private readonly BackgroundWorker _workerDownloadThumbs = new BackgroundWorker();
        private readonly BackgroundWorker _workerListFiles = new BackgroundWorker();

        /// <summary>
        ///     Automode-Download
        /// </summary>
        public FileCopy(bool automode)
        {
            InitializeComponent();
            _connection = new Connection(_programSettingsFile);
            _autoMode = automode;
            if (automode)
            {
                FileSplitter.Panel2Collapsed = true;
                FileSplitter.Panel2.Hide();
            }
        }

        public FileCopy()
        {
        }

        /// <summary>
        ///     Load Contents from Flashair
        /// </summary>
        /// <param name="flashairPath">Flashair-Path</param>
        /// <param name="localPath">Localpath</param>
        public void LoadContents(string flashairPath, string localPath)
        {
            LeftPanel.Controls.Clear();
            LoadLocalContents(localPath);
            LoadFlashairInfoAsync(flashairPath);
        }

        private void CopyFilesAsync()
        {
            CancelCopy.Text = Resources.AbortCopy;
            CancelCopy.Visible = true;

            _workerCopyFiles.WorkerSupportsCancellation = true;
            _workerCopyFiles.WorkerReportsProgress = true;
            _workerCopyFiles.DoWork += WorkerCopyFilesDoWork;
            _workerCopyFiles.ProgressChanged += WorkerCopyFilesProgressChanged;
            _workerCopyFiles.RunWorkerCompleted += WorkerCopyFilesRunWorkerCopyFilesCompleted;
            _workerCopyFiles.RunWorkerAsync();
        }

        private void WorkerCopyFilesRunWorkerCopyFilesCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MetroMessageBox.Show(this, e.Error.Message);
            }
            if (e.Cancelled)
            {
                MetroMessageBox.Show(this, Resources.OperationCancelled);
            }

            MetroMessageBox.Show(this, Resources.CopyFinished);
            CancelCopy.Visible = false;
        }

        private void WorkerCopyFilesProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            Progress.Maximum = 100;
            Progress.Value = e.ProgressPercentage;
            Application.DoEvents();
            if (e.UserState == null) return;
            if (e.UserState is MetroTile)
            {
                ShowPreviewFromTile((MetroTile) e.UserState);
            }
            else if (e.UserState is string)
            {
                CurrentAction.Text = (string) e.UserState;
            }
            else if (e.UserState is MetroMessageBoxProperties)
            {
                var props = (MetroMessageBoxProperties) e.UserState;
                if (MetroMessageBox.Show(this, props.Message, props.Title, props.Buttons, props.Icon) == DialogResult.Abort)
                {
                    _workerCopyFiles.CancelAsync();
                }
            }
        }

        private void WorkerCopyFilesDoWork(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;
            e.Result = CopyFiles(worker, e);
        }

        private void LoadFlashAirThumbs(BackgroundWorker worker)
        {
            var counter = 0;
            var tiles = LeftPanel.Controls.OfType<MetroTile>().ToList();
            worker.ReportProgress(0,Resources.ReadingThumbnailsFromFlashair);
            int maximum=tiles.Count;
            foreach (var tile in tiles)
            {
                int progress = (100*counter)/maximum;
                var fileInfo = (FlashAirFileInformation) tile.Tag;
                worker.ReportProgress(progress,string.Format(Resources.ReadingThumnailNo, counter, Progress.Maximum));
                TryGetFlashAirThumb(tile, fileInfo);
                counter++;
            }
        }

        private void LoadFlashairInfoAsync(string path)
        {
            _workerListFiles.WorkerSupportsCancellation = true;
            _workerListFiles.WorkerReportsProgress = true;
            _workerListFiles.DoWork += WorkerListFilesDoWork;
            _workerListFiles.ProgressChanged += WorkerListFilesProgressChanged;
            _workerListFiles.RunWorkerCompleted += WorkerListFilesCompleted;
            _workerListFiles.RunWorkerAsync(path);
        }

        private void LoadFlashairThumbsAsync()
        {
            _workerDownloadThumbs.WorkerSupportsCancellation = true;
            _workerDownloadThumbs.WorkerReportsProgress = true;
            _workerDownloadThumbs.DoWork += WorkerDownloadThumbsDoWork;
            _workerDownloadThumbs.ProgressChanged += WorkerDownloadThumbsProgressChanged;
            _workerDownloadThumbs.RunWorkerCompleted += WorkerDownloadThumbsCompleted;
            _workerDownloadThumbs.RunWorkerAsync();
        }

        private void WorkerDownloadThumbsCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (!_autoMode) return;
            CurrentAction.Text = Resources.ReadyToCopy;
            CopyFilesAsync();
        }

        private void WorkerDownloadThumbsProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            Progress.Maximum = 100;
            Progress.Value = e.ProgressPercentage;
            Application.DoEvents();
            if (e.UserState is string)
            {
                CurrentAction.Text = (string)e.UserState;
            }
        }

        private void WorkerDownloadThumbsDoWork(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;
            LoadFlashAirThumbs(worker);
        }

        private void WorkerListFilesCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            ResizeTiles(LeftPanel);
            if (_connection.Settings.LoadThumbs)
            {
                LoadFlashairThumbsAsync();
            }
            else
            {
                CopyFilesAsync();
            }
        }

        private void WorkerListFilesProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            Progress.Maximum = 100;
            Progress.Value = e.ProgressPercentage;
            if (e.UserState==null) return;
            if (e.UserState is string)
            {
                CurrentAction.Text = (string)e.UserState;
            } else if (e.UserState is MetroTile)
            {
                var tile = (MetroTile) e.UserState;
                 FileTooltip.SetToolTip(tile, tile.Text);
                 LeftPanel.Controls.Add(tile);
                 tile.Refresh();
            } else if (e.UserState is MetroMessageBoxProperties)
            {
                var props = (MetroMessageBoxProperties)e.UserState;
                if (MetroMessageBox.Show(this, props.Message, props.Title, props.Buttons, props.Icon) == DialogResult.Cancel)
                {
                    _workerListFiles.CancelAsync();
                }
            }
        }

        private void WorkerListFilesDoWork(object sender, DoWorkEventArgs e)
        {
            var worker = sender as BackgroundWorker;
            e.Result = LoadFlashairInfo(worker, (string) e.Argument);
        }

        private int LoadFlashairInfo(BackgroundWorker worker, string path)
        {
            var maxValue = 0;
            _log.Info($"Read information from {path}");
            worker.ReportProgress(0, string.Format(Resources.ReadingFlashAirInfoAtPath, path));
            int imageCount;
            try
            {
                _log.Debug($"Download filecount for {path}");
                imageCount = _connection.GetFileCount(path);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                // could not read Number of files. Just ignore this for now. Just download Thumbs and don't show the progress
                imageCount = int.MaxValue;
            }
            worker.ReportProgress(0, string.Format(Resources.ReadingInfoFromFilesAtPath, imageCount, path));
            if (imageCount <= 0) return maxValue;

            List<FlashAirFileInformation> allFiles;
            try
            {
                _log.Debug($"Get {imageCount} FileSplitter from {path}");
                allFiles = _connection.GetFiles(path);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                worker.ReportProgress(0, new MetroMessageBoxProperties(null) {Buttons = MessageBoxButtons.RetryCancel, Icon = MessageBoxIcon.Error, Title = Resources.ErrorFlashairGenericTitle, Message = Resources.ErrorDownloadingFilelist});
                return 0;
            }

            if (allFiles != null)
            {
                // start with the directories:
                // No ReadOnly, cause the FlashAir-SystemImage is readonly. ANd we do not want to copy that every time
                var nonHiddenfiles = allFiles.Where(af => !af.Hidden && !af.SystemFile && !af.ReadOnly).ToList();
                maxValue = nonHiddenfiles.Count;

                var counter = 0;
                foreach (var singleFile in nonHiddenfiles.Where(nhf => nhf.IsVolume || nhf.IsDirectory))
                {
                    var progress = 100*counter/maxValue;
                    // there shouldn't really be any volumes on that disc, but doesn't hurt to check
                    worker.ReportProgress(progress);
                    counter++;

                    if (!path.EndsWith("/"))
                    {
                        path += '/';
                    }
                    if (_autoMode)
                    {
                        LoadFlashairInfo(worker, path + singleFile.Filename);
                    }
                }
                foreach (var singleFile in nonHiddenfiles.Where(nhf => !nhf.IsVolume && !nhf.IsDirectory))
                {
                    var progress = 100*counter/maxValue;
                    worker.ReportProgress(progress);
                    counter++;
                    if (_autoMode)
                    {
                        switch (_connection.Settings.FileTypesToCopy)
                        {
                            case (int) ProgramSettings.FileTypes.Images:
                                if (!_imageFileTypes.Contains(singleFile.Extension.ToLower()))
                                {
                                    continue;
                                }
                                break;
                            case (int) ProgramSettings.FileTypes.Videos:
                                if (!_imageFileTypes.Contains(singleFile.Extension.ToLower()) && !_movieFilesTypes.Contains(singleFile.Extension.ToLower()))
                                {
                                    continue;
                                }
                                break;
                        }
                    }
                    var tile = new MetroTile
                    {
                        Text = singleFile.Filename,
                        Width = _metroTileSize,
                        Height = _metroTileSize,
                        Tag = singleFile
                    };
                    tile.Click += LeftTile_Click;
                    worker.ReportProgress(progress, tile);

                
                }
            }

            // ResizeTiles(LeftPanel);
            return maxValue;
        }


        private int CopyFiles(BackgroundWorker worker, DoWorkEventArgs e)
        {
            var counter = 0;
            var duration = TimeSpan.FromSeconds(1);
            long lastSize = 0;
            worker.ReportProgress(0, Resources.CopyingFiles);

            var cardId = _connection.GetCid();
            var appId = _connection.GetAppName();

            if (LeftPanel.Controls.OfType<MetroTile>().Any())
            {
                var allTiles = LeftPanel.Controls.OfType<MetroTile>().ToList();
                _log.Info($"Start upload of {allTiles.Count} files");

                var max = allTiles.Count;
                foreach (var tile in allTiles)
                {
                    var progress = 100*counter/max;

                    if (worker.CancellationPending)
                    {
                        e.Cancel = true;
                        break;
                    }
                    worker.ReportProgress(progress, tile);
                    var fileInfo = (FlashAirFileInformation) tile.Tag;
                    var currentByteSpeed = lastSize/duration.TotalSeconds;
                    var unit = "Bytes/s";
                    if (currentByteSpeed > 1024)
                    {
                        currentByteSpeed = currentByteSpeed/1024;
                        unit = "KBytes/s";
                    }
                    if (currentByteSpeed > 1024)
                    {
                        currentByteSpeed = currentByteSpeed/1024;
                        unit = "MBytes/s";
                    }

                    worker.ReportProgress(progress, string.Format(Resources.CopyFileOfAtSpeed, counter + 1, Progress.Maximum, fileInfo.Filename, currentByteSpeed, unit));

                    var targetFolder = CreateTargetFolder(fileInfo, cardId, appId);
                    var targetFile = Path.Combine(targetFolder, fileInfo.Filename);
                    var doDelete = _connection.Settings.DeleteFiles;
                    var doCopy = true;
                    if (File.Exists(targetFile))
                    {
                        switch (_connection.Settings.ExistingFiles)
                        {
                            case (int) ProgramSettings.OverwriteModes.Copy:
                                targetFile = Path.Combine(targetFolder, $"{DateTime.Now:yyMMdd-HHss}{fileInfo.Filename}");
                                break;
                            case (int) ProgramSettings.OverwriteModes.Never:
                                var targetFileInfo = new FileInfo(targetFile);
                                doCopy = targetFileInfo.CreationTime < fileInfo.PictureTaken;
                                break;
                            default:
                                doCopy = _connection.Settings.ExistingFiles == (int) ProgramSettings.OverwriteModes.Always;
                                break;
                        }
                        doDelete &= doCopy;
                    }
                    if (doCopy)
                    {
                        var startTick = DateTime.Now;
                        Stream sourceFileStream = null;
                        try
                        {
                            sourceFileStream = _connection.DownloadFile(fileInfo.Directory, fileInfo.Filename);
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex);
                            if (ex is WebException && ((WebException) ex).Status == WebExceptionStatus.NameResolutionFailure)
                            {
                                worker.ReportProgress(progress, new MetroMessageBoxProperties(null) {Buttons = MessageBoxButtons.OK, Icon = MessageBoxIcon.Error, Title = Resources.CannotFindFlashairTitle, Message = Resources.CannotFindFlashairMessage});
                            }
                            else
                            {
                                worker.ReportProgress(progress, new MetroMessageBoxProperties(null) {Buttons = MessageBoxButtons.OKCancel, Icon = MessageBoxIcon.Error, Title = Resources.ErrorFlashairGenericTitle, Message = string.Format(Resources.FailedDownloadingFile, fileInfo.Filename)});
                            }
                        }
                        if (sourceFileStream == null) continue;
                        using (var fileStream = File.Create(targetFile))
                        {
                            try
                            {
                                sourceFileStream.CopyTo(fileStream);
                                lastSize = fileStream.Length;
                            }
                            catch (Exception ex)
                            {
                                _log.Error(ex);
                            }
                        }
                        duration = DateTime.Now - startTick;
                        if (doDelete)
                        {
                            _connection.DeleteFile(fileInfo.Directory, fileInfo.Filename);
                        }
                    }
                    //Progress.Value = counter;
                    //Application.DoEvents();
                    counter++;

                    worker.ReportProgress(counter*100/Progress.Maximum);
                }
            }

            return counter;
        }
 

        private string CreateTargetFolder(FlashAirFileInformation fileInfo, string cardId, string appId)
        {
            // cardID=0, appId=1, Date=2

            var targetFolderName = string.Format(_connection.Settings.FolderFomat, cardId, appId, fileInfo.PictureTaken);

            var downloadFolder = new DirectoryInfo(_connection.Settings.LocalPath);
            return !downloadFolder.Exists ? null : Directory.CreateDirectory(Path.Combine(downloadFolder.FullName, targetFolderName)).FullName;
        }

        private void TryGetFlashAirThumb(MetroTile tile, FlashAirFileInformation fileInformation)
        {
            try
            {
                _log.Debug($"Download thumbnail {fileInformation.Directory} {fileInformation.Filename}");
                var thumBitmap = _connection.DownloadThumbnail(fileInformation.Directory, fileInformation.Filename, Properties.Settings.Default.ImageFileTypes);
                if (thumBitmap == null)
                {
                    return;
                }
                Image.GetThumbnailImageAbort myCallback = ThumbnailCallback;
                var thumb = thumBitmap.GetThumbnailImage(_metroTileSize, _metroTileSize, myCallback, IntPtr.Zero);
                tile.TileImage = thumb;
                tile.UseTileImage = true;
            }
            catch (Exception ex)
            {
                // Could not download Thumb after 5 retries. Well. Just a thumb. Let's ignore this
                _log.Error(ex);
            }
        }

        private void LoadLocalContents(string localPath)
        {
            CurrentAction.Text = Resources.ReadLocalDir;
            if (Directory.Exists(localPath))
            {
                var currentDirectory = new DirectoryInfo(localPath);
                var files = currentDirectory.GetFiles();
                var folders = currentDirectory.GetDirectories();

                RightPanel.Controls.Clear();
                Progress.Maximum = folders.Length;
                var counter = 0;
                foreach (var folder in folders.OrderBy(fld => fld.Name))
                {
                    var tile = new MetroTile {Text = folder.Name, Width = _metroTileSize, Height = _metroTileSize, Style = MetroColorStyle.Yellow};
                    FileTooltip.SetToolTip(tile, folder.Name);
                    RightPanel.Controls.Add(tile);
                    Progress.Value = counter;
                    counter++;
                    Application.DoEvents();
                }
                Progress.Maximum = folders.Length;
                counter = 0;
                foreach (var fileInfo in files.OrderBy(fld => fld.Name))
                {
                    if (!fileInfo.Name.Contains('.') && _connection.Settings.FileTypesToCopy != (int) ProgramSettings.FileTypes.AllFiles)
                    {
                        continue;
                    }

                    var tile = new MetroTile {Text = fileInfo.Name, Width = _metroTileSize, Height = _metroTileSize};
                    FileTooltip.SetToolTip(tile, fileInfo.Name);
                    TryGetThumb(tile, fileInfo);
                    RightPanel.Controls.Add(tile);
                    Progress.Value = counter;
                    counter++;
                    Application.DoEvents();
                }
            }
            ResizeTiles(RightPanel);
        }

        private bool ThumbnailCallback()
        {
            return false;
        }

        private void TryGetThumb(MetroTile tile, FileInfo fileInfo)
        {
            try
            {
                if (fileInfo.Length > _maxThumbnailSize) return;
                var validExtensions = Properties.Settings.Default.ImageFileTypes.Split(',');
                var extension = fileInfo.Name.Substring(fileInfo.Name.LastIndexOf('.') + 1).ToLower();
                if (!validExtensions.Contains(extension)) return;
                Image.GetThumbnailImageAbort myCallback = ThumbnailCallback;
                var thumBitmap = new Bitmap(fileInfo.FullName);
                var thumb = thumBitmap.GetThumbnailImage(_metroTileSize, _metroTileSize, myCallback, IntPtr.Zero);
                tile.TileImage = thumb;
                tile.UseTileImage = true;
            }
            catch (Exception ex)
            {
                // No valid Image File as it seems. Not a big deal. Just a missing THumbnail
                _log.Error(ex);
            }
        }

        private void ResizeTiles(Control parentControl)
        {
            try
            {
                var maxWidth = parentControl.Width;
                if (maxWidth < _metroTileSize + _metroTileMargin)
                {
                    return;
                }
                var currentX = -_metroTileSize - _metroTileMargin;
                var currentY = 0;

                foreach (var control in parentControl.Controls.OfType<MetroTile>())
                {
                    if (currentX + _metroTileSize*2 + _metroTileMargin <= maxWidth)
                    {
                        currentX += _metroTileSize + _metroTileMargin;
                    }
                    else
                    {
                        currentY += _metroTileSize + _metroTileMargin;
                        currentX = 0;
                    }
                    control.Left = currentX + _metroTileMargin/2;
                    control.Top = currentY + _metroTileMargin/2;
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }
        }

        private void RightPanel_Layout(object sender, LayoutEventArgs e)
        {
            ResizeTiles(RightPanel);
            Refresh();
        }

        private void LeftPanel_Layout(object sender, LayoutEventArgs e)
        {
            ResizeTiles(LeftPanel);
            Refresh();
        }

        private void StartCopy_Click(object sender, EventArgs e)
        {
            if (_autoMode)
            {
                _workerCopyFiles.CancelAsync();
            }
            CancelCopy.Visible = false;
            //CopyFilesAsync();
        }

        private void ShowPreviewFromTile(MetroTile tile)
        {
            var fileData = (FlashAirFileInformation) tile.Tag;
            SinglePreviewThumb.TileImage = tile.TileImage;
            SinglePreviewThumb.UseTileImage = tile.UseTileImage;
            ImageFolderContent.Text = fileData.Directory;
            ImageFilenameContent.Text = fileData.Filename;

            var kbSize = (double) fileData.Size/1024;
            var mbSize = kbSize/1024;
            var gbSize = mbSize/1024;

            if (gbSize > 1)
            {
                ImageSizeContent.Text = $"{gbSize:N} GByte";
            }
            else if (mbSize > 1)
            {
                ImageSizeContent.Text = $"{mbSize:N} MByte";
            }
            else if (kbSize > 1)
            {
                ImageSizeContent.Text = $"{kbSize:N} KByte";
            }
            else
            {
                ImageSizeContent.Text = $"{fileData.Size} Byte";
            }
            ImageInfoPanel.Visible = true;
            SinglePreviewThumb.Refresh();
            Application.DoEvents();
        }

        private void LeftTile_Click(object sender, EventArgs e)
        {
            var tile = (MetroTile) sender;
            ShowPreviewFromTile(tile);
        }
    }
}