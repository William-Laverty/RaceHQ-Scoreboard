using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

#pragma warning disable IDE1006
#pragma warning disable IDE0017

namespace AthleticsCarnivalScoreboard
{
    public partial class frmRaceCenter : Form
    {
        // The control list for the lane panels shown on screen
        private List<Panel> panelControlList { get; set; }

        // The control lists for all the lane and competitor information labels shown on screen
        private List<Label> laneLabelControlList { get; set; }
        private List<Label> nameLabelControlList { get; set; }
        private List<Label> timeLabelControlList { get; set; }
        private List<Label> placeLabelControlList { get; set; }

        private List<Event> eventList { get; set; } // Stores an array of Event objects
        private List<Lane> laneList = new List<Lane>(); // Stores an array of Lane objects
        private List<Result> resultList = new List<Result>(); // Store an array of Result objects for a race
        private Event currentEventObject { get; set; } // Stores the current event 
        private string raceHQDataFolderPath { get; set; } // Stores the path to the Race HQ data folder
        private int currentEventComboboxIndex = -1; // Current index selected in the event combobox
        private Boolean handleEventChangeComboboxFlag = true; // Flag indicating whether the users event selection change in the combobox should be honored
        private FileSystemWatcher fileSystemWatcher { get; set; } // File system watcher

        // Current file locked onto
        private String currentRaceFileLockedOn { get; set; }
        public List<AthleticsCarnivalScoreboard.frmRaceCenter.LaneData> LaneDataList
        {
            get => laneDataList;
            set => laneDataList = value;
        }
        private List<LaneData> laneDataList;

        // Flag indicating whether to truncate times (i.e. remove the 10 minutes and above section)
        private Boolean truncateTimes = (ConfigurationManager.AppSettings["truncateTimes"] == "1" ? true : false);

        private int rhqStopwatchID = 0x000; // ID of the RHQ stopwatch

        // Thread-safe delegates
        delegate void UpdateScoreboardFromBackground(List<Result> resultList);
        delegate void LockOntoFileFromBackground(string fileName);
        delegate void AppendLogReturningVoidDelegate(string text);

        // The scoreboard
        Scoreboard theScoreboard = new Scoreboard();
        private Boolean theScoreboardVisible = false;

        public frmRaceCenter()
        {
            InitializeComponent();
        }

        private void frmRaceCenter_Load(object sender, EventArgs e)
        {
            // Populate all the control lists: (list index) + 1 = lane number
            panelControlList = new List<Panel>() { pnlLane1, pnlLane2, pnlLane3, pnlLane4, pnlLane5, pnlLane6, pnlLane7, pnlLane8, pnlLane9, pnlLane10 }; // Panels
            laneLabelControlList = new List<Label>() { lblLane1, lblLane2, lblLane3, lblLane4, lblLane5, lblLane6, lblLane7, lblLane8, lblLane9, lblLane10 }; // Lane labels
            nameLabelControlList = new List<Label>() { lblLane1Name, lblLane2Name, lblLane3Name, lblLane4Name, lblLane5Name, lblLane6Name, lblLane7Name, lblLane8Name, lblLane9Name, lblLane10Name }; // Name labels
            timeLabelControlList = new List<Label>() { lblLane1Time, lblLane2Time, lblLane3Time, lblLane4Time, lblLane5Time, lblLane6Time, lblLane7Time, lblLane8Time, lblLane9Time, lblLane10Time }; // Time labels
            placeLabelControlList = new List<Label>() { lblLane1Place, lblLane2Place, lblLane3Place, lblLane4Place, lblLane5Place, lblLane6Place, lblLane7Place, lblLane8Place, lblLane9Place, lblLane10Place }; // Place labels
            theScoreboard.truncateTimes = truncateTimes; // Update the truncateTimes flag on the scoreboard

            // Quickly open and close the scoreboard so the form loads
            theScoreboard.Show();
            theScoreboard.Hide();

            // Set the top label to have a transparent background
            lblDropDown.Parent = lblFileMonitorWarning;
            lblDropDown.BackColor = System.Drawing.Color.Transparent;
        }

