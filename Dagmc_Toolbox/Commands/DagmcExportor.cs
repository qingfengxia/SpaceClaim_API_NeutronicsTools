using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using System.Windows.Forms;
using SpaceClaim.Api.V19.Extensibility;
using SpaceClaim.Api.V19.Geometry;
using SpaceClaim.Api.V19.Modeler;
using SpaceClaim.Api.V19;
using SpaceClaim.Api.V19.Scripting;
using SpaceClaim.Api.V19.Scripting.Commands;
using Point = SpaceClaim.Api.V19.Geometry.Point;

using Moab = MOAB.Moab;
using static MOAB.Constants;
using static MOAB.Moab.Core;
using message = System.Diagnostics.Debug;
/// EntityHandle depends on C++ build configuration and 64bit or 32bit, see MOAB's header <EntityHandle.hpp>
using EntityHandle = System.UInt64;   // type alias is only valid in the source file!

/// type alias to help Cubit/Trelis developers to understand SpaceClaim API
///using RefEntity = SpaceClaim.Api.V19.Geometry.IShape;
using RefGroup = SpaceClaim.Api.V19.Group;   /// Group is not derived from Modeler.Topology
using RefEntity = SpaceClaim.Api.V19.Modeler.Topology;
// RefVolume has no mapping in SpaceClaim
using RefBody = SpaceClaim.Api.V19.Modeler.Body;
using RefFace = SpaceClaim.Api.V19.Modeler.Face;
using RefEdge = SpaceClaim.Api.V19.Modeler.Edge;
using RefVertex = SpaceClaim.Api.V19.Modeler.Vertex;
using Primitive = System.Double;
using SpaceClaim.Api.V19.Scripting.Commands.CommandOptions;

/// RefEntity:  ref to CubitEntity 
//typedef std::map<RefEntity*, moab::EntityHandle> refentity_handle_map;
//typedef std::map<RefEntity*, moab::EntityHandle>::iterator refentity_handle_map_itor;   // not needed in C#


namespace Dagmc_Toolbox
{
    /// hash map from the base class for all Geometry type (reference class type) to EntityHandle (ulong)
    using RefEntityHandleMap = Dictionary<RefEntity, EntityHandle>;
    using GroupHandleMap = Dictionary<RefGroup, EntityHandle>;

    /// tuple needs C#7.0 and netframework 4.7
    /// using entities_tuple = System.Tuple<List<RefVertex>, List<RefEdge>, List<RefFace>, List<RefBody>, List<RefGroup>>;

    /// class that export MOAB mesh
    internal class DagmcExporter
    {
        Moab.Core myMoabInstance = null;
        Moab.GeomTopoTool myGeomTool = null;
        const bool make_watertight = false;  // no such C# binding

        int norm_tol;
        double faceting_tol;
        double len_tol;
        bool verbose_warnings;
        bool fatal_on_curves;

        int failed_curve_count;
        List<int> failed_curves;
        int curve_warnings;

        int failed_surface_count;
        List<int> failed_surfaces;

        /// <summary>
        /// in C++, all these Tag, typedef of TagInfo*, initialized to zero (nullptr)
        /// </summary>
        Moab.Tag geom_tag = new Moab.Tag();
        Moab.Tag id_tag = new Moab.Tag();
        Moab.Tag name_tag = new Moab.Tag();
        Moab.Tag category_tag = new Moab.Tag();
        Moab.Tag faceting_tol_tag = new Moab.Tag();
        Moab.Tag geometry_resabs_tag = new Moab.Tag();

        // todo  attach file log to Debug/Trace to get more info from GUI
        static readonly EntityHandle UNINITIALIZED_HANDLE = 0;

        /// GEOMETRY_RESABS - If the distance between two points is less 
        /// than GEOMETRY_RESABS the points are considered to be identical.
        readonly double GEOMETRY_RESABS = 1.0E-6;  /// Trelis SDK /utl/GeometryDefines.h

        /// Topology related functions, to cover the difference between Cubit and SpaceClaim
        #region TopologyMap
        readonly string[] GEOMETRY_NAMES = { "Vertex", "Curve", "Surface", "Volume", "Group"};
        /// <summary>
        /// NOTE const instead of readonly, because const int is needed in switch case loop
        /// </summary>
        const int VERTEX_INDEX = 0;
        const int CURVE_INDEX = 1;
        const int SURFACE_INDEX = 2;
        const int VOLUME_INDEX = 3;
        //const int GROUP_INDEX = 4;
        /// <summary>
        /// Group and other topology types do not derived from the same RefEntity base class! but System.Object
        /// consider:  split out Group, then user type-safer `List<RefEntity>[] TopologyEntities;  `
        /// </summary>
        List<RefEntity>[] TopologyEntities;
        List<RefGroup> GroupEntities;

        /// <summary>
        /// private helper function to initialize the TopologyEntities data structure
        /// assuming all topology objects are within the ActivePart in the ActiveDocument
        /// </summary>
        /// <returns></returns>
        private void GenerateTopologyEntities()
        {
            Part part = Helper.GetActiveMainPart();  // todo: can a document have multiple Parts?
            var allBodies = Helper.GatherAllEntities<DesignBody>(part);
            List<RefEntity> bodies = allBodies.ConvertAll<RefEntity>(o => o.Shape);
            foreach(var b in allBodies)
            {
                BodyToDesignBodyMap[b.Shape] = b;
            }

            // todo:  there is anther way to get all Faces, adding all Faces of body together,
            // needs unit test to check the diff, and face count. 
            List<RefEntity> surfaces = Helper.GatherAllEntities<DesignFace>(part).ConvertAll<RefEntity>(o => o.Shape);
            List<RefEntity> edges = Helper.GatherAllEntities<DesignEdge>(part).ConvertAll<RefEntity>(o => o.Shape);
            List<RefEntity> vertices = new List<RefEntity>();  // There is no DesignVertex class
            foreach(var e in edges)
            {
                vertices.AddRange(GetEdgeVertices((RefEdge)e));
            }

            // Helper.GatherAllEntities<DesignVertex>(part).ConvertAll<RefVertex>(o => o.Shape);
            GroupEntities = Helper.GatherAllEntities<RefGroup>(part);
            TopologyEntities = new List<RefEntity>[] { vertices, edges, surfaces, bodies};

        }
        /* Remoting.RemotingException: Object has been disconnected or does not exist at the server
         * */
        List<RefEntity> GetEdgeVertices(in RefEdge edge)
        {
            List<RefEntity> v = new List<RefEntity>();
            try
            {
                v.Add(edge.StartVertex);  // excpetion here: why?
                v.Add(edge.EndVertex);  // fixme: some edge has only one Vertex!
            }
            catch(System.Runtime.Remoting.RemotingException e)
            {
                Debug.WriteLine(e.ToString());
            }
            return v;
        }

