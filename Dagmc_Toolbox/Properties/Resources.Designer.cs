﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Dagmc_Toolbox.Properties {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "16.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Dagmc_Toolbox.Properties.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &lt;?xml version=&quot;1.0&quot; encoding=&quot;utf-8&quot; ?&gt;&lt;AddIns&gt;&lt;AddIn name=&quot;Sample Add-In&quot; description=&quot;Loaded from a journal file.&quot; assembly=&quot;Samples\V16\SampleAddIn\SampleAddIn.dll&quot; typename=&quot;SpaceClaim.Api.V19.Examples.SampleAddIn&quot; host=&quot;SameAppDomain&quot; /&gt;&lt;/AddIns&gt;.
        /// </summary>
        internal static string AddInManifestInfo {
            get {
                return ResourceManager.GetString("AddInManifestInfo", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized resource of type System.Drawing.Bitmap.
        /// </summary>
        internal static System.Drawing.Bitmap CheckGeometry {
            get {
                object obj = ResourceManager.GetObject("CheckGeometry", resourceCulture);
                return ((System.Drawing.Bitmap)(obj));
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Check Geometry before export for CAE simulation.
        /// </summary>
        internal static string CheckGeometryHint {
            get {
                return ResourceManager.GetString("CheckGeometryHint", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Check Geometry.
        /// </summary>
        internal static string CheckGeometryText {
            get {
                return ResourceManager.GetString("CheckGeometryText", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized resource of type System.Drawing.Bitmap.
        /// </summary>
        internal static System.Drawing.Bitmap CreateGroup {
            get {
                object obj = ResourceManager.GetObject("CreateGroup", resourceCulture);
                return ((System.Drawing.Bitmap)(obj));
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Create a DAGMC group from selected geometry.
        /// </summary>
        internal static string CreateGroupHint {
            get {
                return ResourceManager.GetString("CreateGroupHint", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Creat Group.
        /// </summary>
        internal static string CreateGroupText {
            get {
                return ResourceManager.GetString("CreateGroupText", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized resource of type System.Drawing.Bitmap.
        /// </summary>
        internal static System.Drawing.Bitmap ExportDagmc {
            get {
                object obj = ResourceManager.GetObject("ExportDagmc", resourceCulture);
                return ((System.Drawing.Bitmap)(obj));
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Export geometry and surface mesh in MOAB file format.
        /// </summary>
        internal static string ExportDagmcHint {
            get {
                return ResourceManager.GetString("ExportDagmcHint", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Dagmc Export.
        /// </summary>
        internal static string ExportDagmcText {
            get {
                return ResourceManager.GetString("ExportDagmcText", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to  .
        /// </summary>
        internal static string PartGroupText {
            get {
                return ResourceManager.GetString("PartGroupText", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized resource of type System.Drawing.Bitmap.
        /// </summary>
        internal static System.Drawing.Bitmap PrintUid {
            get {
                object obj = ResourceManager.GetObject("PrintUid", resourceCulture);
                return ((System.Drawing.Bitmap)(obj));
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Print solid geometry hash.
        /// </summary>
        internal static string PrintUidHint {
            get {
                return ResourceManager.GetString("PrintUidHint", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Hash ID.
        /// </summary>
        internal static string PrintUidText {
            get {
                return ResourceManager.GetString("PrintUidText", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &lt;?xml version=&quot;1.0&quot; encoding=&quot;utf-8&quot;?&gt;
        ///&lt;customUI xmlns=&quot;http://schemas.spaceclaim.com/customui&quot;&gt;
        ///	&lt;ribbon&gt;
        ///		&lt;tabs&gt;
        ///			&lt;tab id=&quot;Dagmc_Toolbox.C#.V18.RibbonTab&quot; command=&quot;Dagmc_Toolbox.C#.V18.RibbonTab&quot;&gt;
        ///				&lt;!--
        ///					For the &apos;tab&apos; and &apos;group&apos; elements, you can either specify a &apos;label&apos; attribute, or you can
        ///					specify a &apos;command&apos; attribute.  The &apos;command&apos; attribute gives the name of a command that you
        ///					create, whose Text property will be used for the label.  This approach allows for localization
        ///					si [rest of string was truncated]&quot;;.
        /// </summary>
        internal static string Ribbon {
            get {
                return ResourceManager.GetString("Ribbon", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Dagmc Toolkit.
        /// </summary>
        internal static string RibbonTabText {
            get {
                return ResourceManager.GetString("RibbonTabText", resourceCulture);
            }
        }
    }
}