        private void btnLoadEvents_Click(object sender, EventArgs e)
        {
            // Open a file dialog box to select the events file
            var eventFileDialog = new OpenFileDialog
            {
                // Basic properties
                Title = "Select race events CSV file",
                DefaultExt = "csv",
                Filter = "CSV files (*.csv)|*.csv",
                FilterIndex = 2,

                // Initial directory settings
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                RestoreDirectory = true,

                // Validation checks
                CheckFileExists = true,
                CheckPathExists = true
            };


            // Check if we have a file
            if (eventFileDialog.ShowDialog() == DialogResult.OK)
            {
                // Load the lanes
                if (loadLanes() == true)
                {
                    // Load the event list
                    if (loadEventList(eventFileDialog.FileName) == true)
                    {
                        // Setup the combo box to have custom drawing
                        cmboEventList.DrawMode = DrawMode.OwnerDrawVariable;
                        cmboEventList.DrawItem += new DrawItemEventHandler(cmboEventList_DrawItem);

                        // Bind the event combobox
                        cmboEventList.DisplayMember = "event_name";
                        cmboEventList.ValueMember = "event_id";
                        cmboEventList.DataSource = eventList;

                        // Check if the RaceHQ folder has been selected
                        if (raceHQDataFolderPath != null)
                        {
                            // Enable the other buttons
                            btnMonitorRaceFiles.Enabled = true;
                        }
                    }
                }
            }
        }

        // Define a structure to hold lane data
        public struct LaneData
        {
            public int LaneNumber { get; set; }
            public string HouseCode { get; set; }
            public string HouseName { get; set; }
            public string HouseColor { get; set; }
            public string TextColor { get; set; }
        }

        private Boolean loadLanes()
        {
            logEntry("Loading lanes...");

            // Initialize the lane data
            laneDataList = new List<LaneData>
            {
                new LaneData { LaneNumber = 1, HouseCode = "BN", HouseName = "Burgmann", HouseColor = "fbc628", TextColor = "000000" },
                new LaneData { LaneNumber = 2, HouseCode = "SH", HouseName = "Sheaffe", HouseColor = "FFFFFF", TextColor = "000000" },
                new LaneData { LaneNumber = 3, HouseCode = "MD", HouseName = "Middleton", HouseColor = "13a25e", TextColor = "FFFFFF" },
                new LaneData { LaneNumber = 4, HouseCode = "EW", HouseName = "Edwards", HouseColor = "7c2325", TextColor = "FFFFFF" },
                new LaneData { LaneNumber = 5, HouseCode = "HA", HouseName = "Hay", HouseColor = "000000", TextColor = "FFFFFF" },
                new LaneData { LaneNumber = 6, HouseCode = "ED", HouseName = "Eddison", HouseColor = "1f3452", TextColor = "FFFFFF" },
                new LaneData { LaneNumber = 7, HouseCode = "GY", HouseName = "Garnsey", HouseColor = "3890c8", TextColor = "FFFFFF" },
                new LaneData { LaneNumber = 8, HouseCode = "BL", HouseName = "Blaxland", HouseColor = "e03930", TextColor = "FFFFFF" },
                new LaneData { LaneNumber = 9, HouseCode = "JO", HouseName = "Jones", HouseColor = "1b4c2b", TextColor = "FFFFFF" },
                new LaneData { LaneNumber = 10, HouseCode = "GN", HouseName = "Garran", HouseColor = "513263", TextColor = "FFFFFF" }
            };

            // Convert LaneData to Lane objects and populate laneList
            laneList = laneDataList.Select(data => new Lane
            {
                lane = data.LaneNumber,
                house_code = data.HouseCode,
                house_name = data.HouseName,
                house_color = data.HouseColor,
                text_color = data.TextColor
            }).ToList();

            // Loop through each lane
            foreach (Lane laneObject in laneList)
            {
                // Lane index = lane number - 1
                var laneIndex = Convert.ToUInt16(laneObject.lane) - 1;

                // Set the panel colors
                var panel = panelControlList[laneIndex];
                panel.BackColor = System.Drawing.ColorTranslator.FromHtml("#" + laneObject.house_color);

                // Set the label colors
                var textColor = System.Drawing.ColorTranslator.FromHtml("#" + laneObject.text_color);
                laneLabelControlList[laneIndex].ForeColor = textColor;
                timeLabelControlList[laneIndex].ForeColor = textColor;
                placeLabelControlList[laneIndex].ForeColor = textColor;
                nameLabelControlList[laneIndex].ForeColor = textColor;

                // Set the house name for the lane
                nameLabelControlList[laneIndex].Text = laneObject.house_name;
            }

            logEntry("Lanes loaded");
            logEntry("----");

            // Return success
            return true;
        }
        // --- Updates --