        List<RefEntity> GetBodiesInGroup(in RefGroup group)
        {
            List<RefEntity> v = new List<RefEntity>();
            // todo
            return v;
        }

        /*  List<X> cast to List<Y> will create a new List, not efficient
        ///  https://stackoverflow.com/questions/5115275/shorter-syntax-for-casting-from-a-listx-to-a-listy
        ///  Array<X> to Array<Y> ?
        */
        /// <summary>
        /// </summary>
        /// <remarks> 
        /// this method correponding to Cubit API Entity.get_child_ref_entities()
        /// </remarks>
        /// <param name="ent"></param>
        /// <param name="entity_type_id"></param>
        /// <returns></returns>
        List<RefEntity> get_child_ref_entities(RefEntity ent, int entity_dim)
        {
            switch (entity_dim)
            {
                case 1:
                    return GetEdgeVertices((RefEdge)ent);
                case 2:  // ID entity is edge
                    return ((RefFace)ent).Edges.Cast<RefEntity>().ToList();
                case 3:
                    return ((RefBody)ent).Faces.Cast<RefEntity>().ToList();
                case 4: // Group of Cubit, no such in SpaceClaim
                    throw new ArgumentException("Group class in SpaceClaim is not derived from Modeler.Topology");
                default:
                    return null;
            }
        }

        private Dictionary<RefBody, DesignBody> BodyToDesignBodyMap = new Dictionary<RefBody, DesignBody>();

        /// <summary>
        /// In SpaceClaim Tesselation is owned by DesignBody, not by RefBody as in Cubit
        /// </summary>
        /// <returns></returns>
        private DesignBody FromBodyToDesignBody(in RefBody body)
        {
            if (BodyToDesignBodyMap.ContainsKey(body))
                return BodyToDesignBodyMap[body];
            else
                return null;
        }
        #endregion

        #region UniqueEntityID
        /// <summary>
        /// SpaceClaim only variable, to help generate unique ID for topology entities
        /// it should be used only by generateUniqueId() which must be called in single thread.  
        /// </summary>
        private int entity_id_counter = 0;
        /// <summary>
        ///  todo: check and test whether (testing object has been generated id in later stage) is working
        ///  note: `List[key] + Dictionary[key, value]` could be more efficient than DoubleMap
        ///         Dictionary[key, value] may be sufficient, if only object to id mapping is needed.
        /// </summary>
        private Map<Object, int> entity_id_double_map = new Map<Object, int>();

        /// <summary>
        /// generate unique ID for topology entities, the first id is 1
        /// corresponding to `int id = ent->id();` in Trelis_SDK
        /// </summary>
        /// <param name="o"> object must be Group or Topology types </param>
        /// <returns> an integer unique ID</returns>
        private int generateUniqueId(in Object o)
        {
            entity_id_counter++;  // increase before return, to make sure the first id is 1
            entity_id_double_map.Add(o, entity_id_counter); // in order to retrieve id later
            return entity_id_counter;
        }

        private int getUniqueId(in Object o)
        {
            return entity_id_double_map.Forward[o];
            /* unused code for Dictionary<> only
            if (entity_id_map.ContainKey(o))
            {
                return entity_id_map[o];
            }
            else
            {
                return 0;  // it is not sufficient to indicate no such id,  todo: just throw?
            }*/
        }
        #endregion

        internal string ExportedFileName { get; set; }

        public DagmcExporter()
        {
            // set default values
            norm_tol = 5;
            faceting_tol = 1e-3;
            len_tol = 0.0;
            verbose_warnings = false;
            fatal_on_curves = false;

            myMoabInstance = new Moab.Core();
            myGeomTool = new Moab.GeomTopoTool(myMoabInstance, false, 0, true, true);  //  missing binding has been manually added
        }

        /// <summary>
        /// check return error code for each subroutine in Execute() workflow, 
        /// corresponding to MOAB `CHK_MB_ERR_RET()` macro function
        /// </summary>
        /// <remarks>
        /// SpaceClaim is a GUI app, there is no console output capacity, message is written to Trace/Debug
        /// which can be seen in visual studio output windows, 
        /// it can be directed to log file if needed
        /// </remarks>
        /// <param name="Msg"> string message to explain the context of error </param>
        /// <param name="ErrCode"> enum Moab.ErrorCode </param>
        /// <returns> return true if no error </returns>
        bool CheckMoabErrorCode(string Msg, Moab.ErrorCode ErrCode)
        {
            if (Moab.ErrorCode.MB_SUCCESS != (ErrCode))
            {
                message.WriteLine(String.Format("{0}, {1}", Msg, ErrCode));
                //CubitInterface::get_cubit_message_handler()->print_message(message.str().c_str()); 
                return false;
            }
            else
            {
#if DEBUG
                Debug.WriteLine(String.Format("Sucessful: without {0}", Msg));
#endif
                return true;
            }
            
        }

        /// <summary>
        /// Non-critical error, print to debug console, corresponding to MOAB `CHK_MB_ERR_RET_MB()` macro function
        /// SpaceClaim is a GUI app, there is no console output capacity, message is directed to Trace/Debug
        /// which can be seen in visual studio output windows, it can be directed to log file if needed
        /// </summary>
        /// <param name="Msg"> string message to explain the context of error </param>
        /// <param name="ErrCode"> enum Moab.ErrorCode </param>
        static void PrintMoabErrorToDebug(string Msg, Moab.ErrorCode ErrCode)
        {
#if DEBUG
            if (Moab.ErrorCode.MB_SUCCESS != (ErrCode))
            {
                Debug.WriteLine(String.Format("{0}, {1}", Msg, ErrCode));
            }
#endif
        }

