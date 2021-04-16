using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using SpaceClaim.Api.V19.Extensibility;
using SpaceClaim.Api.V19.Geometry;
using SpaceClaim.Api.V19.Modeler;
using System.Xml.Serialization;
using System.Windows.Forms;
using SpaceClaim.Api.V19;
using Point = SpaceClaim.Api.V19.Geometry.Point;
using System.Diagnostics;


using Dagmc_Toolbox.Properties;

namespace Dagmc_Toolbox.Commands
{
    class ExportDagmc : CommandCapsule
    {
        // This command name must match that in the Ribbon.xml file
        //----------------------------------------------------------

        public const string CommandName = "Dagmc_Toolbox.C#.V18.ExportDagmc";

        public ExportDagmc() : base(CommandName, Resources.ExportDagmcText, Resources.ExportDagmc, Resources.ExportDagmcHint)
        {
        }
        
        protected override void OnUpdate(Command command)
        {
            command.IsEnabled = Window.ActiveWindow != null;
        }
        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect)
        {
            Debug.Assert(Window.ActiveWindow != null, "Window.ActiveWindow != null");
            /* 
             * Export objects to an .sat file depending upon their colour and material                          
            */

            // User selects the file destination to save
            //----------------------------------------------------------

            MessageBox.Show("ExportDagmc called");

            var data = ExportDagmcData.FromString(context.Data);
            if (data == null)
            {
                data = PromptForData();
                context.Data = data.ToString(); // store the data to be used for journal replay
            }

            if (string.IsNullOrEmpty(data.FileName))
                return; // user canceled

            // Window.ActiveWindow.Export(data.Format, data.FileName);

            // Declaration of SpaceClaim variables
            //----------------------------------------------------------

            Window window = Window.ActiveWindow;
            window.InteractionMode = InteractionMode.Solid;
            Document doc = window.Document;
            Part rootPart = doc.MainPart;
            ExportOptions options;
            options = null;
            string tempString = "_";
            //char[] delimiterChars = { '.' };  


            // Declaration of other variables
            //----------------------------------------------------------

            List<Nullable<Color>> bodyColours = new List<Nullable<Color>>();
            var allBodies = new List<IDesignBody>();
            var totalBodies = new List<DesignBody>();
            bool firstObject = true;
            var masterTranslations = new List<Matrix>();
            Nullable<Color> newColor = new Nullable<Color>();


            // Call upon GatherBodies Method  
            //----------------------------------------------------------

            GatherBodies(rootPart, allBodies, window);

            // Find the master of each IDesignBody (DesignBodies)  
            //----------------------------------------------------------

            foreach (IDesignBody temp in allBodies)
            {
                DesignBody bod = temp.Master;
                totalBodies.Add(bod);
                masterTranslations.Add(temp.TransformToMaster);
            }


            // Produce file to write found colours to
            //----------------------------------------------------------

            /*
            string colourString;
            string path = "D:/A_Burns/Body_Colour_Data.txt";
            using (StreamWriter sw = File.CreateText(path))
            {
                sw.WriteLine("Colours of Bodies in SpaceClaim");
            }
            */



            // Create a list of each unique colour in SpaceClaim Window
            //----------------------------------------------------------

            foreach (DesignBody body in totalBodies)
            {

                bool colorPresent = false;
                newColor = body.GetColor(null);
                try
                {
                    var temp = body.GetColor(null).Value.R;
                }
                catch
                {
                    body.SetColor(null, Color.FromArgb(255, 143, 175, 143));
                }

                if (firstObject)
                {
                    bodyColours.Add(newColor);
                    firstObject = false;
                }
                else
                {
                    for (int j = 0; j < bodyColours.Count; j++)
                    {
                        if (newColor == bodyColours[j])
                        {
                            colorPresent = true;
                        }
                    }
                    if (colorPresent == false)
                    {
                        bodyColours.Add(newColor);
                    }
                }

            }


            // Write colours to file
            //----------------------------------------------------------

            /*
            int tempCounter = 0;
            foreach (Nullable<Color> colour in bodyColours)
            {
                string tempNumber;
                string fullString;
                colourString = colour.ToString();
                tempNumber = tempCounter.ToString();
                fullString = tempNumber + "    " + colourString;
                WriteFile(path, fullString);
                tempCounter++;
            }
            */




            // Iterate through colours exporting bodies of same colour
            //----------------------------------------------------------

            int bodCounter = 0;

            for (int k = 0; k < bodyColours.Count; k++)
            {
                bodCounter = 0;
                IPart party = doc.MainPart;
                Nullable<Color> tempColor = new Nullable<Color>();
                tempColor = bodyColours[k];

                foreach (DesignBody body in totalBodies)
                {

                    if (body.GetColor(null) != tempColor)
                    {
                        //body.SetVisibility(null, false);
                        allBodies[bodCounter].SetVisibility(null, false);
                    }
                    bodCounter++;
                }

                // Export to .sat format to path specified by user
                //----------------------------------------------------------
                try
                {
                    tempString += tempColor.Value.A.ToString() + "_"  + tempColor.Value.R.ToString() + "_" + tempColor.Value.G.ToString() + "_" + tempColor.Value.B.ToString();
                }
                catch
                {
                    tempString += "255_143_175_143";
                }
                //string tempString = k.ToString();
                string tempFilename = data.FileName;
                
                string matFiller = "_ARGB";

                int fin = tempFilename.LastIndexOf('.');
                string leftHS = fin < 0 ? tempFilename : tempFilename.Substring(0, fin),
                    rightHS = fin < 0 ? "" : tempFilename.Substring(fin + 1);

                //WriteFile("D:/A_Burns/path_test.txt", leftHS);
                //WriteFile("D:/A_Burns/path_test.txt", rightHS);

                string[] words = tempFilename.Split('.');
                string newPath = words[0];
                int wordsLength = words.Length;
                for (int w = 1; w < wordsLength - 1; w++)
                {
                    newPath += "_";
                    newPath += words[w];
                } 
                
                string finalPath = leftHS + matFiller + tempString + '.' + rightHS;

                //WriteFile("D:/A_Burns/path_test.txt", finalPath);

                rootPart.Export(data.Format, finalPath, true, options);

                tempString = "_";
                foreach (IDesignBody bodies in allBodies)
                {
                    bodies.SetVisibility(null, true);

                }

            }
        }