        private bool loadEventList(string filename)
        {
            logEntry("Loading events from file...");

            try
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = false
                };

                using var reader = new StreamReader(filename);
                using var csv = new CsvReader(reader, config);

                eventList = csv.GetRecords<Event>().ToList();

                logEntry("Events successfully loaded.----");
                return true;
            }
            catch (Exception ex) // Catch a general exception
            {
                logEntry($"Error loading events: {ex.Message}----");
                return false;
            }
        }


        private void btnRaceHQFolder_Click(object sender, EventArgs e)
        {

            // Check we are currently not monitoring for changes
            if ((fileSystemWatcher != null) && (fileSystemWatcher.EnableRaisingEvents))
            {
                MessageBox.Show("You must stop monitoring files before changing the Race HQ folder.", "Error", MessageBoxButtons.OK);
            }

            else
            {

                // Show an information message
                var confirmMessage = MessageBox.Show("You need to start and save a race before doing this so the folder is created.\n\nHere is a sample path so you can locate the correct folder: C:\\Users\\Test\\Documents\\ResultsHQ\\Pack\\19-09-2018\\Backup\n\nMake sure you select the Backup folder, as that contains the files with the required information.", "Important", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);

                if (confirmMessage == DialogResult.OK)
                {

                    // Open a dialog to select the Race HQ data folder
                    FolderBrowserDialog raceHQFolderDialog = new FolderBrowserDialog
                    {
                        ShowNewFolderButton = false
                    };

                    if (raceHQFolderDialog.ShowDialog() == DialogResult.OK)
                    {

                        // Set the folder path
                        raceHQDataFolderPath = raceHQFolderDialog.SelectedPath;

                        // Create the file system watcher
                        createFileSystemWatcher(raceHQDataFolderPath);

                        logEntry("Race HQ folder selected: " + raceHQDataFolderPath);
                        logEntry("----");

                        // Check if the events have been loaded
                        if (eventList.Count > 0)
                        {

                            // Enable the other buttons
                            btnMonitorRaceFiles.Enabled = true;

                        }

                    }

                }

            }

        }

        private void cmboEventList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index == -1)
                return;
            ComboBox combo = ((ComboBox)sender);

            // Get a reference to the event for this row
            var eventObject = eventList[e.Index];

            // Check if the race is gated - assume yes unless otherwise told
            var rowColor = Color.Black;
            if (eventObject.is_gated == false)
            {
                rowColor = Color.LightGray;
            }

            // Draw the text
            using (SolidBrush brush = new SolidBrush(rowColor))
            {
                Font font = e.Font;

                e.DrawBackground();

                var eventDisplayName = eventObject.event_id + ": " + eventObject.event_name;

                e.Graphics.DrawString(eventDisplayName, font, brush, e.Bounds);
                e.DrawFocusRectangle();
            }
        }

        // Called when the user changes the selected item or it is changed through code
        private void cmboEventList_SelectedIndexChanged(object sender, EventArgs e)
        {

            // Check if the change should be honored
            if ((handleEventChangeComboboxFlag == true) && (currentEventComboboxIndex != cmboEventList.SelectedIndex))
            {

                // Update the currentEventComboBoxIndex
                currentEventComboboxIndex = cmboEventList.SelectedIndex;

                // Get the event object for the new event
                var eventObject = eventList[cmboEventList.SelectedIndex];

                // Change the event
                changeEvent(eventObject);

                // Alert if this is an ungated rate
                if (eventObject.is_gated == false)
                {
                    MessageBox.Show("This is an ungated race. Race Center will not work with ungated races.", "Ungated Race");
                }

            }

            // Get rid of the focus
            lstLogBox.Focus();

            // Reset the variable to true
            handleEventChangeComboboxFlag = true;

        }

        // Called when the user changes the selected item
        private void cmboEventList_SelectionChangeCommitted(object sender, EventArgs e)
        {

            // Confirm the event change and actions
            var confirmMessageBox = MessageBox.Show("Do you want to change events? This will clear the scoreboard and unlock from the current file (if any).", "Event Change", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            // No, do not change events
            if (confirmMessageBox != DialogResult.Yes)
            {
                handleEventChangeComboboxFlag = false;
                cmboEventList.SelectedIndex = currentEventComboboxIndex;
            }

        }

        // Called when we change events
        private void changeEvent(Event eventObject, bool releaseLockOnFile = true)
        {

            // Release lock on the file, if needed (the default)
            if (releaseLockOnFile)
            {
                lockOntoFileFromBackground(null);
            }

            // Update the current event object
            currentEventObject = eventObject;

            // Clear the LED board
            theScoreboard.clearScoreboardFull();

            // Set the header on the LED board
            if (currentEventObject != null)
                theScoreboard.changeHeaderLabel("Event " + currentEventObject.event_id + ": " + currentEventObject.event_name);

            // Clear the admin scoreboard
            for (int i = 0; i < panelControlList.Count; i = i + 1)
            {
                timeLabelControlList[i].Text = "";
                placeLabelControlList[i].Text = "";
            }

        }

        private void cmboEventList_DropDownClosed(object sender, EventArgs e)
        {
            // Get rid of the focus
            lstLogBox.Focus();
        }

        private void btnNextEvent_Click(object sender, EventArgs e)
        {

            // Guard against no event being selected
            if (currentEventObject == null)
            {
                MessageBox.Show("No event selected.", "Error");
                return;
            }

            if (MessageBox.Show("Advance to next event?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {

                // Advance to the next event
                if (cmboEventList.SelectedIndex + 1 >= cmboEventList.Items.Count)
                {
                    cmboEventList.SelectedIndex = 0;
                }
                else
                {
                    cmboEventList.SelectedIndex = cmboEventList.SelectedIndex + 1;
                }

                // When we programatically change the SelectedIndex this will trigger cmboEventList_SelectedIndexChanged and the subsequent changeEvent method

                // Clear the lock on the current file
                lockOntoFileFromBackground(null);

            }

        }

        private void btnMonitorRaceFiles_Click(object sender, EventArgs e)
        {

            // Currently monitoring race files
            if (fileSystemWatcher.EnableRaisingEvents) {

                if (MessageBox.Show("This will disable file monitoring and unlock you from any currently locked on files. Are you sure?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    fileSystemWatcher.EnableRaisingEvents = false;
                    btnMonitorRaceFiles.BackColor = Color.Red;
                    btnFileLockOn.Enabled = false;
                    lockOntoFileFromBackground(null);

                    logEntry("Monitoring of race files is disabled");
                    logEntry("----");

                    lblFileMonitorWarning.BackColor = System.Drawing.Color.Red;

                }

            }

            else
            {
                fileSystemWatcher.EnableRaisingEvents = true;
                btnMonitorRaceFiles.BackColor = Color.Green;

                logEntry("Monitoring of race files is enabled");
                logEntry("----");

                lblFileMonitorWarning.BackColor = SystemColors.Control;

            }

        }

        private void createFileSystemWatcher(string path)
        {

            // Create a new FileSystemWatcher and set its properties.
            fileSystemWatcher = new FileSystemWatcher();
            fileSystemWatcher.Path = path;

            // Watch for changes in LastAccess and LastWrite times, and the renaming of files or directories
            // fileSystemWatcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;

            // Only watch RHQ files
            fileSystemWatcher.Filter = "*.rhq";

            // Add event handlers.
            fileSystemWatcher.Changed += new FileSystemEventHandler(OnFileChanged);
            fileSystemWatcher.Created += new FileSystemEventHandler(OnFileChanged);

        }

        private void OnFileChanged(object source, FileSystemEventArgs e)
        {

            // Check events
            if (e.ChangeType == WatcherChangeTypes.Created)
            {

                // Check if we can lock onto this file
                if (currentRaceFileLockedOn == null)
                {

                    // Lock onto the file
                    lockOntoFileFromBackground(e.Name);

                    logEntry("Created and locked on: " + e.Name);
                    logEntry("----");

                    // Parse the file
                    parseRaceFile(e.FullPath);
                }

                // Cannot lock on
                else
                {
                    logEntry("Created and ignored: " + e.Name);
                    logEntry("----");
                }

            }
            else if (e.ChangeType == WatcherChangeTypes.Changed)
            {

                // Check this is the current file we're locked on to
                if (currentRaceFileLockedOn == e.Name)
                {
                    logEntry("Changed: " + e.Name);
                    logEntry("----");

                    // Parse the file
                    parseRaceFile(e.FullPath);
                }

                // Another file, ignore
                else
                {
                    logEntry("Changed and ignored: " + e.Name);
                    logEntry("----");
                }

            }

        }

        private void parseRaceFile(string path)
        {

            // Get a reference to the file without locking the file - need to enclose in a try...catch in case the file is
            // locked by another process
            try
            {

                var fileStream = new FileStream(@path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var streamReader = new StreamReader(fileStream);

                // To hold the current line of text
                string line;

                // Hole the current line number
                int lineNumber = 1;

                // Clear the resultList (if it exists) ready for repopulation
                resultList.Clear();

                // Read the file line-by-line
                while ((line = streamReader.ReadLine()) != null)
                {
                    // Race results start on line 11
                    if (lineNumber >= 11)
                    {

                        // Check there is not a totally blank line somehow (??)
                        if (line.Replace(" ", "").Length == 0)
                        {
                            // If so, skip this iteration
                            continue;
                        }

                        // Check that there are results, otherwise line 11 will be the sentinel line
                        if (line.Contains("TS File Data End") == false)
                        {

                            // Split the CSV line
                            var lineArray = line.Split(',');

                            // Get the fields
                            var raceTime = lineArray[11];
                            var lane = lineArray[19];
                            var placeOverall = lineArray[28];

                            // Check this is not a duplicate time for a lane
                            if (placeOverall != "0")
                            {

                                // Create a new Result object
                                Result resultObject = new Result();
                                resultObject.lane = Convert.ToInt16(lane);
                                resultObject.race_time = raceTime;
                                resultObject.race_place = Convert.ToInt16(placeOverall);

                                // Add to the resultList
                                resultList.Add(resultObject);

                            }

                        }

                    }

                    lineNumber += 1;

                }

                // Close the file
                streamReader.Close();

                // Update the scoreboard (update all rows, as this will clear any removed times as well e.g. due to the sun hitting the gates)

                theScoreboard.clearScoreboardTimesOnly(); // Clear the LED scoreboard times, ready for update

                updateScoreboardFromBackground(resultList); // Update the scoreboard

            }
            catch
            {
                logEntry("Error reading: " + path);
                logEntry("----");
            }

        }

        private void updateScoreboardFromBackground(List<Result> resultList)
        {

            // Just check the first label, as all labels were created on the same thread
            if (lblLane1Time.InvokeRequired)
            {
                UpdateScoreboardFromBackground d = new UpdateScoreboardFromBackground(updateScoreboardFromBackground);
                this.Invoke(d, new object[] { resultList });
            }
            else
            {

                // Sort resultList into place order
                List<Result> orderedResultList = resultList.OrderBy(o => o.race_place).ToList();

                // Create a variable to store the next available slot on the LED scoreboard
                // We don't use the place, in case multiple runners get the same time and place - we just sort the results array
                // and add them in that order
                var ledScoreboardNextSlotIndex = 0;

                // Loop through the orderedResultList
                foreach(Result resultObject in orderedResultList)
                {

                    // Check the length of the time
                    var raceTimeToDisplay = resultObject.race_time;
                    if (raceTimeToDisplay.Length == 11)
                    {

                        // Truncate first 00: as times will not be 10 minutes or over
                        raceTimeToDisplay = raceTimeToDisplay.Substring(truncateTimes ? 3 : 0);

                    }

                    // Update the admin scoreboard (this form)
                    var laneIndex = resultObject.lane - 1;

                    timeLabelControlList[laneIndex].Text = raceTimeToDisplay;
                    placeLabelControlList[laneIndex].Text = resultObject.race_place.ToString();

                    // Update the LED scoreboard                
                    theScoreboard.placeLabelControlList[ledScoreboardNextSlotIndex].Text = resultObject.race_place.ToString();
                    theScoreboard.timeLabelControlList[ledScoreboardNextSlotIndex].Text = raceTimeToDisplay;

                    // Check if this is a no house race (e.g. Grammar Gift)
                    if (currentEventObject.no_house == true)
                    {

                        // Set the lane number as the name
                        theScoreboard.nameLabelControlList[ledScoreboardNextSlotIndex].Text = "Lane " + resultObject.lane;

                        // Set all the text to be white (due to black background)
                        var textColor = System.Drawing.Color.White;
                        theScoreboard.placeLabelControlList[ledScoreboardNextSlotIndex].ForeColor = textColor;
                        theScoreboard.nameLabelControlList[ledScoreboardNextSlotIndex].ForeColor = textColor;
                        theScoreboard.timeLabelControlList[ledScoreboardNextSlotIndex].ForeColor = textColor;

                    }

                    // This is a standard house race
                    else
                    {

                        // House details
                        var houseName = laneList[laneIndex].house_name;
                        var houseColor = laneList[laneIndex].house_color;
                        var textColor = System.Drawing.ColorTranslator.FromHtml("#" + laneList[laneIndex].text_color);

                        // Set the house name
                        theScoreboard.nameLabelControlList[ledScoreboardNextSlotIndex].Text = houseName;

                        // Set the panel colors
                        theScoreboard.panelControlList[ledScoreboardNextSlotIndex].BackColor = System.Drawing.ColorTranslator.FromHtml("#" + houseColor);

                        // Set the label colors
                        theScoreboard.placeLabelControlList[ledScoreboardNextSlotIndex].ForeColor = textColor;
                        theScoreboard.nameLabelControlList[ledScoreboardNextSlotIndex].ForeColor = textColor;
                        theScoreboard.timeLabelControlList[ledScoreboardNextSlotIndex].ForeColor = textColor;

                    }

                    // Increment ledScoreboardNextSlotIndex
                    ledScoreboardNextSlotIndex++;

                }                

            }

        }

        private void lockOntoFileFromBackground(string fileName)
        {

            if (btnFileLockOn.InvokeRequired)
            {
                LockOntoFileFromBackground d = new LockOntoFileFromBackground(lockOntoFileFromBackground);
                this.Invoke(d, new object[] { fileName });
            }
            else
            {
                currentRaceFileLockedOn = fileName;
                btnFileLockOn.Text = (fileName == null ? "Not Locked On" : "Locked On To " + fileName);
                btnFileLockOn.Enabled = (fileName == null ? false : true);
                btnFileLockOn.BackColor = (fileName == null ? Color.DarkOrange : Color.Green);
            }

        }

        private void btnFileLockOn_Click(object sender, EventArgs e)
        {

            if (MessageBox.Show("This will unlock from " + currentRaceFileLockedOn + ". Are you sure?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                lockOntoFileFromBackground(null);
            }

        }

        private void btnResetScoreboard_Click(object sender, EventArgs e)
        {

            if (MessageBox.Show("This will reset and clear the scoreboard back to the beginning of the current event. Are you sure?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {

                // Reset everything - reload the same event again but do NOT release the lock on the file, since a rerun uses the same file!
                if (currentEventObject != null)
                    changeEvent(currentEventObject, false);

            }

        }

        private void btnHookRHQStopwatch_Click(object sender, EventArgs e)
        {

            if (MessageBox.Show("Make sure you have the Race HQ window open with the stopwatch visible before proceeding.\n\nNote, this operation will lock up the user interface temporarily.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {

                // Log
                logEntry("Searching for the stopwatch in the Race HQ window");

                // Find the RHQ timing window
                IntPtr hWnd = FindWindow("TrHQPackHQForm", null);

                int scanStart = 0;
                int scanEnd = 10000000; // Arbitrary, pretty much will iterate through all controls on the hWnd form
                bool found = false;
                int scanNum = scanStart;

                while (scanNum < scanEnd && !found)
                {

                    // Find the control window that has the text
                    IntPtr hEdit = GetDlgItem(hWnd, scanNum);

                    // Initialize the buffer to store the text for the control on each iteration
                    System.Text.StringBuilder sb = new System.Text.StringBuilder(255);

                    // Get the text from the child control
                    int RetVal = SendMessage(hEdit, WM_GETTEXT, sb.Capacity, sb);

                    long style = 0;

                    // Check the control is visible (there seem to be a lot of hidden controls in the RHQ window)
                    const int GWL_STYLE = -16;
                    const int WS_VISIBLE = 0x10000000;
                    style = GetWindowLong(hEdit, GWL_STYLE);
                    bool visible = (style & WS_VISIBLE) == WS_VISIBLE;

                    // Check the control contains ":" which indicates it is the stopwatch
                    if (sb.ToString().Contains(":") && visible)
                    {
                        found = true;

                        // Update the scoreboard
                        theScoreboard.changeStopwatch(sb.ToString());

                        hWnd = IntPtr.Zero;
                    }

                    scanNum += 1;

                }

                // Hook has been found
                if (found == true)
                {

                    rhqStopwatchID = scanNum - 1;

                    // Start the updates
                    tmrRHQStopwatchUpdate.Enabled = true;

                    logEntry("Success, stopwatch found and hooked");
                    logEntry("----");

                }

                // Hook has not been found
                else
                {
                    logEntry("Failed, stopwatch not found");
                    logEntry("----");
                    MessageBox.Show("Cannot locate the stopwatch in Race HQ.", "Error");
                }

            }

        }

        const int WM_GETTEXT = 0x0D;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SendMessage(IntPtr hWnd, int msg, int Param, System.Text.StringBuilder text);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private void tmrRHQStopwatchUpdate_Tick(object sender, EventArgs e)
        {

            // Find the RHQ timing window
            IntPtr hWnd = FindWindow("TrHQPackHQForm", null);

            // Find the control window that has the text
            IntPtr hEdit = GetDlgItem(hWnd, rhqStopwatchID);

            // Initialize the buffer to store the text for the control on each iteration
            System.Text.StringBuilder sb = new System.Text.StringBuilder(255);

            // Get the text from the child control
            int RetVal = SendMessage(hEdit, WM_GETTEXT, sb.Capacity, sb);

            // Check the control contains ":" which indicates it is the stopwatch
            if (sb.ToString().Contains(":"))
            {
                // Update the scoreboard
                theScoreboard.changeStopwatch(sb.ToString().Substring(truncateTimes ? 3 : 0));

                hWnd = IntPtr.Zero;
            }

        }

        private void btnShowScoreboard_Click(object sender, EventArgs e)
        {

            // Update the flag
            theScoreboardVisible = !theScoreboardVisible;

            // Update the button
            btnShowScoreboard.Text = (theScoreboardVisible ? "Hide Scoreboard" : "Show Scoreboard");
            btnShowScoreboard.BackColor = (theScoreboardVisible ? Color.Green : Color.Red);
            btnHideScoreboardTopBar.Enabled = theScoreboardVisible;

            // Show or hide the scoreboard
            if (theScoreboardVisible)
            {
                theScoreboard.Show();
            }
            else
            {
                theScoreboard.Hide();
                theScoreboard.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
                btnHideScoreboardTopBar.Text = "Hide Scoreboard Top Bar";
                btnHideScoreboardTopBar.BackColor = Color.Red;
            }

        }

        private void btnHideScoreboardTopBar_Click(object sender, EventArgs e)
        {

            if (theScoreboard.FormBorderStyle == System.Windows.Forms.FormBorderStyle.FixedSingle)
            {
                theScoreboard.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                btnHideScoreboardTopBar.Text = "Show Scoreboard Top Bar";
                btnHideScoreboardTopBar.BackColor = Color.Green;
            }
            else
            {
                theScoreboard.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
                btnHideScoreboardTopBar.Text = "Hide Scoreboard Top Bar";
                btnHideScoreboardTopBar.BackColor = Color.Red;
            }

        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (closeCancel() == false)
            {
                e.Cancel = true;
            };
        }

        // Confirm whether to quit
        public static bool closeCancel()
        {
            var result = MessageBox.Show("Are you sure that you want to quit?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

            if (result == DialogResult.Yes)
                return true;
            else
                return false;
        }

        private void logEntry(string logEntryString)
        {

            var time = DateTime.Now;
            var timestamp = time.ToString("HH:mm:ss");

            // This makes the logging thread safe since this method may be called from the file watcher which is run on a background thread
            if (lstLogBox.InvokeRequired)
            {
                AppendLogReturningVoidDelegate d = new AppendLogReturningVoidDelegate(logEntry);
                this.Invoke(d, new object[] { logEntryString });
            }
            else
            {
                // Add the text
                lstLogBox.Items.Add(timestamp.ToString() + ": " + logEntryString);

                // Keep it scrolled to the bottom
                lstLogBox.TopIndex = lstLogBox.Items.Count - 1;
            }

        }

    }

    public class Lane
    {
        public int lane { get; set; }
        public string house_code { get; set; }
        public string house_name { get; set; }
        public string house_color { get; set; }
        public string text_color { get; set; }
    }

    public class Event
    {

        public string event_id { get; set; }        
        public string event_name { get; set; }
        public string gender { get; set; }
        public bool is_gated { get; set; }
        public bool no_house { get; set; }

    }

    public sealed class EventMap: ClassMap<Event>
    {
        public EventMap()
        {
            Map(m => m.event_id).Index(0);
            Map(m => m.event_name).Index(1);
            Map(m => m.gender).Index(2);
            Map(m => m.is_gated).Index(3);
            Map(m => m.no_house).Index(4);
        }
    }

    public class Result
    {
        public int lane { get; set; }
        public string race_time { get; set; }
        public int race_place { get; set; }
    }

}