using System;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Xml;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using CodeProject.Downloader;
using System.IO;

namespace InstallPad
{
    public partial class InstallPad : Form
    {
        private ControlList controlList;

        // Initialize and use if we encounter errors while loading the applist file.
        private AppListErrorDialog errorDialog;
        private AppListErrorBox appListErrorBox;
        
        OpenFileDialog openDialog = new OpenFileDialog();

        // When the user clicks "Install All," every time something finishes downloading or
        // installing, we should begin downloading/installing the next enabled app on the list.
        private bool installingAll = false;

        // For now, only install one app at a time.
        private int currentlyInstalling = 0;

        public InstallPad()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Load the application config file and restore form position
        /// </summary>
        private void LoadConfigFile()
        {
            TextReader reader=null;
            Rectangle formBounds = Rectangle.Empty ;
            try
            {
                string configFolder = Path.GetDirectoryName(InstallPadApp.ConfigFilePath);
                if (!Directory.Exists(configFolder))
                    Directory.CreateDirectory(configFolder);

                reader = new StreamReader(InstallPadApp.ConfigFilePath);
                RectangleConverter converter = new RectangleConverter();
                formBounds = (Rectangle)converter.ConvertFromString(reader.ReadLine());
            }
            catch (NotSupportedException)
            {
                // Error in the configure file. Just ignore it.
            }
            catch (IOException)
            {
                // No config file found. Ignore.
            }
            finally
            {
                if (reader != null)
                    reader.Close();
            }
            if (formBounds != Rectangle.Empty)
            {
                // If the bounds are outside of the screen's work area, move the form so it's not outside
                // of the work area. This can happen if the user changes their resolution 
                // and we then restore the applicationto its position -- it may be off screen and 
                // then they can't see it or move it.

                // Get the working area of the monitor that contains this rectangle (in case it's a
                // multi-display system
                Rectangle workingArea = Screen.GetWorkingArea(formBounds);
                if (formBounds.Left < workingArea.Left)
                    formBounds.Location = new Point(workingArea.Location.X, formBounds.Location.Y);
                if (formBounds.Top < workingArea.Top)
                    formBounds.Location = new Point(formBounds.Location.X, workingArea.Location.Y);
                if (formBounds.Right > workingArea.Right)
                    formBounds.Location = new Point(formBounds.X - (formBounds.Right - workingArea.Right),
                        formBounds.Location.Y);
                if (formBounds.Bottom > workingArea.Bottom)
                    formBounds.Location = new Point(formBounds.X, 
                        formBounds.Y - (formBounds.Bottom - workingArea.Bottom));

                this.Bounds = formBounds;
            }
                
        }

        private void InstallPad_Load(object sender, EventArgs e)
        {
            this.KeyUp += new KeyEventHandler(InstallPad_KeyUp);
            this.FormClosing += new FormClosingEventHandler(InstallPad_FormClosing);
            this.controlList=new ControlList();
            controlList.Width = this.controlListPanel.Width;
            controlList.Height= this.controlListPanel.Height;
            this.controlListPanel.Controls.Add(controlList);
            
            controlList.Anchor = AnchorStyles.Right | AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;

            this.controlList.Resize += new EventHandler(controlList_Resize);

            BuildContextMenuEntries();            

            // Restore form position etc. from the installpad config file
            LoadConfigFile();
            // Should be externalized
            string errorMessage = "Error creating temporary folder for downloaded files: ";
            // Try and create the temp folder that we'll be downloading to.
            // If we aren't successful, maybe log a warning            
            if (!Directory.Exists(InstallPadApp.InstallFolder))
            {
                try
                {
                    Directory.CreateDirectory(InstallPadApp.InstallFolder);
                }
                catch (System.IO.IOException)
                {
                    //Debug.WriteLine("Error creating install folder: " + ex);
                    MessageBox.Show(this,errorMessage + InstallPadApp.InstallFolder,
                        "Error creating install folder",MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    // Should log this TODO
                }
                catch (NotSupportedException)
                {
                    MessageBox.Show(this, errorMessage + InstallPadApp.InstallFolder,
                        "Error creating install folder", MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }

            // Error box used to indicate a problem loading the application list
            appListErrorBox = new AppListErrorBox();
            this.controlListPanel.Controls.Add(appListErrorBox);
            appListErrorBox.Visible = false;
            appListErrorBox.openLink.Click += new EventHandler(openLink_Click);

            // Load the application list. If not successful (file not found),
            // all controls will be disabled.
            LoadApplicationList(InstallPadApp.AppListFile);
        }

        void InstallPad_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyValue == 'O' && e.Control)
            {
                // Do something
                e.Handled = true;

                // Don't open a new list if something is downloading or installing
                foreach (ApplicationListItem item in this.controlList.ListItems)
                {
                    if (item.Downloading || item.Installing)
                    {
                        MessageBox.Show(this,
                            "Can't open a new application list while an program is downloading or installing.",
                            "Can't open new application list",MessageBoxButtons.OK,MessageBoxIcon.Error);
                        return;
                    }
                }
                ShowAppListOpenDialog();
            }
        }

        private void ShowAppListOpenDialog()
        {
            DialogResult result = openDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                appListErrorBox.Hide();

                this.controlList.ClearListItems();

                LoadApplicationList(openDialog.FileName);
            }

        }