        /// <summary>
        /// The DAGMC mesh export workflow, the only public API for user to run
        /// </summary>
        /// <returns> return true if sucessful </returns>
        public bool Execute()
        {

            //Moab.Core myMoabInstance = myMoabInstance;
            //mw = new MakeWatertight(mdbImpl);

            //message.str("");
            bool result = true;
            Moab.ErrorCode rval;

            // Create (allocate memory for) entity sets for all geometric entities
            const int N = 4;  // Cubit Group has the base class of `RefEntity`
                              // but SpaceSpace does not have Group type shares base class with RefFace
            RefEntityHandleMap[] entityMaps = new RefEntityHandleMap[N] {
                new RefEntityHandleMap(), new RefEntityHandleMap(),
                new RefEntityHandleMap(), new RefEntityHandleMap()};
            GroupHandleMap groupMap = new GroupHandleMap(); 

            rval = create_tags();
            CheckMoabErrorCode("Error initializing DAGMC export: ", rval);

            // create a file set for storage of tolerance values
            EntityHandle file_set = 0;  // will zero value means invalid/uninitialized handle?
            rval = myMoabInstance.CreateMeshset(0, ref file_set, 0);  // the third parameter, `start_id` default value is zero
            CheckMoabErrorCode("Error creating file set.", rval);

            /// options data is from CLI command parse_options in Trelis_Plugin
            /// TODO: needs another way to capture options, may be Windows.Forms UI
            Dictionary<string, object> options = get_options();

            rval = parse_options(options, ref file_set);  
            CheckMoabErrorCode("Error parsing options: ", rval);

            GenerateTopologyEntities();  // fill data fields: TopologyEntities , GroupEntities
            rval = create_entity_sets(entityMaps);
            CheckMoabErrorCode("Error creating entity sets: ", rval);
            //rval = create_group_sets(groupMap);

            rval = create_topology(entityMaps);
            CheckMoabErrorCode("Error creating topology: ", rval);

            rval = store_surface_senses(ref entityMaps[SURFACE_INDEX], ref entityMaps[VOLUME_INDEX]);
            CheckMoabErrorCode("Error storing surface senses: ", rval);

            rval = store_curve_senses(ref entityMaps[CURVE_INDEX], ref entityMaps[SURFACE_INDEX]);
            CheckMoabErrorCode("Error storing curve senses: ", rval);

            rval = store_groups(entityMaps, groupMap);
            CheckMoabErrorCode("Error storing groups: ", rval);

            entityMaps[3].Clear();  // why clear it?
            groupMap.Clear();

            rval = create_vertices(ref entityMaps[VERTEX_INDEX]);
            CheckMoabErrorCode("Error creating vertices: ", rval);

            rval = create_curve_facets(ref entityMaps[CURVE_INDEX], ref entityMaps[VERTEX_INDEX]);
            CheckMoabErrorCode("Error faceting curves: ", rval);

            rval = create_surface_facets(ref entityMaps[SURFACE_INDEX], ref entityMaps[CURVE_INDEX], ref entityMaps[VERTEX_INDEX]);
            CheckMoabErrorCode("Error faceting surfaces: ", rval);

            rval = gather_ents(file_set);
            CheckMoabErrorCode("Error could not gather entities into file set.", rval);

            if (make_watertight)
            {
                //rval = mw->make_mesh_watertight(file_set, faceting_tol, false);
                CheckMoabErrorCode("Could not make the model watertight.", rval);
            }

            EntityHandle h = UNINITIALIZED_HANDLE;  /// to mimic "EntityHandle(integer)" in C++
            rval = myMoabInstance.WriteFile(ExportedFileName);
            CheckMoabErrorCode("Error writing file: ", rval);

            rval = teardown();  // summary
            CheckMoabErrorCode("Error tearing down export command.", rval);

            return result;
        }

        /// <summary>
        /// NOTE:  completed for MVP stage, but need a GUI form for user to customize the parameters in the next stage
        /// </summary>
        /// <returns></returns>
        Dictionary<string, object> get_options()
        {
            var options = new Dictionary<string, object>();
            //options["filename"] = "tmp_testoutput";
            options["faceting_tolerance"] = faceting_tol;  // double,  MOAB length unit?
            options["length_tolerance"] = len_tol;  // double
            options["normal_tolerance"] = norm_tol;  // int
            options["verbose"] = verbose_warnings;  // bool
            options["fatal_on_curves"] = fatal_on_curves; // bool
            return options;
        }


        /// <summary>
        /// NOTE: completed for this MVP stage, if no more new parameter added
        /// </summary>
        /// <param name="data"></param>
        /// <param name="file_set"></param>
        /// <returns></returns>
        Moab.ErrorCode parse_options(Dictionary<string, object> data, ref EntityHandle file_set)
        {
            Moab.ErrorCode rval;

            // read parsed command for faceting tolerance
            faceting_tol = (double)data["faceting_tolerance"];
            message.WriteLine(String.Format("Setting faceting tolerance to {0}", faceting_tol));

            len_tol = (double)data["length_tolerance"];
            message.WriteLine(String.Format("Setting length tolerance to {0}" , len_tol) );

            // Always tag with the faceting_tol and geometry absolute resolution
            // If file_set is defined, use that, otherwise (file_set == NULL) tag the interface  
            EntityHandle set = file_set != 0 ? file_set : 0;
            rval = myMoabInstance.SetTagData(faceting_tol_tag, ref set, faceting_tol);
            PrintMoabErrorToDebug("Error setting faceting tolerance tag ", rval);

            // read parsed command for normal tolerance
            norm_tol = (int) data["normal_tolerance"];
            message.WriteLine(String.Format("Setting normal tolerance to {0}", norm_tol));

            rval = myMoabInstance.SetTagData(geometry_resabs_tag, ref set, GEOMETRY_RESABS);
            PrintMoabErrorToDebug("Error setting geometry_resabs_tag", rval);

            // read parsed command for verbosity
            verbose_warnings = (bool)data["verbose"];
            fatal_on_curves = (bool)data["fatal_on_curves"];
            //make_watertight = data["make_watertight"];

            if (verbose_warnings && fatal_on_curves)
                message.WriteLine("This export will fail if curves fail to facet" );

            return rval;
        }


