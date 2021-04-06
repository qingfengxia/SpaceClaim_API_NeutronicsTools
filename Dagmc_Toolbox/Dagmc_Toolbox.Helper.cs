using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Diagnostics;
using SpaceClaim.Api.V18.Extensibility;
using SpaceClaim.Api.V18.Geometry;
using SpaceClaim.Api.V18.Modeler;
using System.Xml.Serialization;
using System.Windows.Forms;
using SpaceClaim.Api.V18;
using Point = SpaceClaim.Api.V18.Geometry.Point;

using Dagmc_Toolbox.Properties;

namespace Dagmc_Toolbox
{
    public partial class Helper
    {
        static public Part GetActiveMainPart()
        {
            Window window = Window.ActiveWindow;
            window.InteractionMode = InteractionMode.Solid;
            Document doc = window.Document;
            //doc.MainPart.ShareTopology = true;  // only affect exporting to CAE analysis
            //doc.MainPart.GetDescendants<Group>();  // group belongs to Part, a collection of DocObject
            return doc.MainPart;
        }
        static public List<T> GatherAllEntities<T>(IPart part) where T: DocObject
        {
            var allEntities = new List<T>();
            allEntities.AddRange(part.GetDescendants<T>());
            return allEntities;
        }
        
        static public List<IDesignBody> GatherAllVisibleBodies(IPart part, Window window)
        {
            List<IDesignBody> allBodies = new List<IDesignBody>();
            var tempList = new List<IDesignBody>();
            tempList.AddRange(part.GetDescendants<IDesignBody>());
            foreach (IDesignBody body in tempList)
            {
                if (CheckIfVisible(body, window))
                {
                    allBodies.Add(body);
                }
            }
            return allBodies;
        }

        static public List<IDesignBody> GatherSelectionBodies(InteractionContext context )
        {
            var iBodies = new List<IDesignBody>();
            var selection = context.Selection;
            foreach (IDesignBody body in selection)
            {
                iBodies.Add(body);
            }
            return iBodies;
        }

        static public List<Body> CopyIDesign(List<IDesignBody> iDesBods)
        {
            var bodies = new List<Body>();
            foreach (IDesignBody bod in iDesBods)
            {
                DesignBody master = bod.Master;
                Matrix masterTrans = bod.TransformToMaster;
                Matrix reverseTrans = masterTrans.Inverse;
                Body copy = master.Shape.Copy();
                copy.Transform(reverseTrans);
                bodies.Add(copy);
            }

            return bodies;
        }

        static public List<Body> CopyIDesignAndTransforms(List<IDesignBody> iDesBods, out List<Matrix> transforms)
        {
            transforms = new List<Matrix>();
            var bodies = new List<Body>();
            foreach (IDesignBody bod in iDesBods)
            {
                DesignBody master = bod.Master;
                Matrix masterTrans = bod.TransformToMaster;
                transforms.Add(masterTrans);
                Matrix reverseTrans = masterTrans.Inverse;
                Body copy = master.Shape.Copy();
                copy.Transform(reverseTrans);
                bodies.Add(copy);
            }

            return bodies;
        }

        static public Body CreateRectBody(Part part, double length, double width, double height, PointUV UVPoint, Plane plane)
        {
            Debug.Assert(part != null, "part != null");
            Body body = Body.ExtrudeProfile(new RectangleProfile(plane, length, width, UVPoint), height);
            return body;
        }

        static public void FileWriter(string path, string words)
        {
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(words);
            }
        }

        static public void ColorFace(Face face, DesignBody desBody)
        {
            var desFace = desBody.GetDesignFace(face);
            desFace.SetColor(null, Color.Magenta);
        }

        static public void SetFaceTranslucent (Face face, DesignBody desBody)
        {
            // Get face current colour
            var bodyColour = desBody.GetColor(null);
            var otherColour = Color.FromArgb(173, bodyColour.Value.R, bodyColour.Value.G, bodyColour.Value.B);
            var desFace = desBody.GetDesignFace(face);
            desFace.SetColor(null, otherColour);
        }

        static public bool CheckIfVisible (IHasVisibility obj, Window window)
        {
            var context = window.Scene as IAppearanceContext;
            return obj.IsVisible(context);
        }

        static public List<Moniker<IDesignBody>> ReturnMonikers (List<IDesignBody> iBodies)
        {
            List<Moniker<IDesignBody>> monikers = new List<Moniker<IDesignBody>>();
            foreach(IDesignBody iBody in iBodies)
            {
                monikers.Add(iBody.Moniker);
            }
            return monikers;
        }


    }
}