        /// <summary>
        /// This link can be clicked only when an application list has failed to load.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void openLink_Click(object sender, EventArgs e)
        {            
            DialogResult result = openDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                appListErrorBox.Hide();
                LoadApplicationList(openDialog.FileName);
            }
        }
        private void LoadApplicationList(string filename){
            ApplicationList appList;
            
            try
            {
                appList = ApplicationList.FromFile(filename);
                InstallPadApp.AppList = appList;
            }
            catch (FileNotFoundException)
            {
                ShowErrorBox("Could not find an application file. Ensure that there is an " +
                "applist.xml file in the same folder as InstallPad.exe",null);
                return;
            }
            catch (XmlException ex)
            {
                ShowErrorBox("Error parsing the application file. The file contains invalid XML.",
                    ex.Message);
                return;
            }

            appList.FileName = filename;

            if (appList.ApplicationItems.Count <= 0)
                SetControlsEnabled(false);
            else
                SetControlsEnabled(true);

            // Show errors, if we had any in loading
            if (appList.Errors.Count > 0)
            {
                errorDialog = new AppListErrorDialog();
                foreach (string error in appList.Errors)
                    errorDialog.errorsText.AppendText(error + System.Environment.NewLine);
                // Show the "encountered errors" label
                this.errorLabel.Show();
                this.errorLink.Show();
            }
            ArrayList toAdd = new ArrayList();
            foreach (ApplicationItem item in appList.ApplicationItems)
            {
                ApplicationListItem listItem = CreateApplicationListItem(item);
                toAdd.Add(listItem);
            }

            // Add the controls all at once.
            this.controlList.AddAll(toAdd);
            
        }
        /// <summary>
        /// Creates a list item and listens to its events
        /// </summary>
        /// <param name="?"></param>
        /// <returns></returns>
        private ApplicationListItem CreateApplicationListItem(ApplicationItem item)
        {
            ApplicationListItem listItem = new ApplicationListItem(item);
            listItem.FinishedDownloading += new EventHandler(HandleFinishedDownloading);
            listItem.FinishedInstalling += new EventHandler(HandleFinishedInstalling);
            return listItem;
        }
        /// <summary>
        /// Show an error box in the center of the application list, detailing
        /// that we can't find an applist.xml
        /// </summary>
        void ShowErrorBox(string errorCaption, string details)
        {
            appListErrorBox.errorLabel.Text = errorCaption;

            if (details == null)
                appListErrorBox.DetailsVisible = false;
            else 
                appListErrorBox.DetailsText = details;

            UpdateErrorBoxLocation();
            this.appListErrorBox.BringToFront();
            appListErrorBox.Visible = true;

            SetControlsEnabled(false);
            return;
        }

        void InstallPad_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Serialize form position to file, so we can restore it later.
            RectangleConverter convert = new RectangleConverter();
            string formPosition = convert.ConvertToString(this.Bounds);           

            TextWriter writer=null;
            try
            {
                string configFolder = Path.GetDirectoryName(InstallPadApp.ConfigFilePath);
                if (!Directory.Exists(configFolder))
                    Directory.CreateDirectory(configFolder);
                writer = new StreamWriter(InstallPadApp.ConfigFilePath);
                writer.Write(formPosition);
                
            }finally{
                if (writer!=null)
                    writer.Close();
            }

        }

        /// <summary>
        /// Needed when the interface has no items to use.
        /// </summary>
        private void SetControlsEnabled(bool enabled)
        {
            this.buttonInstall.Enabled = enabled;
            
            if (enabled)
                this.controlList.ContextMenu = this.menu;
            else
                this.controlList.ContextMenu = null;
        }

        void controlList_Resize(object sender, EventArgs e)
        {
            if (this.appListErrorBox!=null)
                UpdateErrorBoxLocation();
        }
        void UpdateErrorBoxLocation()
        {
            int x = (this.controlList.Width - this.appListErrorBox.Width) / 2;
            int y = (this.controlList.Height - this.appListErrorBox.Height) / 2;

            this.appListErrorBox.Location = new Point(x, y);
        }

        void HandleFinishedInstalling(object sender, EventArgs e)
        {
            currentlyInstalling--;
            //if (this.installingAll)
                //InstallNextItem();
        }

        void HandleFinishedDownloading(object sender, EventArgs e)
        {
            if (this.installingAll)
                DownloadNextOnList();
        }
        // TODO this isn't being used right now..
        private void InstallNextItem()
        {
            if (currentlyInstalling > 0)
                return;

            int currentlyDownloading = 0;

            // Go through all enabled items; if there's something that's downloaded and not installed,
            // install it.
            foreach (ApplicationListItem item in this.controlList.ListItems)
            {
                if (!item.Checked)
                    continue;

                if (item.DownloadComplete)
                {
                    if (!item.Installed && !item.Installing)
                    {
                        currentlyInstalling++;
                        item.InstallApplication();
                        break;
                    }
                }
                else
                    currentlyDownloading++;
            }
            if (currentlyInstalling==0 && currentlyDownloading==0)
                // We're done downloading and installing everything.
                this.installingAll = false;
        }

        private void buttonInstall_Click(object sender, EventArgs e)
        {
            this.installingAll = true;
            
            DownloadNextOnList();

            // Install any apps that've already downloaded, but are just sitting there waiting to be installed
            //InstallNextItem();
            
        }

        private void DownloadNextOnList()
        {            
            int currentlyDownloading = 0;
            foreach (ApplicationListItem item in this.controlList.ListItems)
            {
                if (item.Downloading)
                {
                    currentlyDownloading++;
                    // Once we reach the limit on simul downloads, just exit.
                }
                else if (item.Checked)
                {
                    if (!item.Installed && !item.Installing && !item.DownloadComplete)
                    {
                        currentlyDownloading++;
                        item.Download();
                    }
                }
                //}else if (item.DownloadComplete && item.Checked && !item.Installing)
                    //item.Download
                if (currentlyDownloading >= InstallPadApp.AppList.InstallationOptions.SimultaneousDownloads)
                    return;
            }
        }

        private void errorLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            this.errorDialog.ShowDialog();
        }
    }

  
}