        /// <summary>
        /// NOTE: completed for this MVP stage, if no more new parameter added
        /// </summary>
        /// <returns></returns>
        Moab.ErrorCode create_tags()
        {
            Moab.ErrorCode rval;

            // get some tag handles
            int zero = 0;  // used as default value for tag
            int negone = -1;
            bool created = false;  // 
            ///  unsigned flags = 0, const void* default_value = 0, bool* created = 0
            // fixme: runtime error!
            ///  uint must be cast from enum in C#,  void* is mapped to IntPtr type in C#
            rval = myMoabInstance.GetTagHandle<int>(GEOM_DIMENSION_TAG_NAME, 1, Moab.DataType.MB_TYPE_INTEGER,
                                           out geom_tag, Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_ANY, negone);
            PrintMoabErrorToDebug("Error creating geom_tag", rval); 

            rval = myMoabInstance.GetTagHandle<int>(GLOBAL_ID_TAG_NAME, 1, Moab.DataType.MB_TYPE_INTEGER,
                                           out id_tag, Moab.TagType.MB_TAG_DENSE | Moab.TagType.MB_TAG_ANY, zero);
            PrintMoabErrorToDebug("Error creating id_tag", rval); 

            rval = myMoabInstance.GetTagHandle(NAME_TAG_NAME, NAME_TAG_SIZE, Moab.DataType.MB_TYPE_OPAQUE,
                                           out name_tag, Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_ANY);
            PrintMoabErrorToDebug("Error creating name_tag", rval);

            rval = myMoabInstance.GetTagHandle(CATEGORY_TAG_NAME, CATEGORY_TAG_SIZE, Moab.DataType.MB_TYPE_OPAQUE,
                                           out category_tag, Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_CREAT);
            PrintMoabErrorToDebug("Error creating category_tag", rval);

            rval = myMoabInstance.GetTagHandle("FACETING_TOL", 1, Moab.DataType.MB_TYPE_DOUBLE, out faceting_tol_tag,
                                           Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_CREAT);
            PrintMoabErrorToDebug("Error creating faceting_tol_tag", rval);

            rval = myMoabInstance.GetTagHandle("GEOMETRY_RESABS", 1, Moab.DataType.MB_TYPE_DOUBLE,
                                           out geometry_resabs_tag, Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_CREAT);
            PrintMoabErrorToDebug("Error creating geometry_resabs_tag", rval);

            return rval;
        }

        /// <summary>
        /// NOTE: completed for this MVP stage, if no more new parameter added
        /// consider split this function
        /// </summary>
        /// <returns></returns>
        Moab.ErrorCode teardown()
        {
            message.WriteLine("***** Faceting Summary Information *****");
            if (0 < failed_curve_count)
            {
                message.WriteLine("----- Curve Fail Information -----");
                message.WriteLine($"There were {failed_curve_count} curves that could not be faceted.");
            }
            else
            {
                message.WriteLine("----- All curves faceted correctly  -----");
            }
            if (0 < failed_surface_count)
            {
                message.WriteLine("----- Facet Fail Information -----");
                message.WriteLine($"There were {failed_surface_count} surfaces that could not be faceted.");
            }
            else
            {
                message.WriteLine("----- All surfaces faceted correctly  -----");
            }
            message.WriteLine("***** End of Faceting Summary Information *****");

            // this code section is not needed in spaceclaim
            //CubitInterface::get_cubit_message_handler()->print_message(message.str().c_str());  
            // TODO in C++ this print_message() should have a function to hide impl
            //message.str("");  

            // todo: MOABSharp, DeleteMesh() should be a method, not property!
            Moab.ErrorCode rval = myMoabInstance.DeleteMesh;   
            PrintMoabErrorToDebug("Error cleaning up mesh instance.", rval);
            //delete myGeomTool;  not needed

            return rval;

        }