        // Method: Appends all IDesignBodies to a List
        //----------------------------------------------------------

        static void GatherBodies(IPart part, List<IDesignBody> allBodies, Window window)
        {
            var tempList = new List<IDesignBody>();
            tempList.AddRange(part.GetDescendants<IDesignBody>());
            foreach(IDesignBody bod in tempList)
            {
                if (IsVisible(bod, window))
                {
                    allBodies.Add(bod);
                }
            }
            
        }


        // Method: Writes string to file
        //----------------------------------------------------------

        static void WriteFile(string path, string words)
        {
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(words);
            }
        }


        // Prompt the user for the location to save the file
        //----------------------------------------------------------

        static ExportDagmcData PromptForData()
        {
            PartExportFormat exportFormat = PartExportFormat.AcisText;
            string fileName = null;

            AddIn.ExecuteWindowsFormsCode(() => {
                using (var fileDialog = new SaveFileDialog())
                {
                    var formats = new List<PartExportFormat>();

                    // add the supported file formats to the SaveAs dialog
                    string filter = string.Empty;
                    foreach (PartExportFormat format in Enum.GetValues(typeof(PartExportFormat)))
                    {
                        string formatFilter = GetFilter(format);
                        if (!string.IsNullOrEmpty(formatFilter))
                        {
                            filter += formatFilter + "|";
                            formats.Add(format);
                        }
                    }
                    filter = filter.TrimEnd('|');
                    fileDialog.Filter = filter;

                    if (fileDialog.ShowDialog(SpaceClaim.Api.V19.Application.MainWindow) != DialogResult.OK)
                        return; // user canceled

                    // get the data the user entered
                    exportFormat = formats[fileDialog.FilterIndex - 1];
                    fileName = fileDialog.FileName;
                }
            });

            return new ExportDagmcData(exportFormat, fileName);
        }


        // Different Filters for the user to choose from
        //----------------------------------------------------------

        static string GetFilter(PartExportFormat exportFormat)
        {
            switch (exportFormat)
            {
                case PartExportFormat.AcisText:
                    return "SAT files (*.sat)|*.sat";
                default:
                    return null;
            }
        }


        // Method: Misses out invisible objects
        //----------------------------------------------------------

        static bool IsVisible(IHasVisibility obj, Window window)
        {
            var context = window.Scene as IAppearanceContext;
            return obj.IsVisible(context);
        }


    }


    // Class of export properties 
    //----------------------------------------------------------

    public class ExportDagmcData : CommandData<ExportDagmcData>
    {
        public ExportDagmcData(PartExportFormat format, string filename)
        {
            Format = format;
            FileName = filename;
        }

        protected ExportDagmcData()
        {
            // Serialization
        }

        [XmlElement(ElementName = "File")]
        public string FileName { get; set; }

        [XmlElement(ElementName = "Format")]
        public PartExportFormat Format { get; set; }
    }

}
