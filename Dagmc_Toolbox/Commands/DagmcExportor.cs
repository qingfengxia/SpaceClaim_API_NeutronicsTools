using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using SpaceClaim.Api.V18.Extensibility;
using SpaceClaim.Api.V18.Geometry;
using SpaceClaim.Api.V18.Modeler;
using System.Xml.Serialization;
using System.Windows.Forms;
using SpaceClaim.Api.V18;
using Point = SpaceClaim.Api.V18.Geometry.Point;
using System.Diagnostics;
using Moab = MOAB.Moab;
using static MOAB.Constants;
using message = System.Diagnostics.Debug;
using EntityHandle = System.UInt64;   // type alias is only valid in the source file?
/// depends on C++ build configuration and 64bit or 32bit, see MOAB's header <EntityHandle.hpp>


//typedef std::map<RefEntity*, moab::EntityHandle> refentity_handle_map;
//typedef std::map<RefEntity*, moab::EntityHandle>::iterator refentity_handle_map_itor;


namespace Dagmc_Toolbox
{
    using refentity_handle_map = Dictionary<int, EntityHandle>;
    class DagmcExporter
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

        //std::ostringstream message;  // 

        Moab.TagInfo geom_tag, id_tag, name_tag, category_tag, faceting_tol_tag, geometry_resabs_tag;

