﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Diagnostics;
using SpaceClaim.Api.V19.Extensibility;
using SpaceClaim.Api.V19.Geometry;
using SpaceClaim.Api.V19.Modeler;
using System.Xml.Serialization;
using System.Windows.Forms;
using SpaceClaim.Api.V19;
using Point = SpaceClaim.Api.V19.Geometry.Point;

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
        static public List<T> GatherAllEntities<T>(IPart part) where T : DocObject
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

        static public ICollection<T> GatherSelectedObjects<T>(InteractionContext context) where T : DocObject
        {
            var docObjects = new List<T>();
            var selection = context.Selection;
            foreach (DocObject o in selection)
            {
                if ((T)o != null)
                    docObjects.Add((T)o);
            }
            return docObjects;
        }

        static public ICollection<Group> GatherAllGroups(IPart part = null)
        {
            //if (part != null)
            //{
            //    part.Document.Wind
            //}
            return Window.ActiveWindow.Groups;
        }

        static public Group SelectGroup(string existingGroupName)
        {
            foreach (var group in GatherAllGroups())
            {
                if (group.Name == existingGroupName)
                {
                    return group;
                }
            }
            return null;
        }

        static public bool RemoveGroup(string existingGroupName)
        {
            SpaceClaim.Api.V19.Scripting.Commands.NamedSelection.Delete(existingGroupName);
            return true;
        }

        static public void ReportMessage(string message)
        {
            // SpaceClaim.exe may only accessible by IronPython
            //SpaceClaim.UserInterface.Session.Current.StatusQueue.ReportStatus(message, StatusMessageType.Information);
        }

        static public bool AppendToGroup(ICollection<IDocObject> objects, string existingGroupName)
        {
            var group = SelectGroup(existingGroupName);
            string tmpGroupName = "__tmpGroupToAadd";
            if (group != null)
            {
                foreach (var o in group.Members)
                    objects.Add(o);
                // group.Members is fixed sized collection, can not be added
                var g = Group.Create(Helper.GetActiveMainPart(), tmpGroupName, objects);
                if (!RemoveGroup(existingGroupName))
                {
                    Debug.WriteLine("Failed to remove a group with name {}", existingGroupName);
                    group.Name = "__groupFailedToRemove(try_manually)";
                }
                g.Name = existingGroupName;
                return true;
            }
            return false;
        }

        static public void ColorizeGroup(Group group, Color color)
        {
            foreach(var o in group.Members)
            {
                if(o is IHasColor)
                    ((IHasColor)o).SetColor(null, color);
            }
        }

        //============================== remove below if not in use later =================================

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
