using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using SpaceClaim.Api.V18.Extensibility;
using SpaceClaim.Api.V18.Geometry;
using SpaceClaim.Api.V18.Modeler;
using SpaceClaim.Api.V18;
using SpaceClaim.Api.V18.Display;
using Point = SpaceClaim.Api.V18.Geometry.Point;
using SpaceClaim.Api.V18.Scripting;
using Dagmc_Toolbox.Properties;

namespace Dagmc_Toolbox.Commands
{
    //class ScriptClass : 

    class PrintUid : CommandCapsule
    {
        // This command name must match that in the Ribbon.xml file
        //----------------------------------------------------------
        public bool first = true;
        public string ScriptRelPath = @"PythonScripts\PrintUid.scsript";
        public const string CommandName = "CCFE_Toolbox.C#.V18.PrintUid";

        public PrintUid() : base(CommandName, Resources.PrintUidText, Resources.PrintUid, Resources.PrintUidHint)
        {

        }

        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect)
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var scriptPath = Path.Combine(assemblyDir, ScriptRelPath);
            if (File.Exists(scriptPath))
            {
                // Variables
                Window window = Window.ActiveWindow;
                Document doc = window.Document;
                Part rootPart = doc.MainPart;

                // To pass args to python script
                var scriptParams = new Dictionary<string, object>();
                //scriptParams.Add("iter", iterations);
                //scriptParams.Add("mf", maxfaces);

                // Run the script
                SpaceClaim.Api.V18.Application.RunScript(scriptPath, scriptParams);
                MessageBox.Show($"ERROR: Script file {scriptPath} called successfully");
            }
            else
            {
                MessageBox.Show($"ERROR: Script file {scriptPath} is not found.");
            }
        }
    }
}