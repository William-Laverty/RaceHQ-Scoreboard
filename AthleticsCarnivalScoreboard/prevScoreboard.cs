using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using AthleticsCarnivalScoreboard;

#pragma warning disable IDE1006
#pragma warning disable IDE0017

namespace AthleticsCarnivalScoreboard
{
    public partial class prevScoreboard : Form
    {

        // The control list for the lane panels shown on screen
        public List<Panel> panelControlList { get; set; }

        // The control lists for all the lane and competitor information labels shown on screen
        public List<Label> placeLabelControlList { get; set; }
        public List<Label> nameLabelControlList { get; set; }
        public List<Label> timeLabelControlList { get; set; }

        // Flag indicating whether to truncate times (i.e. remove the 10 minutes and above section)
        public Boolean truncateTimes = false;

        // Thread-safe delegates
        delegate void ClearScoreboardFullFromBackground();
        delegate void ClearScoreboardTimesOnlyFromBackground();

        public prevScoreboard()
        {
            InitializeComponent();
        }

        private void Scoreboard_Load(object sender, EventArgs e)
        {
           
            // Populate all the control lists: (list index) + 1 = lane number

            // Panels
            panelControlList = new List<Panel>() { pnlPlace1, pnlPlace2, pnlPlace3, pnlPlace4, pnlPlace5, pnlPlace6, pnlPlace7, pnlPlace8, pnlPlace9, pnlPlace10 };

            // Lane labels
            placeLabelControlList = new List<Label>() { lblPlace1, lblPlace2, lblPlace3, lblPlace4, lblPlace5, lblPlace6, lblPlace7, lblPlace8, lblPlace9, lblPlace10 };

            // Name labels
            nameLabelControlList = new List<Label>() { lblPlace1Name, lblPlace2Name, lblPlace3Name, lblPlace4Name, lblPlace5Name, lblPlace6Name, lblPlace7Name, lblPlace8Name, lblPlace9Name, lblPlace10Name };

            // Time labels
            timeLabelControlList = new List<Label>() { lblPlace1Time, lblPlace2Time, lblPlace3Time, lblPlace4Time, lblPlace5Time, lblPlace6Time, lblPlace7Time, lblPlace8Time, lblPlace9Time, lblPlace10Time };

            // Clear the scoreboard
            clearScoreboardFull();

            LoadPreviousRaceResults();

        }

        public void clearScoreboardFull()
        {

            if (lblEventHeading.InvokeRequired)
            {
                ClearScoreboardFullFromBackground d = new ClearScoreboardFullFromBackground(clearScoreboardFull);
                this.Invoke(d, new object[] { });
            }
            else
            {
                for (var i = 0; i < panelControlList.Count; i += 1)
                {
                    panelControlList[i].BackColor = Color.Black;
                    placeLabelControlList[i].Text = "";
                    nameLabelControlList[i].Text = "";
                    timeLabelControlList[i].Text = "";

                    lblEventHeading.Text = "";
                    lblStopwatch.Text = "00:00:00.0".Substring(truncateTimes ? 3 : 0);
                }
            }

        }

        public void clearScoreboardTimesOnly()
        {
            if (lblEventHeading.InvokeRequired)
            {
                ClearScoreboardTimesOnlyFromBackground d = new ClearScoreboardTimesOnlyFromBackground(clearScoreboardTimesOnly);
                this.Invoke(d, new object[] { });
            }
            else
            {
                for (var i = 0; i < panelControlList.Count; i += 1)
                {
                    panelControlList[i].BackColor = Color.Black;
                    placeLabelControlList[i].Text = "";
                    nameLabelControlList[i].Text = "";
                    timeLabelControlList[i].Text = "";
                }
            }
        }

        public string GetMostRecentRaceFile()
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string folderPath = Path.Combine(desktopPath, "PreviousResults");

            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show("The 'PreviousResults' folder does not exist on the desktop.");
                return null;
            }

            var csvFiles = Directory.GetFiles(folderPath, "*.csv");

            if (csvFiles.Length == 0)
            {
                MessageBox.Show("No CSV files found in the 'PreviousResults' folder.");
                return null;
            }

            return csvFiles.OrderByDescending(f => File.GetCreationTime(f)).FirstOrDefault();
        }

        public List<RaceResult> ParseCSVContent(string filePath)
        {
            List<RaceResult> results = new List<RaceResult>();
            string[] lines = File.ReadAllLines(filePath);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(',');
                results.Add(new RaceResult {
                    Lane = int.Parse(columns[0]),
                    Name = columns[1],
                    Time = columns[2],
                    Place = columns[3]
                });
            }
            return results;
        }

        public void DisplayResultsOnScoreboard(List<RaceResult> raceResults)
        {
            for (int i = 0; i < raceResults.Count; i++)
            {
                nameLabelControlList[i].Text = raceResults[i].Name;
                timeLabelControlList[i].Text = raceResults[i].Time;
                placeLabelControlList[i].Text = raceResults[i].Place;
            }
        }

        public void LoadPreviousRaceResults()
        {
            string mostRecentRaceFile = GetMostRecentRaceFile();

            if (!string.IsNullOrEmpty(mostRecentRaceFile))
            {
                List<RaceResult> raceResults = ParseCSVContent(mostRecentRaceFile);
                DisplayResultsOnScoreboard(raceResults);
            }
        }

        public void changeHeaderLabel(string s)
        {
            lblEventHeading.Text = s;
        }

        public void changeStopwatch(string s)
        {
            lblStopwatch.Text = s;
        }

        private void lblEventHeading_Click(object sender, EventArgs e)
        {

        }
    }
}