        /// <summary>
        /// PROGRESS: group set seems not needed, but 
        /// </summary>
        /// <param name="entmap"></param>
        /// <returns></returns>
        Moab.ErrorCode create_entity_sets(RefEntityHandleMap[] entmap)
        {
            //GeometryQueryTool::instance()->ref_entity_list(names[dim], entlist, true);  //  Cubit Geom API
            Moab.ErrorCode rval;
            // Group set is created in a new function `create_group_sets()`
            /// FIXME: dim = 0, has error
            for (int dim = 1; dim < 4; dim++)  // collect all vertices, edges, faces, bodies
            {
                // declare new List here, no need for entlist.clean_out(); entlist.reset();
                var entlist = (List<RefEntity>)(TopologyEntities[dim]);  /// FIXME !!! from Object to List<> cast is not working

                message.WriteLine($"Debug Info: Found {entlist.Count} entities of dimension {dim}, geometry type {GEOMETRY_NAMES[dim]}");

                rval = _create_entity_sets(entlist, ref entmap[dim], dim);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return rval;  // todo:  print debug info
            }

            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// <summary>
        /// a private template function, to share code with `create_group_sets()`
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entlist"></param>
        /// <returns></returns>
        private Moab.ErrorCode _create_entity_sets<T>(in List<T> entlist, ref Dictionary<T, EntityHandle> entmap, int dim)
        {
            string[] geom_categories = { "Vertex\0", "Curve\0", "Surface\0", "Volume\0", "Group\0" };
            /// checkme: c++ use byte[][] with "\0" as ending

            Moab.ErrorCode rval;
            foreach (var ent in entlist)
            {
                EntityHandle handle = UNINITIALIZED_HANDLE;

                // Create the new meshset
                int start_id = 0;
                uint flag = (uint)(dim == 1 ? Moab.EntitySetProperty.MESHSET_ORDERED : Moab.EntitySetProperty.MESHSET_SET);
                rval = myMoabInstance.CreateMeshset(flag, ref handle, start_id);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                // Map the geom reference entity to the corresponding moab meshset
                entmap[ent] = handle;   // checkme, it is 

                /// Create tags for the new meshset

                ///  tag_data is a pointer to the opaque C++ object, need a helper function
                /// moab::ErrorCode moab::Interface::tag_set_data(moab::Tag tag_handle,
                // //     const moab::EntityHandle *entity_handles, int num_entities, const void *tag_data)
                int numEnt = 1;
                rval = myMoabInstance.SetTagData(geom_tag, ref handle, dim);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                int id = generateUniqueId(ent);

                /// CONSIDER: set more tags' data in one go by Range, which is more efficient in C#
                rval = myMoabInstance.SetTagData(id_tag, ref handle, id);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                rval = myMoabInstance.SetTagData(category_tag, ref handle, geom_categories[dim]);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
            }
            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// <summary>
        /// not needed! there is a function create_group_entsets()
        /// SpaceClaim specific, to supplement `create_entity_sets()` which can not deal with Group
        /// </summary>
        /// <param name="groupMap"></param>
        /// <returns></returns>
/*        Moab.ErrorCode create_group_sets(GroupHandleMap groupMap)
        {
            int dim = 4;
            var entlist = GroupEntities;
            Moab.ErrorCode rval = _create_entity_sets(entlist, groupMap, dim);
            return rval;
        }*/

        /// <summary> 
        /// write parent-children relationship into MOAB
        ///  PROGRESS: not tested, group-body relation seems not saved
        /// </summary>
        /// <remarks> 
        /// SpaceClaim 's Group class is not derived from Topology base, 
        /// so this function is quite different from Trelis plugin's impl
        /// </remarks>
        /// <param name="entitymaps"></param>
        /// <returns></returns>
        Moab.ErrorCode create_topology(RefEntityHandleMap[] entitymaps)
        {
            Moab.ErrorCode rval;
            for (int dim = 1; dim < 4; ++dim)
            {
                var entitymap = entitymaps[dim];
                foreach (KeyValuePair<RefEntity, EntityHandle> entry in entitymap)
                {
                    // declare new List here, no need for entlist.clean_out(); entlist.reset(); 
                    List<RefEntity> entitylist = get_child_ref_entities(entry.Key, dim);
                    foreach (RefEntity ent in entitylist)
                    {
                        if (entitymaps[dim - 1].ContainsKey(ent))
                        {
                            EntityHandle h = entitymaps[dim - 1][ent];
                            rval = myMoabInstance.AddParentChild(entry.Value, h);
                            if (Moab.ErrorCode.MB_SUCCESS != rval)
                                return rval;  // todo:  print debug info
                        }
                        else  // Fixme
                        {
                            message.WriteLine($"There is logic error, children handle is not found for entity dim = {dim}");
                        }
                    }
                }
            }
            // todo: extra work needed for CubitGroup topology
            foreach (var group in GroupEntities)
            {
                //var bodies = GetBodiesInGroup(group);
            }

            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// <summary>
        /// Progress: not understood?
        /// In Cubit from each face it is possible to get both/all bodies that share this face, 
        /// No spaceclaim API has been confirmed, here use some API to close code flow
        /// </summary>
        /// <param name="surface_map"></param>
        /// <param name="volume_map"></param>
        /// <returns></returns>
        Moab.ErrorCode store_surface_senses(ref RefEntityHandleMap surface_map, ref RefEntityHandleMap volume_map)
        {
            Moab.ErrorCode rval;

            foreach (KeyValuePair<RefEntity, EntityHandle> entry in surface_map)
            {
                List<EntityHandle> ents = new List<EntityHandle>();
                List<bool> senses = new List<bool>();
                RefFace face = (RefFace)(entry.Key);

                // how to filter the face? for external not shared face? 

                // get senses, for each topology entity in Cubit, connected to more than one upper geometry
                // IsReverse() ,   but sense only make sense related with Parent Topology Object
                // Cubut each lower topology types may have more than one upper topology types
                var bodies = from f in face.AdjacentFaces select f.Body;  // get_parent
                foreach (Body b in bodies)
                {
                    ents.Add(volume_map[b]);
                    senses.Add(face.IsReversed);
                }

                /* FIXME:
                RefFace* face = (RefFace*)(ci->first);
                BasicTopologyEntity *forward = 0, *reverse = 0;
                for (SenseEntity* cf = face->get_first_sense_entity_ptr();
                     cf; cf = cf->next_on_bte()) 
                { 
                  BasicTopologyEntity* vol = cf->get_parent_basic_topology_entity_ptr();
                  // Allocate vol to the proper topology entity (forward or reverse)
                  if (cf->get_sense() == CUBIT_UNKNOWN ||
                      cf->get_sense() != face->get_surface_ptr()->bridge_sense()) {
                    // Check that each surface has a sense for only one volume
                    if (reverse) {
                      message << "Surface " << face->id() << " has reverse sense " <<
                        "with multiple volume " << reverse->id() << " and " <<
                        "volume " << vol->id() << std::endl;
                      return moab::MB_FAILURE;
                    }
                    reverse = vol;
                  }
                  if (cf->get_sense() == CUBIT_UNKNOWN ||
                      cf->get_sense() == face->get_surface_ptr()->bridge_sense()) {
                    // Check that each surface has a sense for only one volume
                    if (forward) {
                      message << "Surface " << face->id() << " has forward sense " <<
                        "with multiple volume " << forward->id() << " and " <<
                        "volume " << vol->id() << std::endl;
                      return moab::MB_FAILURE;
                    }
                    forward = vol;
                  }
                */


                /*  todo: check 
                for (int i = 0; i < ents.Count; i++)
                {
                    myGeomTool.SetSense(entry.Value, ents[i], senses[i]);
                }
                bool reverse = false;

                // set sense
                if (! reverse)
                {
                    rval = myGeomTool->set_sense(ci->second, volume_map[forward], moab::SENSE_FORWARD);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
                }
                if (reverse)
                {
                    rval = myGeomTool->set_sense(ci->second, volume_map[reverse], moab::SENSE_REVERSE);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
                }
                */
            }

            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="curve_map"></param>
        /// <param name="surface_map"></param>
        /// <returns></returns>
        Moab.ErrorCode store_curve_senses(ref RefEntityHandleMap curve_map, ref RefEntityHandleMap surface_map)
        {
            Moab.ErrorCode rval;
            
            foreach (KeyValuePair<RefEntity, EntityHandle> entry in curve_map)
            {
                List<EntityHandle> ents = new List<EntityHandle>();
                List<int> senses = new List<int>();
                RefEdge edge = (RefEdge)entry.Key;

                //FIXME: find all parent sense relation edge->get_first_sense_entity_ptr()
                /*
                    BasicTopologyEntity* fac = ce->get_parent_basic_topology_entity_ptr();
                    moab::EntityHandle face = surface_map[fac];
                    if (ce->get_sense() == CUBIT_UNKNOWN ||
                        ce->get_sense() != edge->get_curve_ptr()->bridge_sense())
                    {
                        ents.push_back(face);
                        senses.push_back(moab::SENSE_REVERSE);
                    }
                    if (ce->get_sense() == CUBIT_UNKNOWN ||
                        ce->get_sense() == edge->get_curve_ptr()->bridge_sense())
                    {
                        ents.push_back(face);
                        senses.push_back(moab::SENSE_FORWARD);
                    }
                */

                // this MOAB API `myGeomTool.SetSenses(entry.Value, ents, senses); ` is not binded,
                // due to missing support of std::vector<int>
                // use the less effcient API to set sense for each entity
                for (int i = 0; i< ents.Count; i++)
                {
                    myGeomTool.SetSense(entry.Value, ents[i], senses[i]);
                }
  
            }

             return Moab.ErrorCode.MB_SUCCESS;
        }


        Moab.ErrorCode store_groups(RefEntityHandleMap[] entitymap, GroupHandleMap group_map )
        {
            Moab.ErrorCode rval;

            // Create entity sets for all ref groups
            rval = create_group_entsets(ref group_map);
            if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

            // Store group names and entities in the mesh
            rval = store_group_content(entitymap, ref group_map);
            if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="group_map"></param>
        /// <returns></returns>
        // refentity_handle_map& group_map
        Moab.ErrorCode create_group_entsets(ref GroupHandleMap group_map)
        {
            Moab.ErrorCode rval;

            List<RefGroup> allGroups = GroupEntities; //  (List<RefGroup>)TopologyEntities[GROUP_INDEX];
            foreach (RefGroup  group in allGroups)
            {
                // Create entity handle for the group
                EntityHandle h = UNINITIALIZED_HANDLE;
                int start_id = 0;
                rval = myMoabInstance.CreateMeshset((uint)Moab.EntitySetProperty.MESHSET_SET, ref h, start_id);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                string groupName = group.Name;
                if (null != groupName && groupName.Length > 0)
                {
                    if (groupName.Length >= NAME_TAG_SIZE)
                    {
                        groupName = groupName.Substring(0, NAME_TAG_SIZE - 1);
                        message.WriteLine($"WARNING: group name '{groupName}' is truncated to a max length {NAME_TAG_SIZE} char");
                    }
                    rval = myMoabInstance.SetTagData(name_tag, ref h, groupName);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
                }

                int id = generateUniqueId(group);
                rval = myMoabInstance.SetTagData<int>(id_tag, ref h, id);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;

                rval = myMoabInstance.SetTagData(category_tag, ref h, "Group\0");
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;

                // TODO: missing code: Check for extra group names  there may be no such things in SpaceClaim
                /*
                if (name_list.size() > 1)
                {
                    for (int j = extra_name_tags.size(); j < name_list.size(); ++j)
                    {
                        sprintf(namebuf, "EXTRA_%s%d", NAME_TAG_NAME, j);
                        moab::Tag t;
                        rval =myMoabInstance.tag_get_handle(namebuf, NAME_TAG_SIZE, Moab.ErrorCode.MB_TYPE_OPAQUE, t, Moab.ErrorCode.MB_TAG_SPARSE | Moab.ErrorCode.MB_TAG_CREAT);
                        assert(!rval);
                        extra_name_tags.push_back(t);
                    }
                    // Add extra group names to the group handle
                    for (int j = 0; j < name_list.size(); ++j)
                    {
                        name1 = name_list.get_and_step();
                        memset(namebuf, '\0', NAME_TAG_SIZE);
                        strncpy(namebuf, name1.c_str(), NAME_TAG_SIZE - 1);
                        if (name1.length() >= (unsigned)NAME_TAG_SIZE)
                        {
                            message.WriteLine("WARNING: group name '" << name1.c_str()
                                    << "' truncated to '" << namebuf << "'" ; ;
                        }
                        rval =myMoabInstance.tag_set_data(extra_name_tags[j], &h, 1, namebuf);
                        if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
                    }
                }

                */

                // Add the group handle
                group_map[group] = h;
            }

            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// <summary>
        /// Progress: not completed due to mismatched API
        ///  This function will be dramatically diff from Trelis DAGMC Plugin in C++
        ///  Range class is used, need unit test
        /// </summary>
        /// <param name="entitymap"></param>
        /// <returns></returns>
        // refentity_handle_map (&entitymap)[5]
        Moab.ErrorCode store_group_content(RefEntityHandleMap[] entitymap, ref GroupHandleMap groupMap)
        {
            Moab.ErrorCode rval;

            List<RefGroup> allGroups = GroupEntities; //  (List<RefGroup>)TopologyEntities[GROUP_INDEX];
            foreach (var gh in groupMap)
            {
                Moab.Range entities = new Moab.Range();
                foreach (DocObject obj in gh.Key.Members)  // Cubit: grp->get_child_ref_entities(entlist);
                {
                    // In Cubit, A group can contains another group as child, but it is not possible in SpaceClaim
                    // so these 2 lines are not needed in SpaceClaim
                    /*
                    if (entitymap[4].find(ent) != entitymap[4].end())
                    {
                        // Child is another group; examine its contents
                        entities.insert(entitymap[4][ent]);
                    }
                    */

                    if (null != (DesignBody)obj) 
                    {
                        var body = (DesignBody)obj;
                        var ent = body.Shape;
                        // FIXME: from Body get a list of Volumes, but there is no such API/concept in SpaceClaim
                        //In SpaceClaim, Body has PieceCount, but it seems not possible to get each piece
                        //  get a list of Volumes from Body:  `DLIList<RefVolume*> vols;  body->ref_volumes(vols);`
                        /*
                         // Child is a CGM Body, which presumably comprises some volumes--
                         // extract volumes as if they belonged to group.
                          DLIList<RefVolume*> vols;
                          body->ref_volumes(vols);
                          for (int vi = vols.size(); vi--; ) {
                            RefVolume* vol = vols.get_and_step();
                            if (entitymap[3].find(vol) != entitymap[3].end()) {
                              entities.insert(entitymap[3][vol]);
                            } else {
                              message << "Warning: CGM Body has orphan RefVolume" << std::endl;
                            }
                          }
                        */
                    }
                    else // not a Body geometry
                    {
                        //int dim = 1;  // get dim/type of geometry/topology type, need a helper function
                        // FIXME: yet translated code
                        /*
                        if (dim < 4)
                        {
                            if (entitymap[dim].find(ent) != entitymap[dim].end())
                                entities.insert(entitymap[dim][ent]);
                        }
                        */
                    }
                }
                if (!entities.Empty)
                {
                    rval = myMoabInstance.AddEntities(gh.Value, entities);
                    if (Moab.ErrorCode.MB_SUCCESS != rval)
                        return rval;
                }
            }

            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode _add_vertex(in Point pos,  ref EntityHandle h)
        {
            Moab.ErrorCode rval;
            double[] coords = { pos.X, pos.Y, pos.Z };
            rval = myMoabInstance.CreateVertex(coords, ref h);
            if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
            
            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode create_vertices(ref RefEntityHandleMap vertex_map)
        {
            Moab.ErrorCode rval;
            foreach (var key in vertex_map.Keys)
            {
                EntityHandle h = UNINITIALIZED_HANDLE;
                RefVertex v = (RefVertex)key;
                Point pos = v.Position;
                rval = _add_vertex(pos, ref h);
                // Add the vertex to its tagged meshset
                rval = myMoabInstance.AddEntities(vertex_map[key], ref h, 1);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
                // point map entry at vertex handle instead of meshset handle to
                // simplify future operations
                vertex_map[key] = h;
            }
            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// <summary>
        ///  code has been merged into create_surface_facets() for performance.
        /// </summary>
        /// <param name="curve_map"></param>
        /// <param name="vertex_map"></param>
        /// <returns></returns>
        Moab.ErrorCode create_curve_facets(ref RefEntityHandleMap edge_map,
                                           ref RefEntityHandleMap vertex_map)
        {
            Moab.ErrorCode rval;

            var allBodies = TopologyEntities[VOLUME_INDEX];
            foreach (var ent in allBodies)
            {
                var body = (RefBody)ent;
                var designBoby = FromBodyToDesignBody(body);
                Moab.Range entities = new Moab.Range();
                //var designBody = body.
                foreach (var kv in designBoby.GetEdgeTessellation(body.Edges))
                {
                    // do nothing, as this function body has been moved/merged with `create_surface_facets()`
                }
            }

            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode check_edge_mesh(RefEdge edge, ICollection<Point> points)
        {
            var curve = edge.GetGeometry<Curve>();  // the return may be null
            if (points.Count == 0)
            {
                // check if fatal error found on curves
                if (fatal_on_curves)
                {
                    message.WriteLine($"Failed to facet the curve with id: {getUniqueId(edge)}");
                    return Moab.ErrorCode.MB_FAILURE;
                }
                // otherwise record them
                else
                {
                    failed_curve_count++;
                    failed_curves.Add(getUniqueId(edge));
                    return Moab.ErrorCode.MB_FAILURE;
                }
            }
            
            if (points.Count < 2)
            {
                var interval = 1e-3;  // todo, not compilable code
                /*
                if (curve.GetLength(interval) > GEOMETRY_RESABS)   // `start_vtx != end_vtx`  not necessary
                {
                    message.WriteLine($"Warning: No facetting for curve {edge.GetHashCode()}");
                    return Moab.ErrorCode.MB_FAILURE;
                }
                */
            }

            // Check for closed curve
            RefVertex start_vtx, end_vtx;
            start_vtx = edge.StartVertex;
            end_vtx = edge.EndVertex;
            // Check to see if the first and last interior vertices are considered to be
            // coincident by CUBIT
            bool closed = (points.Last() - points.First()).Magnitude < GEOMETRY_RESABS;
            if (closed != (start_vtx == end_vtx))
            {
                message.WriteLine($"Warning: topology and geometry inconsistant for possibly closed curve id = {getUniqueId(edge)}");
            }

            // Check proximity of vertices to end coordinates
            if ((start_vtx.Position - points.First()).Magnitude > GEOMETRY_RESABS ||
                (end_vtx.Position - points.Last()).Magnitude > GEOMETRY_RESABS)  // todo: is Magnitude == 
            {

                curve_warnings--;
                if (curve_warnings >= 0 || verbose_warnings)
                {
                    message.WriteLine($"Warning: vertices not at ends of curve id = {getUniqueId(edge)}");
                    if (curve_warnings == 0 && !verbose_warnings)
                    {
                        message.WriteLine("further instances of this warning will be suppressed...");
                    }
                }
            }
            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode add_edge_mesh(RefEdge edge, ICollection<Point> points, ref RefEntityHandleMap edge_map,
                               ref RefEntityHandleMap vertex_map)
        {
            Moab.ErrorCode rval;
            EntityHandle edgeHandle = edge_map[edge];
            if (Moab.ErrorCode.MB_SUCCESS != check_edge_mesh(edge, points))
                return Moab.ErrorCode.MB_FAILURE;

            var curve = edge.GetGeometry<Curve>();  // the return may be null

            // Todo: Need to reverse data but how?
            //if (curve->bridge_sense() == CUBIT_REVERSED)
            //    std::reverse(points.begin(), points.end());

            RefVertex start_vtx, end_vtx;
            start_vtx = edge.StartVertex;
            end_vtx = edge.EndVertex;

            // Special case for point curve, closed curve has only 1 point
            if (points.Count < 2)
            {
                EntityHandle h = vertex_map[start_vtx];  // why ???
                rval = myMoabInstance.AddEntities(edgeHandle, ref h, 1);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;
            }

            var segs = new List<EntityHandle>();
            var verts = new List<EntityHandle>();
            /// FIXME: no such key in map,  skip now
            //verts.Add(vertex_map[start_vtx]);  // todo: check if in spaceclaim the edge tessellation has starting and ending vertex
            foreach (var point in points)
            {
                EntityHandle h = UNINITIALIZED_HANDLE;
                rval = _add_vertex(point, ref h);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;
                verts.Add(h);
            }
            /// FIXME: no such key in map,  skip now
            //verts.Add(vertex_map[end_vtx]); // todo: check if in spaceclaim the edge tessellation has starting and ending vertex

            EntityHandle[] meshVerts = verts.ToArray();
            // Create edges, can this be skipped?
            for (int i = 0; i < verts.Count - 1; ++i)
            {
                EntityHandle h = UNINITIALIZED_HANDLE;

                rval = myMoabInstance.CreateElement(Moab.EntityType.MBEDGE, ref meshVerts[i], 2, ref h);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;
                segs.Add(h);
            }

            // If closed, remove duplicate, must done after adding the meshedge
            if (verts.First() == verts.Last())
                verts.RemoveAt(verts.Count - 1);

            // Add entities to the curve meshset from entitymap
            rval = myMoabInstance.AddEntities(edgeHandle, ref meshVerts[0], meshVerts.Length);
            if (Moab.ErrorCode.MB_SUCCESS != rval)
                return Moab.ErrorCode.MB_FAILURE;
            EntityHandle[] meshEdges = segs.ToArray();
            rval = myMoabInstance.AddEntities(edgeHandle, ref meshEdges[0], meshEdges.Length);
            if (Moab.ErrorCode.MB_SUCCESS != rval)
                return Moab.ErrorCode.MB_FAILURE;

            return Moab.ErrorCode.MB_SUCCESS;
        }


        Moab.ErrorCode add_surface_mesh(in RefFace face, in FaceTessellation t, EntityHandle faceHandle, ref RefEntityHandleMap vertex_map)
        {
            Moab.ErrorCode rval;

            int nPoint = t.Vertices.Count;
            int nFacet = t.Facets.Count;
            // record the failures for information
            if (nFacet == 0)
            {
                failed_surface_count++;
                failed_surfaces.Add(getUniqueId(face));
            }

            var hVerts = new EntityHandle[nPoint];  // vertex id in MOAB
            var pointData = t.Vertices;

            // For each geometric vertex, find a single coincident point in facets, Otherwise, print a warning
            // i.e. find the vertices on edge/curve which have been added into MOAB during add_edge_mesh(),
            // wont a hash matching faster?
            foreach (var v in vertex_map.Keys)
            {
                var pos = ((RefVertex)v).Position;
                for (int j = 0; j < nPoint; ++j)
                {
                    hVerts[j] = UNINITIALIZED_HANDLE;
                    if ((pos - pointData[j].Position).Magnitude < GEOMETRY_RESABS)  // length_square < GEOMETRY_RESABS*GEOMETRY_RESABS
                    {
                        hVerts[0] = vertex_map[v];
                    }
                    else
                    {
                        message.WriteLine($"Warning: Coincident vertices in surface id = {getUniqueId(face)}, for the point at {pos}");
                    }
                }
            }

            for(int i = 0; i< nPoint; i++)
            {
                if (hVerts[i] == UNINITIALIZED_HANDLE) // not found existing vertex in MOAB database
                {
                    EntityHandle h = UNINITIALIZED_HANDLE;
                    _add_vertex(pointData[i].Position, ref h);
                    //vertex_map[] = h;
                    hVerts[i] = h;
                }
            }

            var hFacets = new List<EntityHandle>(); // [nFacet];
            EntityHandle[] tri = new EntityHandle[3];
            foreach (Facet facet in t.Facets)  // C++ Cubit: (int i = 0; i < facet_list.size(); i += facet_list[i] + 1)
            {
                var type = Moab.EntityType.MBTRI;  // in SpaceClaim it must be triangle
                tri[0] = hVerts[facet.Vertex0];  // todo: debug to see if facet.Vertex0 starts with zero!
                tri[1] = hVerts[facet.Vertex1];
                tri[2] = hVerts[facet.Vertex2];

                EntityHandle h = UNINITIALIZED_HANDLE;
                rval = myMoabInstance.CreateElement(type, ref tri[0], tri.Length, ref h);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;
                hFacets.Add(h);
            }

            // Add entities to the curve meshset from entitymap
            EntityHandle[] meshVerts = hVerts;
            rval = myMoabInstance.AddEntities(faceHandle, ref meshVerts[0], meshVerts.Length);
            if (Moab.ErrorCode.MB_SUCCESS != rval)
                return Moab.ErrorCode.MB_FAILURE;
            EntityHandle[] meshFaces = hFacets.ToArray();
            rval = myMoabInstance.AddEntities(faceHandle, ref meshFaces[0], meshFaces.Length);
            if (Moab.ErrorCode.MB_SUCCESS != rval)
                return Moab.ErrorCode.MB_FAILURE;

            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// create_curve_facets() moved here for performance and diff API design in SpaceClaim
        Moab.ErrorCode create_surface_facets(ref RefEntityHandleMap surface_map, ref RefEntityHandleMap edge_map,
                                             ref RefEntityHandleMap vertex_map)
        {
            Moab.ErrorCode rval;

            TessellationOptions meshOptions = new TessellationOptions();  
            // todo: mininum length control
            var allBodies = TopologyEntities[VOLUME_INDEX];
            foreach (var ent in allBodies)
            {
                var body = (RefBody)ent;
                Moab.Range facet_entities = new Moab.Range();
                try
                {
                    // FIXME: sometime no error here, sometime RemotingException to get DesignBody
                    DesignBody designBody = FromBodyToDesignBody(body);
                    /// NOTE: may make a smaller Vertex_map, for duplicaet map check, and then merge with global vertex_map
                    foreach (var kv in designBody.GetEdgeTessellation(body.Edges))
                    {
                        add_edge_mesh(kv.Key, kv.Value, ref edge_map, ref vertex_map);
                    }
                    /// todo:  here new tesselaton may be created. 
                    foreach (var kv in body.GetTessellation(body.Faces, meshOptions))
                    {
                        var face = kv.Key;
                        EntityHandle faceHandle = surface_map[face];
                        FaceTessellation t = kv.Value;
                        add_surface_mesh(face, t, faceHandle, ref vertex_map);
                    }
                }
                catch (System.Exception e)
                {
                    message.WriteLine("Fixme: Body curve edge saving failed" + e.ToString());
                }

            }

            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode gather_ents(EntityHandle gather_set)
        {
            Moab.ErrorCode rval;
            Moab.Range new_ents = new Moab.Range();
            rval = myMoabInstance.GetEntitiesByHandle(0, new_ents, false);  // the last parameter has a default value false
            PrintMoabErrorToDebug("Could not get all entity handles.", rval);

            //make sure there the gather set is empty
            Moab.Range gather_ents = new Moab.Range();
            rval = myMoabInstance.GetEntitiesByHandle(gather_set, gather_ents, false);  // 
            PrintMoabErrorToDebug("Could not get the gather set entities.", rval);

            if (0 != gather_ents.Size)
            {
                PrintMoabErrorToDebug("Unknown entities found in the gather set.", rval);
            }

            rval = myMoabInstance.AddEntities(gather_set, new_ents);
            PrintMoabErrorToDebug("Could not add newly created entities to the gather set.", rval);

            return rval;
        }

    }
}