        DagmcExporter()
        {
            // set default values
            norm_tol = 5;
            faceting_tol = 1e-3;
            len_tol = 0.0;
            verbose_warnings = false;
            fatal_on_curves = false;

            // just test MOAB API
            string[] args = new string[] { "moab_test" };
            MOAB.iMOAB.Initialize(args);
            MOAB.iMOAB.Finalize();

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
                message.WriteLine("{0}, {1}", A, B);
                //return B;
            }
        }

        bool Execute()
        {

            //Moab.Core myMoabInstance = myMoabInstance;
            //mw = new MakeWatertight(mdbImpl);

            //message.str("");
            bool result = true;
            Moab.ErrorCode rval;

            // Create entity sets for all geometric entities
            const int N = 5;
            refentity_handle_map[] entmap = new refentity_handle_map[N];

            rval = create_tags();
            CHK_MB_ERR_RET("Error initializing DAGMC export: ", rval);

            // create a file set for storage of tolerance values
            EntityHandle file_set = 0;
            rval = myMoabInstance.CreateMeshset(0, ref file_set, 0);  // what is the default value for the third parameter?
            CHK_MB_ERR_RET("Error creating file set.", rval);

            //rval = parse_options(data, &file_set);  // needs another way to capture options, may be Windows.Forms UI
            //CHK_MB_ERR_RET("Error parsing options: ", rval);

            rval = create_entity_sets(entmap);
            CHK_MB_ERR_RET("Error creating entity sets: ", rval);

            rval = create_topology(entmap);
            CHK_MB_ERR_RET("Error creating topology: ", rval);

            rval = store_surface_senses(ref entmap[2], ref entmap[3]);
            CHK_MB_ERR_RET("Error storing surface senses: ", rval);

            rval = store_curve_senses(ref entmap[1], ref entmap[2]);
            CHK_MB_ERR_RET("Error storing curve senses: ", rval);

            rval = store_groups(entmap);
            CHK_MB_ERR_RET("Error storing groups: ", rval);

            entmap[3].Clear();
            entmap[4].Clear();

            rval = create_vertices(ref entmap[0]);
            CHK_MB_ERR_RET("Error creating vertices: ", rval);

            rval = create_curve_facets(ref entmap[1], ref entmap[0]);
            CHK_MB_ERR_RET("Error faceting curves: ", rval);

            rval = create_surface_facets(ref entmap[2], ref entmap[0]);
            CHK_MB_ERR_RET("Error faceting surfaces: ", rval);

            rval = gather_ents(file_set);
            CHK_MB_ERR_RET("Could not gather entities into file set.", rval);

            if (make_watertight)
            {
                //rval = mw->make_mesh_watertight(file_set, faceting_tol, false);
                CHK_MB_ERR_RET("Could not make the model watertight.", rval);
            }

            Dictionary<string, string> options = new Dictionary<string, string>();
            options["filename"] = "tmp_testoutput";
            // options data is from parse_options in Trelis_Plugin

            EntityHandle h = 0;  /// to mimic nullptr  for  "EntityHandle*" in C++
            rval = myMoabInstance.WriteFile(options["filename"], null, null, ref h, 0, null, 0);
            CHK_MB_ERR_RET("Error writing file: ", rval);

            rval = teardown();  // summary
            CHK_MB_ERR_RET("Error tearing down export command.", rval);

            return result;
        }

        Moab.ErrorCode create_tags()
        {
            Moab.ErrorCode rval;

            // get some tag handles
            int negone = -1;
            bool created = false;  // 
            ///  unsigned flags = 0, const void* default_value = 0, bool* created = 0
            ///  uint must be cast from enum in C#,  void* is mapped to IntPtr type in C#
            rval = myMoabInstance.TagGetHandle(GEOM_DIMENSION_TAG_NAME, 1, Moab.DataType.MB_TYPE_INTEGER,
                                           geom_tag, (uint)(Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_ANY), (IntPtr)negone, ref created);
            CHK_MB_ERR_RET_MB("Error creating geom_tag", rval);

            rval = myMoabInstance.TagGetHandle(GLOBAL_ID_TAG_NAME, 1, Moab.DataType.MB_TYPE_INTEGER,
                                           id_tag, (uint)(Moab.TagType.MB_TAG_DENSE | Moab.TagType.MB_TAG_ANY), IntPtr.Zero, ref created);
            CHK_MB_ERR_RET_MB("Error creating id_tag", rval);

            rval = myMoabInstance.TagGetHandle(NAME_TAG_NAME, NAME_TAG_SIZE, Moab.DataType.MB_TYPE_OPAQUE,
                                           name_tag, (uint)(Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_ANY), IntPtr.Zero, ref created);
            CHK_MB_ERR_RET_MB("Error creating name_tag", rval);

            rval = myMoabInstance.TagGetHandle(CATEGORY_TAG_NAME, CATEGORY_TAG_SIZE, Moab.DataType.MB_TYPE_OPAQUE,
                                           category_tag, (uint)(Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_CREAT), IntPtr.Zero, ref created);
            CHK_MB_ERR_RET_MB("Error creating category_tag", rval);

            rval = myMoabInstance.TagGetHandle("FACETING_TOL", 1, Moab.DataType.MB_TYPE_DOUBLE, faceting_tol_tag,
                                           (uint)(Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_CREAT), IntPtr.Zero, ref created);
            CHK_MB_ERR_RET_MB("Error creating faceting_tol_tag", rval);

            rval = myMoabInstance.TagGetHandle("GEOMETRY_RESABS", 1, Moab.DataType.MB_TYPE_DOUBLE,
                                           geometry_resabs_tag, (uint)(Moab.TagType.MB_TAG_SPARSE | Moab.TagType.MB_TAG_CREAT), IntPtr.Zero, ref created);
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

        // refentity_handle_map (&entmap)[5]
        Moab.ErrorCode create_entity_sets(refentity_handle_map[] entmap)
        {

            Moab.ErrorCode rval;
            /* needs lots of porting work
            const char geom_categories[][CATEGORY_TAG_SIZE] =
                        {"Vertex\0", "Curve\0", "Surface\0", "Volume\0", "Group\0"};
            const char* const names[] = { "Vertex", "Curve", "Surface", "Volume" };
                  DLIList<RefEntity*> entlist;  // what is DLIList?

            for (int dim = 0; dim< 4; dim++) {
              entlist.clean_out();
              GeometryQueryTool::instance()->ref_entity_list(names[dim], entlist, true);  // why not using myGeomTopoTool ?
                  entlist.reset();

              message.WriteLine($"Found {entlist.size()} entities of dimension {dim}");

              for (int i = entlist.size(); i--;) {
                RefEntity* ent = entlist.get_and_step();
                  moab::EntityHandle handle;

                  // Create the new meshset
                  rval =myMoabInstance.create_meshset(dim == 1 ? moab::MESHSET_ORDERED : moab::MESHSET_SET, handle);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                // Map the geom reference entity to the corresponding moab meshset
                entmap[dim][ent] = handle;

                // Create tags for the new meshset
                rval =myMoabInstance.tag_set_data(geom_tag, &handle, 1, &dim);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                int id = ent->id();
                  rval =myMoabInstance.tag_set_data(id_tag, &handle, 1, &id);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                rval =myMoabInstance.tag_set_data(category_tag, &handle, 1, &geom_categories[dim]);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;
              }
          }
         */
            return Moab.ErrorCode.MB_SUCCESS;
        }
        // refentity_handle_map (&entitymap)[5]
        Moab.ErrorCode create_topology(refentity_handle_map[] entitymap)
        {
            Moab.ErrorCode rval;
            /*
            DLIList<RefEntity*> entitylist;
            refentity_handle_map_itor ci;  // not needed in C#

            for (int dim = 1; dim < 4; ++dim)
            {
                for (ci = entitymap[dim].begin(); ci != entitymap[dim].end(); ++ci)
                {
                    entitylist.clean_out();
                    ci->first->get_child_ref_entities(entitylist);

                    entitylist.reset();
                    for (int i = entitylist.size(); i--;)
                    {
                        RefEntity* ent = entitylist.get_and_step();
                        moab::EntityHandle h = entitymap[dim - 1][ent];
                        rval = myMoabInstance.AddParentChild(ci->second, h);

                        if (Moab.ErrorCode.MB_SUCCESS != rval)
                            return rval;
                    }
                }
            }
            */
            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode store_surface_senses(ref refentity_handle_map surface_map, ref refentity_handle_map volume_map)
        {
            Moab.ErrorCode rval;
            /*
            refentity_handle_map_itor ci;

            for (ci = surface_map.begin(); ci != surface_map.end(); ++ci)
            {
                RefFace* face = (RefFace*)(ci->first);
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
        Moab.ErrorCode store_curve_senses(ref refentity_handle_map curve_map, ref refentity_handle_map surface_map)
        {
            Moab.ErrorCode rval;
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
        Moab.ErrorCode store_groups(refentity_handle_map[] entitymap)
        {
            Moab.ErrorCode rval;

            // Create entity sets for all ref groups
            rval = create_group_entsets(ref entitymap[4]);
            if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

            // Store group names and entities in the mesh
            rval = store_group_content(entitymap);
            if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

            return Moab.ErrorCode.MB_SUCCESS;
        }
        // refentity_handle_map& group_map
        Moab.ErrorCode create_group_entsets(ref refentity_handle_map group_map)
        {
            Moab.ErrorCode rval;
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
                moab::EntityHandle h;
                rval =myMoabInstance.create_meshset(moab::MESHSET_SET, h);
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
                rval =myMoabInstance.tag_set_data(name_tag, &h, 1, namebuf);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;


                int id = grp->id();
                rval =myMoabInstance.tag_set_data(id_tag, &h, 1, &id);
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;

                rval =myMoabInstance.tag_set_data(category_tag, &h, 1, &geom_categories[4]);
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
        // refentity_handle_map (&entitymap)[5]
        Moab.ErrorCode store_group_content(refentity_handle_map[] entitymap)
        {
            Moab.ErrorCode rval;
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

                if (!entities.empty())
                {
                    rval = myMoabInstance.AddEntities(ci->second, entities);
                    if (Moab.ErrorCode.MB_SUCCESS != rval) 
                        return rval;
                }
                    */

            return Moab.ErrorCode.MB_SUCCESS;
        }

        Moab.ErrorCode create_vertices(ref refentity_handle_map vertex_map)
        {
            Moab.ErrorCode rval;
            /*
            refentity_handle_map_itor ci;

            for (ci = vertex_map.begin(); ci != vertex_map.end(); ++ci)
            {
                CubitVector pos = dynamic_cast<RefVertex*>(ci->first)->coordinates();
                double coords[3] = { pos.x(), pos.y(), pos.z() };
                moab::EntityHandle vh;

                rval =myMoabInstance.create_vertex(coords, vh);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                // Add the vertex to its tagged meshset
                rval =myMoabInstance.add_entities(ci->second, &vh, 1);
                if (Moab.ErrorCode.MB_SUCCESS != rval) return rval;

                // point map entry at vertex handle instead of meshset handle to
                // simplify future operations
                ci->second = vh;
            }
            */
            return Moab.ErrorCode.MB_SUCCESS;
        }


        Moab.ErrorCode create_curve_facets(ref refentity_handle_map curve_map,
                                           ref refentity_handle_map vertex_map)
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
                        moab::EntityHandle h;
                        // Create vertex entity
                        rval =myMoabInstance.create_vertex(coords, h);
                        if (Moab.ErrorCode.MB_SUCCESS != rval)
                            return Moab.ErrorCode.MB_FAILURE;
                        verts.push_back(h);
                    }
                    verts.push_back(vertex_map[end_vtx]);

                    // Create edges
                    for (size_t i = 0; i < verts.size() - 1; ++i)
                    {
                        EntityHandle h;
                        rval =myMoabInstance.CreateElement(Moab.MBEDGE, &verts[i], 2, h);
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
        Moab.ErrorCode create_surface_facets(ref refentity_handle_map surface_map,
                                             ref refentity_handle_map vertex_map)
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
                moab::Range facets;
                std::vector<moab::EntityHandle> corners;
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

                    moab::EntityHandle h;
                    rval =myMoabInstance.create_element(type, &corners[0], corners.size(), h);
                    if (Moab.ErrorCode.MB_SUCCESS != rval)
                        return Moab.ErrorCode.MB_FAILURE;

                    facets.insert(h);
                }

                // Add vertices and facets to surface set
                rval =myMoabInstance.add_entities(ci->second, &verts[0], verts.size());
                if (Moab.ErrorCode.MB_SUCCESS != rval)
                    return Moab.ErrorCode.MB_FAILURE;
                rval =myMoabInstance.add_entities(ci->second, facets);
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