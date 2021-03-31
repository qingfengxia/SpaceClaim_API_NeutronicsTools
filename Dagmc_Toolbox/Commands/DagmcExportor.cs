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
using SpaceClaim.Api.V18.Extensibility;
using SpaceClaim.Api.V18.Geometry;
using SpaceClaim.Api.V18.Modeler;
using SpaceClaim.Api.V18;
using Point = SpaceClaim.Api.V18.Geometry.Point;

using Moab = MOAB.Moab;
using static MOAB.Constants;
using static MOAB.Moab.Core;
using message = System.Diagnostics.Debug;
/// EntityHandle depends on C++ build configuration and 64bit or 32bit, see MOAB's header <EntityHandle.hpp>
using EntityHandle = System.UInt64;   // type alias is only valid in the source file!

/// type alias to help Cubit/Trelis developers to understand SpaceClaim API
///using RefEntity = SpaceClaim.Api.V18.Geometry.IShape;
using RefGroup = SpaceClaim.Api.V18.Group;   /// SpaceClaim.Api.V18.Group;
using RefEntity = SpaceClaim.Api.V18.Modeler.Topology;
using RefBody = SpaceClaim.Api.V18.Modeler.Body;
using RefFace = SpaceClaim.Api.V18.Modeler.Face;
using RefEdge = SpaceClaim.Api.V18.Modeler.Edge;
using RefVertex = SpaceClaim.Api.V18.Modeler.Vertex;
using Primitive = System.Double;

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


        /// GEOMETRY_RESABS - If the distance between two points is less 
        /// than GEOMETRY_RESABS the points are considered to be identical.
        readonly double GEOMETRY_RESABS = 1.0E-6;  /// Trelis SDK /utl/GeometryDefines.h

        readonly string[] GEOMETRY_NAMES = { "Vertex", "Curve", "Surface", "Volume", "Group"};
        readonly int VERTEX_INDEX = 0;
        readonly int CURVE_INDEX = 1;
        readonly int SURFACE_INDEX = 2;
        readonly int VOLUME_INDEX = 3;
        readonly int GROUP_INDEX = 4;
        object[] TopologyEntities;

        internal class EntitityTopology
        {
            public List<RefVertex> Vertices;
            public List<RefEdge> Edges;
            public List<RefFace> Faces;
            public List<RefBody> Bodies;
            public List<RefGroup> Groups;

            public EntitityTopology(List<RefVertex> v, List<RefEdge> e, List<RefFace> f, List<RefBody> b, List<RefGroup> g)
            {
                Vertices = v;
                Edges = e;
                Faces = f;
                Bodies = b;
                Groups = g;
            }
            public object GetEnties(int index)
            {
                switch (index)
                {
                    case 1:
                        // return Edges.Cast<RefEntity>().ToList();  /// this create a new object, not efficient
                        return Edges;
                    default:
                        return null;

                }
            }
            public object this[int index]
            {
                // get and set accessors
                get => GetEnties(index);
            }
        }


        /// <summary>
        /// in C++, all these Tag, typedef of TagInfo*, initialized to zero (nullptr)
        /// </summary>
        Moab.TagInfo geom_tag, id_tag, name_tag, category_tag, faceting_tol_tag, geometry_resabs_tag;

        DagmcExporter()
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

        bool CHK_MB_ERR_RET(string A, Moab.ErrorCode B)
        {
            if (Moab.ErrorCode.MB_SUCCESS != (B))
            {
                message.WriteLine("{0}, {1}", A, B);
                //CubitInterface::get_cubit_message_handler()->print_message(message.str().c_str()); 
                return false;
            }
            return true;
        }

        void CHK_MB_ERR_RET_MB(string A, Moab.ErrorCode B)
        {
            if (Moab.ErrorCode.MB_SUCCESS != (B))
            {
                message.WriteLine("{0}, {1}", A, B);  /// may just use Debug.WriteLine() as Windows Desktop App has no Console
                //return B;
            }
        }

        void CHK_MB_ERR_RET_DEBUG(string A, Moab.ErrorCode B)
        {
#if DEBUG
            if (Moab.ErrorCode.MB_SUCCESS != (B))
            {
                Debug.WriteLine("{0}, {1}", A, B);
            }
#endif
        }

        bool Execute()
        {

            //Moab.Core myMoabInstance = myMoabInstance;
            //mw = new MakeWatertight(mdbImpl);

            //message.str("");
            bool result = true;
            Moab.ErrorCode rval;

            // Create entity sets for all geometric entities
            const int N = 4;  // Cubit Group is an dim higher than volume, but SpaceSpace does not have such 
            RefEntityHandleMap[] entityMaps = new RefEntityHandleMap[N];
            GroupHandleMap groupMap = new GroupHandleMap(); 

            rval = create_tags();
            CHK_MB_ERR_RET("Error initializing DAGMC export: ", rval);

            // create a file set for storage of tolerance values
            EntityHandle file_set = 0;  // will zero value means invalid/uninitialized handle?
            rval = myMoabInstance.CreateMeshset(0, ref file_set, 0);  // the third parameter, `start_id` default value is zero
            CHK_MB_ERR_RET("Error creating file set.", rval);

            /// options data is from CLI command parse_options in Trelis_Plugin
            /// TODO: needs another way to capture options, may be Windows.Forms UI
            Dictionary<string, object> options = get_options();

            rval = parse_options(options, ref file_set);  
            CHK_MB_ERR_RET("Error parsing options: ", rval);

            TopologyEntities = generate_topoloyg_map();

            rval = create_entity_sets(entityMaps);
            CHK_MB_ERR_RET("Error creating entity sets: ", rval);

            rval = create_topology(entityMaps);
            CHK_MB_ERR_RET("Error creating topology: ", rval);

            rval = store_surface_senses(ref entityMaps[SURFACE_INDEX], ref entityMaps[VOLUME_INDEX]);
            CHK_MB_ERR_RET("Error storing surface senses: ", rval);

            rval = store_curve_senses(ref entityMaps[CURVE_INDEX], ref entityMaps[SURFACE_INDEX]);
            CHK_MB_ERR_RET("Error storing curve senses: ", rval);

            rval = store_groups(entityMaps, groupMap);
            CHK_MB_ERR_RET("Error storing groups: ", rval);

            entityMaps[3].Clear();  // why clear it
            entityMaps[4].Clear();

            rval = create_vertices(ref entityMaps[CURVE_INDEX]);
            CHK_MB_ERR_RET("Error creating vertices: ", rval);

            rval = create_curve_facets(ref entityMaps[CURVE_INDEX], ref entityMaps[VERTEX_INDEX]);
            CHK_MB_ERR_RET("Error faceting curves: ", rval);

            rval = create_surface_facets(ref entityMaps[SURFACE_INDEX], ref entityMaps[VERTEX_INDEX]);
            CHK_MB_ERR_RET("Error faceting surfaces: ", rval);

            rval = gather_ents(file_set);
            CHK_MB_ERR_RET("Could not gather entities into file set.", rval);

            if (make_watertight)
            {
                //rval = mw->make_mesh_watertight(file_set, faceting_tol, false);
                CHK_MB_ERR_RET("Could not make the model watertight.", rval);
            }

            EntityHandle h = 0;  /// to mimic nullptr  for  "EntityHandle*" in C++
            rval = myMoabInstance.WriteFile((string)options["filename"], null, null, ref h, 0, null, 0);
            CHK_MB_ERR_RET("Error writing file: ", rval);

            rval = teardown();  // summary
            CHK_MB_ERR_RET("Error tearing down export command.", rval);

            return result;
        }

        Dictionary<string, object> get_options()
        {
            var options = new Dictionary<string, object>();
            options["filename"] = "tmp_testoutput";
            options["faceting_tolerance"] = faceting_tol;  // double,  MOAB length unit?
            options["length_tolerance"] = len_tol;  // double
            options["normal_tolerance"] = norm_tol;  // int
            options["verbose"] = verbose_warnings;  // bool
            options["fatal_on_curves"] = fatal_on_curves; // bool
            return options;
        }

        Moab.ErrorCode parse_options(Dictionary<string, object> data, ref EntityHandle file_set)
        {
            Moab.ErrorCode rval;

            // read parsed command for faceting tolerance
            faceting_tol = (double)data["faceting_tolerance"];
            message.WriteLine("Setting faceting tolerance to {}", faceting_tol);

            len_tol = (double)data["length_tolerance"];
            message.WriteLine("Setting length tolerance to " , len_tol );

            // Always tag with the faceting_tol and geometry absolute resolution
            // If file_set is defined, use that, otherwise (file_set == NULL) tag the interface  
            EntityHandle set = file_set != 0 ? file_set : 0;
            rval = myMoabInstance.SetTagData(faceting_tol_tag, ref set, faceting_tol);
            CHK_MB_ERR_RET_MB("Error setting faceting tolerance tag", rval);

            // read parsed command for normal tolerance
            norm_tol = (int) data["normal_tolerance"];
            message.WriteLine("Setting normal tolerance to {}", norm_tol);

            rval = myMoabInstance.SetTagData(geometry_resabs_tag, ref set, GEOMETRY_RESABS);
            CHK_MB_ERR_RET_MB("Error setting geometry_resabs_tag", rval);

            // read parsed command for verbosity
            verbose_warnings = (bool)data["verbose"];
            fatal_on_curves = (bool)data["fatal_on_curves"];
            //make_watertight = data["make_watertight"];

            if (verbose_warnings && fatal_on_curves)
                message.WriteLine("This export will fail if curves fail to facet" );

            return rval;
        }


        Moab.ErrorCode create_tags()
        {
            Moab.ErrorCode rval;

            // get some tag handles
            int zero = 0;  // used as default value for tag
            int negone = -1;
            bool created = false;  // 
            ///  unsigned flags = 0, const void* default_value = 0, bool* created = 0
            ///  uint must be cast from enum in C#,  void* is mapped to IntPtr type in C#
            rval = myMoabInstance.GetTagHandle<int>(GEOM_DIMENSION_TAG_NAME, 1, Moab.DataType.MB_TYPE_INTEGER,
                                           out geom_tag, Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_ANY, negone);
            CHK_MB_ERR_RET_MB("Error creating geom_tag", rval); 

            rval = myMoabInstance.GetTagHandle<int>(GLOBAL_ID_TAG_NAME, 1, Moab.DataType.MB_TYPE_INTEGER,
                                           out id_tag, Moab.TagType.MB_TAG_DENSE | Moab.TagType.MB_TAG_ANY, zero);
            CHK_MB_ERR_RET_MB("Error creating id_tag", rval); 

            rval = myMoabInstance.GetTagHandle(NAME_TAG_NAME, NAME_TAG_SIZE, Moab.DataType.MB_TYPE_OPAQUE,
                                           out name_tag, Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_ANY);
            CHK_MB_ERR_RET_MB("Error creating name_tag", rval);

            rval = myMoabInstance.GetTagHandle(CATEGORY_TAG_NAME, CATEGORY_TAG_SIZE, Moab.DataType.MB_TYPE_OPAQUE,
                                           out category_tag, Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_CREAT);
            CHK_MB_ERR_RET_MB("Error creating category_tag", rval);

            rval = myMoabInstance.GetTagHandle("FACETING_TOL", 1, Moab.DataType.MB_TYPE_DOUBLE, out faceting_tol_tag,
                                           Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_CREAT);
            CHK_MB_ERR_RET_MB("Error creating faceting_tol_tag", rval);

            rval = myMoabInstance.GetTagHandle("GEOMETRY_RESABS", 1, Moab.DataType.MB_TYPE_DOUBLE,
                                           out geometry_resabs_tag, Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_CREAT);
            CHK_MB_ERR_RET_MB("Error creating geometry_resabs_tag", rval);

            return rval;
        }

        /// this function should split
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

            //CubitInterface::get_cubit_message_handler()->print_message(message.str().c_str());  
            // TODO this print_message() should have a function to hide impl
            //message.str("");  // not needed

            Moab.ErrorCode rval = myMoabInstance.DeleteMesh();   // todo MOABSharp, it is a method, not property!
            CHK_MB_ERR_RET_MB("Error cleaning up mesh instance.", rval);
            //delete myGeomTool;  not needed

            return rval;

        }

        object[] generate_topoloyg_map()
        {
            Part part = Helper.GetActiveMainPart();  // todo: can a document have multiple Parts?
            List<RefBody> bodies = Helper.GatherAllEntities<DesignBody>(part).ConvertAll<RefBody>(o => o.Shape);
            List<RefFace> surfaces = Helper.GatherAllEntities<DesignFace>(part).ConvertAll<RefFace>(o => o.Shape);
            List<RefEdge> curves = Helper.GatherAllEntities<DesignEdge>(part).ConvertAll<RefEdge>(o => o.Shape);
            List<RefVertex> vertices = new List<RefVertex>();  //TODO
            // Helper.GatherAllEntities<DesignVertex>(part).ConvertAll<RefVertex>(o => o.Shape);
            List<RefGroup> groups = Helper.GatherAllEntities<RefGroup>(part);
            object[] entities = { vertices, curves, surfaces, bodies, groups};
            return entities;
        }

        // refentity_handle_map (&entmap)[5]
        Moab.ErrorCode create_entity_sets(RefEntityHandleMap[] entmap)
        {
            Moab.ErrorCode rval;
            string[] geom_categories = { "Vertex\0", "Curve\0", "Surface\0", "Volume\0", "Group\0" };
            // todo: fill into byte[][] if needed

            //GeometryQueryTool::instance()->ref_entity_list(names[dim], entlist, true);  //  Cubit Geom API

            var DIMS = 4;
            for (int dim = DIMS-1; dim > 0; dim--)  // collect all bodies then all surfaces
            {
                // declare new List here, no need for entlist.clean_out(); entlist.reset(); 
                List<RefEntity> entlist = (List<RefEntity>)TopologyEntities[dim];  /// !!! this List<> cast may not working

                message.WriteLine($"Found {entlist.Count} entities of dimension {dim}, geometry type {GEOMETRY_NAMES[dim]}");

                foreach (var ent in entlist)
                {
                    EntityHandle handle = 0;

                    // Create the new meshset
                    int start_id = 0;
                    uint flag = (uint)(dim == 1 ? Moab.EntitySetProperty.MESHSET_ORDERED : Moab.EntitySetProperty.MESHSET_SET);
                    rval = myMoabInstance.CreateMeshset(flag, ref handle, start_id);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                    // Map the geom reference entity to the corresponding moab meshset
                    entmap[dim][ent] = handle;

                    /// Create tags for the new meshset
                    
                    ///  tag_data is a pointer to the opaque C++ object, need a helper function
                    /// moab::ErrorCode moab::Interface::tag_set_data(moab::Tag tag_handle,
                    // //     const moab::EntityHandle *entity_handles, int num_entities, const void *tag_data)
                    int numEnt = 1;
                    rval = myMoabInstance.SetTagData(geom_tag, ref handle, dim);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                    /// TODO: sequence number or just hashID? SpaceClaim may does not have, how about moniker?
                    //int id = ent->id();  // returned ID not the `class id` defiend in Trelis_SDK
                    int id = ent.GetHashCode();

                    /// CONSIDER: set more tags' data in one go by Range, which is more efficient in C#
                    rval = myMoabInstance.SetTagData(id_tag, ref handle, id);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                    rval = myMoabInstance.SetTagData(category_tag, ref handle, geom_categories[dim]);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
                }
            }
            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// <summary>
        ///  List<X> cast to List<Y> will create a new List, not efficient
        ///  https://stackoverflow.com/questions/5115275/shorter-syntax-for-casting-from-a-listx-to-a-listy
        ///  Array<X> to Array<Y> ?
        /// </summary>
        /// <param name="ent"></param>
        /// <param name="entity_type_id"></param>
        /// <returns></returns>
        List<RefEntity> get_parent_ref_entities(RefEntity ent, int entity_type_id)
        {
            switch(entity_type_id)
            {
                case 0:
                    return ((RefVertex)ent).Edges.Cast<RefEntity>().ToList();
                case 1:
                    return ((RefEdge)ent).Faces.Cast<RefEntity>().ToList();
                case 2:
                    return new List<RefEntity>() { ((RefFace)ent).Body };  // single parent??? CompSolid?
                //case 3:
                //    return ((RefVertex)ent).Edges.Cast<RefEntity>().ToList();
                default:
                    return null;
            }
        }



        // refentity_handle_map (&entitymap)[5]
        /// <summary>
        ///  SpaceClaim use bottom-up topology reference, different from Cubut's top-down
        /// </summary>
        /// <param name="entitymaps"></param>
        /// <returns></returns>
        Moab.ErrorCode create_topology(RefEntityHandleMap[] entitymaps)
        {
            Moab.ErrorCode rval;
            var DIMS = 4;
            for (int dim = 0; dim < DIMS-1; ++dim)
            {
                var entitymap = entitymaps[dim];
                foreach (KeyValuePair<RefEntity, EntityHandle> entry in entitymap)
                {
                    // declare new List here, no need for entlist.clean_out(); entlist.reset(); 
                    List<RefEntity> entitylist = get_parent_ref_entities(entry.Key, dim);
                    foreach (RefEntity ent in entitylist)
                    {
                        EntityHandle h = entitymaps[dim + 1][ent];
                        rval = myMoabInstance.AddParentChild(h, entry.Value);

                        if (Moab.ErrorCode.MB_SUCCESS != rval)
                            return rval;  // todo:  print debug info
                    }
                }
            }

            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode store_surface_senses(ref RefEntityHandleMap surface_map, ref RefEntityHandleMap volume_map)
        {
            Moab.ErrorCode rval;

            foreach (KeyValuePair<RefEntity, EntityHandle> entry in surface_map)
            {
                List<EntityHandle> ents = new List<EntityHandle>();
                List<bool> senses = new List<bool>();
                RefFace face = (RefFace)(entry.Key);

                // get senses, for each topology entity in Cubit, connected to more than one upper geometry
                // IsReverse() ,   but sense only make sense related with Parent Topology Object
                // Cubut each lower topology types may have more than one upper topology types

                var bodies = get_parent_ref_entities(entry.Key, 1);
                foreach(Body b in bodies)
                {
                    ents.Add(volume_map[b]);
                    senses.Add(face.IsReversed); 
                }
                /*  todo: check 
                for (int i = 0; i < ents.Count; i++)
                {
                    myGeomTool.SetSense(entry.Value, ents[i], senses[i]);
                }
                bool reverse = false;

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

            /*
                BasicTopologyEntity* forward = 0, *reverse = 0;
                for (SenseEntity* cf = face->get_first_sense_entity_ptr();
                     cf; cf = cf->next_on_bte())
                {
                    BasicTopologyEntity* vol = cf->get_parent_basic_topology_entity_ptr();
                    // Allocate vol to the proper topology entity (forward or reverse)
                    if (cf->get_sense() == CUBIT_UNKNOWN ||
                        cf->get_sense() != face->get_surface_ptr()->bridge_sense())
                    {
                        // Check that each surface has a sense for only one volume
                        if (reverse)
                        {
                            message.WriteLine("Surface " << face->id() << " has reverse sense " <<
                              "with multiple volume " << reverse->id() << " and " <<
                              "volume " << vol->id() ; ;
                            return Moab.ErrorCode.MB_FAILURE;
                        }
                        reverse = vol;
                    }
                    if (cf->get_sense() == CUBIT_UNKNOWN ||
                        cf->get_sense() == face->get_surface_ptr()->bridge_sense())
                    {
                        // Check that each surface has a sense for only one volume
                        if (forward)
                        {
                            message.WriteLine("Surface " << face->id() << " has forward sense " <<
                              "with multiple volume " << forward->id() << " and " <<
                              "volume " << vol->id() ; ;
                            return Moab.ErrorCode.MB_FAILURE;
                        }
                        forward = vol;
                    }
                }

                if (forward)
                {
                    rval = myGeomTool->set_sense(ci->second, volume_map[forward], moab::SENSE_FORWARD);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
                }
                if (reverse)
                {
                    rval = myGeomTool->set_sense(ci->second, volume_map[reverse], moab::SENSE_REVERSE);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
                }
            }
            */
            return Moab.ErrorCode.MB_SUCCESS;
        }

        // 
        Moab.ErrorCode store_curve_senses(ref RefEntityHandleMap curve_map, ref RefEntityHandleMap surface_map)
        {
            Moab.ErrorCode rval;
            
            foreach (KeyValuePair<RefEntity, EntityHandle> entry in curve_map)
            {
                List<EntityHandle> ents = new List<EntityHandle>();
                List<int> senses = new List<int>();
                RefEdge edge = (RefEdge)entry.Key;

                //TODO: find all parent sense relation edge->get_first_sense_entity_ptr()

                // this MOAB API `SetSenses` is not binded, due to missing support of std::vector<int>, use the less effcient API
                //myGeomTool.SetSenses(entry.Value, ents, senses);   
                for (int i = 0; i< ents.Count; i++)
                {
                    myGeomTool.SetSense(entry.Value, ents[i], senses[i]);
                }
  
            }

            /*   
            std::vector<moab::EntityHandle> ents;
            std::vector<int> senses;
            refentity_handle_map_itor ci;
            for (ci = curve_map.begin(); ci != curve_map.end(); ++ci)
            {
                RefEdge* edge = (RefEdge*)(ci->first);
                ents.clear();
                senses.clear();
                for (SenseEntity* ce = edge->get_first_sense_entity_ptr();
                     ce; ce = ce->next_on_bte())
                {
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
                }

                rval = myGeomTool->set_senses(ci->second, ents, senses);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
            }
            */

            return Moab.ErrorCode.MB_SUCCESS;
        }

        // (&entitymap)[5]
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
        ///  group in SpaceClaim has not been 
        /// </summary>
        /// <param name="group_map"></param>
        /// <returns></returns>
        // refentity_handle_map& group_map
        Moab.ErrorCode create_group_entsets(ref GroupHandleMap group_map)
        {
            Moab.ErrorCode rval;

            List<RefGroup> allGroups = (List<RefGroup>)TopologyEntities[GROUP_INDEX];
            foreach (RefGroup  group in allGroups)
            {
                // Create entity handle for the group
                EntityHandle h = 0;
                int start_id = 0;
                rval = myMoabInstance.CreateMeshset((uint)Moab.EntitySetProperty.MESHSET_SET, ref h, start_id);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                string groupName = group.Name;
                if (null != groupName && groupName.Length > 0)
                {
                    if (groupName.Length >= NAME_TAG_SIZE)
                    {
                        groupName = groupName.Substring(0, NAME_TAG_SIZE - 1);
                        message.WriteLine("WARNING: group name '{0}' is truncated to a max length {2} char", groupName, NAME_TAG_SIZE);
                    }
                    rval = myMoabInstance.SetTagData(name_tag, ref h, groupName);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
                }

                int id = group.GetHashCode();   //  todo:  ->id();
                rval = myMoabInstance.SetTagData<int>(id_tag, ref h, id);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;

                rval = myMoabInstance.SetTagData(category_tag, ref h, "Group\0");
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;

                // TODO:  Check for extra group names

                group_map[group] = h;
            }

            /*
            const char geom_categories[][CATEGORY_TAG_SIZE] =
              { "Vertex\0", "Curve\0", "Surface\0", "Volume\0", "Group\0"};
            DLIList<RefEntity*> entitylist;

            // Create entity sets for all ref groups
            std::vector<moab::Tag> extra_name_tags;
            DLIList<CubitString> name_list;
            entitylist.clean_out();

            // Get all entity groups from the CGM model
            GeometryQueryTool::instance()->ref_entity_list("group", entitylist);
            entitylist.reset();

            // Loop over all groups
            for (int i = entitylist.size(); i--;)
            {
                // Take the next group
                RefEntity* grp = entitylist.get_and_step();
                name_list.clean_out();
                // Get the names of all entities in this group from the solid model
                RefEntityName::instance()->get_refentity_name(grp, name_list);
                if (name_list.size() == 0)
                    continue;
                // Set pointer to first name of the group and set the first name to name1
                name_list.reset();
                CubitString name1 = name_list.get();

                // Create entity handle for the group
                EntityHandle h;
                rval = myMoabInstance.CreateMeshset(MESHSET_SET, ref h);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                // Set tag data for the group
                char namebuf[NAME_TAG_SIZE];
                memset(namebuf, '\0', NAME_TAG_SIZE);
                strncpy(namebuf, name1.c_str(), NAME_TAG_SIZE - 1);
                if (name1.length() >= (unsigned)NAME_TAG_SIZE)
                {
                    message.WriteLine("WARNING: group name '" << name1.c_str()
                            << "' truncated to '" << namebuf << "'" ; ;
                }
                rval = myMoabInstance.SetTagData(name_tag, ref h, 1, namebuf);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;


                int id = grp->id();
                rval = myMoabInstance.SetTagData(id_tag, ref h, 1, id);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;

                rval = myMoabInstance.SetTagData(category_tag, ref h, 1, geom_categories[4]);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;
                // Check for extra group names
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
                // Add the group handle
                group_map[grp] = h;
            }
            */
            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// <summary>
        ///  Range class is used, need unit test
        /// </summary>
        /// <param name="entitymap"></param>
        /// <returns></returns>
        // refentity_handle_map (&entitymap)[5]
        Moab.ErrorCode store_group_content(RefEntityHandleMap[] entitymap, ref GroupHandleMap groupMap)
        {
            Moab.ErrorCode rval;

            List<RefGroup> allGroups = (List<RefGroup>)TopologyEntities[GROUP_INDEX];
            foreach (var gh in groupMap)
            {
                Moab.Range entities = new Moab.Range();
                foreach (DocObject obj in gh.Key.Members)  // Cubit: grp->get_child_ref_entities(entlist);
                {
                    var body = (DesignBody)obj;
                    if (null != body) 
                    {
                        var ent = body.Shape;
                        // from Body get a list of Volumes
                    }
                    else if ( null == body)
                    {
                        var ent = body.Shape;
                        // from Body get a list of Volumes
                        int dim = 1;  // get dim/type of geometry/topology type

                        //if (! (ent in entitymap[dim]))
                        //    entities.Insert(entitymap[dim][ent]);
                    }
                    else
                    {

                    }
                    
                }
                if (!entities.Empty)
                {
                    rval = myMoabInstance.AddEntities(gh.Value, entities);
                    if (Moab.ErrorCode.MB_SUCCESS != rval)
                        return rval;
                }
            }


            /*
            DLIList<RefEntity*> entlist;
            refentity_handle_map_itor ci;
            // Store contents for each group
            entlist.reset();
            for (ci = entitymap[4].begin(); ci != entitymap[4].end(); ++ci)
            {
                RefGroup* grp = (RefGroup*)(ci->first);
                entlist.clean_out();
                grp->get_child_ref_entities(entlist);

                moab::Range entities;
                while (entlist.size())
                {
                    RefEntity* ent = entlist.pop();
                    int dim = ent->dimension();

                    if (dim < 0)
                    {
                        Body* body;
                        if (entitymap[4].find(ent) != entitymap[4].end())
                        {
                            // Child is another group; examine its contents
                            entities.insert(entitymap[4][ent]);
                        }
                        else if ((body = dynamic_cast<Body*>(ent)) != NULL)
                        {
                            // Child is a CGM Body, which presumably comprises some volumes--
                            // extract volumes as if they belonged to group.
                            DLIList<RefVolume*> vols;
                            body->ref_volumes(vols);
                            for (int vi = vols.size(); vi--;)
                            {
                                RefVolume* vol = vols.get_and_step();
                                if (entitymap[3].find(vol) != entitymap[3].end())
                                {
                                    entities.insert(entitymap[3][vol]);
                                }
                                else
                                {
                                    message.WriteLine("Warning: CGM Body has orphan RefVolume" ; ;
                                }
                            }
                        }
                        else
                        {
                            // Otherwise, warn user.
                            message.WriteLine("Warning: A dim<0 entity is being ignored by ReadCGM." ; ;
                        }
                    }
                    else if (dim < 4)
                    {
                        if (entitymap[dim].find(ent) != entitymap[dim].end())
                            entities.insert(entitymap[dim][ent]);
                    }
                }


                    */

            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode create_vertices(ref RefEntityHandleMap vertex_map)
        {
            Moab.ErrorCode rval;
            foreach (var key in vertex_map.Keys)
            {
                RefVertex v = (RefVertex)key;
                Point pos = v.Position; 
                double[] coords =  { pos.X, pos.Y, pos.Z };

                EntityHandle h = 0;
                rval = myMoabInstance.CreateVertex(coords, ref h);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                // Add the vertex to its tagged meshset
                rval = myMoabInstance.AddEntities(vertex_map[key], ref h, 1);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                // point map entry at vertex handle instead of meshset handle to
                // simplify future operations
                vertex_map[key] = h;
            }

            /*
            refentity_handle_map_itor ci;

            for (ci = vertex_map.begin(); ci != vertex_map.end(); ++ci)
            {
                CubitVector pos = dynamic_cast<RefVertex*>(ci->first)->coordinates();
                double coords[3] = { pos.x(), pos.y(), pos.z() };

                EntityHandle vh;
                rval = myMoabInstance.CreateVertex(coords, vh);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                // Add the vertex to its tagged meshset
                rval = myMoabInstance.AddEntities(ci->second, &vh, 1);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                // point map entry at vertex handle instead of meshset handle to
                // simplify future operations
                ci->second = vh;
            }
            */
            return Moab.ErrorCode.MB_SUCCESS;
        }


        Moab.ErrorCode create_curve_facets(ref RefEntityHandleMap curve_map,
                                           ref RefEntityHandleMap vertex_map)
        {
            Moab.ErrorCode rval;
            /*
                CubitStatus s;

                // Maximum allowable curve-endpoint proximity warnings
                // If this integer becomes negative, then abs(curve_warnings) is the
                // number of warnings that were suppressed.
                int curve_warnings = 0;
                failed_curve_count = 0;

                // Map iterator
                refentity_handle_map_itor ci;

                // Create geometry for all curves
                GMem data;
                for (ci = curve_map.begin(); ci != curve_map.end(); ++ci)
                {
                    // Get the start and end points of the curve in the form of a reference edge
                    RefEdge* edge = dynamic_cast<RefEdge*>(ci->first);
                    // Get the edge's curve information
                    Curve* curve = edge->get_curve_ptr();
                    // Clean out previous curve information
                    data.clear();
                    // Facet curve according to parameters and CGM version
                    s = edge->get_graphics(data, norm_tol, faceting_tol);

                    if (s != CUBIT_SUCCESS)
                    {
                        // if we fatal on curves
                        if (fatal_on_curves)
                        {
                            message.WriteLine("Failed to facet the curve " << edge->id() ; ;
                            return Moab.ErrorCode.MB_FAILURE;
                        }
                        // otherwise record them
                        else
                        {
                            failed_curve_count++;
                            failed_curves.push_back(edge->id());
                        }
                        continue;
                    }

                    std::vector<CubitVector> points = data.point_list();

                    // Need to reverse data?
                    if (curve->bridge_sense() == CUBIT_REVERSED)
                        std::reverse(points.begin(), points.end());

                    // Check for closed curve
                    RefVertex* start_vtx, *end_vtx;
                    start_vtx = edge->start_vertex();
                    end_vtx = edge->end_vertex();

                    // Special case for point curve
                    if (points.size() < 2)
                    {
                        if (start_vtx != end_vtx || curve->measure() > GEOMETRY_RESABS)
                        {
                            message.WriteLine("Warning: No facetting for curve " << edge->id() ; ;
                            continue;
                        }
                        moab::EntityHandle h = vertex_map[start_vtx];
                        rval =myMoabInstance.add_entities(ci->second, &h, 1);
                        if (Moab.ErrorCode.MB_SUCCESS != rval)
                            return Moab.ErrorCode.MB_FAILURE;
                        continue;
                    }
                    // Check to see if the first and last interior vertices are considered to be
                    // coincident by CUBIT
                    const bool closed = (points.front() - points.back()).length() < GEOMETRY_RESABS;
                    if (closed != (start_vtx == end_vtx))
                    {
                        message.WriteLine("Warning: topology and geometry inconsistant for possibly closed curve "
                                << edge->id() ; ;
                    }

                    // Check proximity of vertices to end coordinates
                    if ((start_vtx->coordinates() - points.front()).length() > GEOMETRY_RESABS ||
                        (end_vtx->coordinates() - points.back()).length() > GEOMETRY_RESABS)
                    {

                        curve_warnings--;
                        if (curve_warnings >= 0 || verbose_warnings)
                        {
                            message.WriteLine("Warning: vertices not at ends of curve " << edge->id() ; ;
                            if (curve_warnings == 0 && !verbose_warnings)
                            {
                                message.WriteLine("         further instances of this warning will be suppressed..." ; ;
                            }
                        }
                    }

                    // Create interior points
                    std::vector<moab::EntityHandle> verts, edges;
                    verts.push_back(vertex_map[start_vtx]);
                    for (size_t i = 1; i < points.size() - 1; ++i)
                    {
                        double coords[] = { points[i].x(), points[i].y(), points[i].z() };
                        EntityHandle h;
                        // Create vertex entity
                        rval = myMoabInstance.CreateVertex(ref coords[0], ref h);
                        if (Moab.ErrorCode.MB_SUCCESS != rval)
                            return Moab.ErrorCode.MB_FAILURE;
                        verts.push_back(h);
                    }
                    verts.push_back(vertex_map[end_vtx]);

                    // Create edges
                    for (size_t i = 0; i < verts.size() - 1; ++i)
                    {
                        EntityHandle h;
                        rval = myMoabInstance.CreateElement(MBEDGE, &verts[i], 2, ref h);
                        if (Moab.ErrorCode.MB_SUCCESS != rval)
                            return Moab.ErrorCode.MB_FAILURE;
                        edges.push_back(h);
                    }

                    // If closed, remove duplicate
                    if (verts.front() == verts.back())
                        verts.pop_back();
                    // Add entities to the curve meshset from entitymap
                    rval = myMoabInstance.AddEntities(ci->second, &verts[0], verts.size());
                    if (Moab.ErrorCode.MB_SUCCESS != rval)
                        return Moab.ErrorCode.MB_FAILURE;
                    rval = myMoabInstance.AddEntities(ci->second, &edges[0], edges.size());
                    if (Moab.ErrorCode.MB_SUCCESS != rval)
                        return Moab.ErrorCode.MB_FAILURE;
                }
                */
            if (!verbose_warnings && curve_warnings < 0)
            {
                message.WriteLine($"Suppressed {-curve_warnings } 'vertices not at ends of curve' warnings.");
                //std::cerr << "To see all warnings, use reader param VERBOSE_CGM_WARNINGS." ;;
            }

            return Moab.ErrorCode.MB_SUCCESS;
        }

        /// this function is full of Cubit Types
        Moab.ErrorCode create_surface_facets(ref RefEntityHandleMap surface_map,
                                             ref RefEntityHandleMap vertex_map)
        {
            Moab.ErrorCode rval;
            /*
                    refentity_handle_map_itor ci;
                    CubitStatus s;   // should be type alias
                    failed_surface_count = 0;

                    DLIList<TopologyEntity*> me_list;

                    GMem data;
                    // Create geometry for all surfaces
                    for (ci = surface_map.begin(); ci != surface_map.end(); ++ci)
                    {
                        RefFace* face = dynamic_cast<RefFace*>(ci->first);

                        data.clear();
                        s = face->get_graphics(data, norm_tol, faceting_tol, len_tol);

                        if (CUBIT_SUCCESS != s)
                            return Moab.ErrorCode.MB_FAILURE;

                        std::vector<CubitVector> points = data.point_list();    // std::vector<CubitVector> should be type alias

                        // Declare array of all vertex handles
                        std::vector<moab::EntityHandle> verts(points.size(), 0);

                // Get list of geometric vertices in surface
                me_list.clean_out();
                ModelQueryEngine::instance()->query_model(*face, DagType::ref_vertex_type(), me_list);

                // For each geometric vertex, find a single coincident point in facets
                // Otherwise, print a warning
                for (int i = me_list.size(); i--;)
                {
                    // Assign geometric vertex
                    RefVertex* vtx = dynamic_cast<RefVertex*>(me_list.get_and_step());
                    CubitVector pos = vtx->coordinates();

                    for (int j = 0; j < points.size(); ++j)
                    {
                        // Assign facet vertex
                        CubitVector vpos = points[j];

                        // Check to see if they are considered coincident
                        if ((pos - vpos).length_squared() < GEOMETRY_RESABS * GEOMETRY_RESABS)
                        {
                            // If this facet vertex has already been found coincident, print warning
                            if (verts[j])
                            {
                                message.WriteLine("Warning: Coincident vertices in surface " << face->id() ; ;
                            }
                            // If a coincidence is found, keep track of it in the verts vector
                            verts[j] = vertex_map[vtx];
                            break;
                        }
                    }
                }

                // Now create vertices for the remaining points in the facetting
                for (int i = 0; i < points.size(); ++i)
                {
                    if (verts[i]) // If a geometric vertex
                        continue;
                    double coords[] = { points[i].x(), points[i].y(), points[i].z() };
                    // Return vertex handle to verts to fill in all remaining facet
                    // vertices
                    rval =myMoabInstance.create_vertex(coords, verts[i]);
                    if (Moab.ErrorCode.MB_SUCCESS != rval)
                        return rval;
                }

                std::vector<int> facet_list = data.facet_list();

                // record the failures for information
                if (facet_list.size() == 0)
                {
                    failed_surface_count++;
                    failed_surfaces.push_back(face->id());
                }

                // Now create facets
                Moab.Range facets = new Moab.Range();
                std::vector<EntityHandle> corners;
                for (int i = 0; i < facet_list.size(); i += facet_list[i] + 1)
                {
                    // Get number of facet verts
                    int num_verts = facet_list[i];
                    corners.resize(num_verts);
                    for (int j = 1; j <= num_verts; ++j)
                    {
                        if (facet_list[i + j] >= (int)verts.size())
                        {
                            message.WriteLine("ERROR: Invalid facet data for surface " << face->id() ; ;
                            return Moab.ErrorCode.MB_FAILURE;
                        }
                        corners[j - 1] = verts[facet_list[i + j]];
                    }
                    Moab.EntityType type;
                    if (num_verts == 3)
                        type = Moab.EntityType.MBTRI;
                    else
                    {
                        message.WriteLine("Warning: non-triangle facet in surface " << face->id() ; ;
                        message.WriteLine("  entity has " << num_verts << " edges" ; ;
                        if (num_verts == 4)
                            type = Moab.EntityType.MBQUAD;
                        else
                            type = Moab.EntityType.MBPOLYGON;
                    }

                    //if (surf->bridge_sense() == CUBIT_REVERSED)
                    //std::reverse(corners.begin(), corners.end());

                    EntityHandle h;
                    rval =myMoabInstance.CreateElement(type, &corners[0], corners.size(), ref h);
                    if (Moab.ErrorCode.MB_SUCCESS != rval)
                        return Moab.ErrorCode.MB_FAILURE;

                    facets.insert(h);
                }

                // Add vertices and facets to surface set
                rval = myMoabInstance.AddEntities(ci->second, &verts[0], verts.size());
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;
                rval = myMoabInstance.AddEntities(ci->second, facets);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;
                  }
                */

            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode gather_ents(EntityHandle gather_set)
        {
            Moab.ErrorCode rval;
            Moab.Range new_ents = new Moab.Range();
            rval = myMoabInstance.GetEntitiesByHandle(0, new_ents, false);  // the last parameter has a default value false
            CHK_MB_ERR_RET_MB("Could not get all entity handles.", rval);

            //make sure there the gather set is empty
            Moab.Range gather_ents = new Moab.Range();
            rval = myMoabInstance.GetEntitiesByHandle(gather_set, gather_ents, false);  // 
            CHK_MB_ERR_RET_MB("Could not get the gather set entities.", rval);

            if (0 != gather_ents.Size)
            {
                CHK_MB_ERR_RET_MB("Unknown entities found in the gather set.", rval);
            }

            rval = myMoabInstance.AddEntities(gather_set, new_ents);
            CHK_MB_ERR_RET_MB("Could not add newly created entities to the gather set.", rval);

            return rval;
        }

    }
}