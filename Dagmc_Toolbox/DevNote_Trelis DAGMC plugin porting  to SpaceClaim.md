# Porting notes

from standard C++ to C++/CLI could be easier,   STL such as `std::cout` and `std::vector<>` can still be used.

It is assume C#6 grammar like `$string` can be used to target .net 4.5 runtime.

## steps
1. Source code porting approach: manually  translation

There is some tool to automate this conversion, but not sure about the quality. 
Given the fact only 1 C++ source file to port, manually translation approach is chosen.

2. STL replaced by .net container and IO types

3. MOAB C++ APIs replaced by their C# binding APIs
MOABSharp is the C# binding to MOAB C++ API,  https://bitbucket.org/qingfengxia/moabsharp/
which serves for this plugin in the first place.

4. Cubit types to SpaceClaim types

5. general notes on how to make Trelis_Plugin more portable to other CAD platform

https://github.com/svalinn/Trelis-plugin/issues/88

occ_facetor is a ref for sense, write in C++ for OpenCASCADE

6. GUI form to capture user options like tolerance



## MOABSharp API related

### `entity_map` as functon parameter

This should be a `Dictionary<>` in C#

### MOAB C++ typedef to C# alias

`using EntityHandle = System.UInt64;   // type alias is only valid in one source file`

https://medium.com/@morgankenyon/under-the-hood-of-c-alias-types-and-namespaces-82504a02660e


### Test MOABSharp hdf5 binding correct and dll loading

it is possible to write h5m mesh file.

### MOAB Range binding is almost completed.

`range.const_iterator` ? some typedef iterators have been skipped

```c++
// forward declare the iterators
class const_iterator;
class const_reverse_iterator;
typedef const_iterator iterator;
typedef const_reverse_iterator reverse_iterator;
```

MOAB Range is NOT a template class, but the in-source doc states it is a template class!
if not working, is that possible to add item one by one, instead of by `Range`?




## Porting Cubit API

### Cubit API to SpaceClaim API
+ IDList, similar with `std::vector<>` efficient add and remove at the end, `List<>`  in C# should be fine

+ CubitStatus:  `enum CubitStatus { CUBIT_FAILURE = 0, CUBIT_SUCCESS = 1 } ;`

+ CubitVector -> Point in SpaceClaim

#### Topology types in Cubit/Trelis
```c++
class Body; ACIS lump?

class RefEntity;   all base type for Topology
class RefGroup;
class RefVolume;
class RefFace;
class RefEdge;
class RefVertex;
```

### No one-by-one corresponding API
+ SpaceClaim Curve meshing
`DesignBody has GetEdgeTEssellation` while Cubut edge has EdgeTEssellation
>  `create_curve_facets()` should merged into `create_surface_facets()` for better performance

+ Cubit Group has children RefBody, this is kind of Topology;  in SpaceClaim,  Body is the highest dim for topology
>  EntityMap[5] =>  EntityMap[4] + GroupMap     in `create_topology()`

#### Group in SpaceClaim is diff from Cubit Group

`NameSelection` is a group, but do not know how to get Group from document
Group can contain different topology types.

#### RefBody is diff from RefVolume

### SpaceClaim has `BodyMesh`  in `Analysis` namespace

Once create mesh from Body, body will be hidden, 

```c#
    /// <summary>
    /// Gets a faceted representation of the faces of the design body.
    /// </summary>
    /// <param name="faces">The faces in this design body whose tessellation is sought; else <b>null</b> for all faces.</param>
    /// <returns>Face tessellations for each face.</returns>
    /// <remarks>
    /// Unlike the <see cref="M:SpaceClaim.Api.V18.Modeler.Body.GetTessellation(System.Collections.Generic.ICollection{SpaceClaim.Api.V18.Modeler.Face},SpaceClaim.Api.V18.Modeler.TessellationOptions)" /> method on <see cref="T:SpaceClaim.Api.V18.Modeler.Body" />,
    /// which calculates a tessellation to a desired accuracy,
    /// this method returns the tessellation already being used to display the design body, if it exists.
    /// If the design body has only just been created using the API, it will not have been displayed yet,
    /// so this method will return <b>null</b>.
    /// </remarks>
    public IDictionary<SpaceClaim.Api.V18.Modeler.Face, FaceTessellation> GetTessellation(
      ICollection<SpaceClaim.Api.V18.Modeler.Face> faces)
```
### Mesh types in SpaceClaim
doc.MainPart.Meshes -> DesignMesh  CreateFromPart
DesignMesh.Shape -> Mesh
MeshItem, base class for MeshFace, MeshVertex
MeshLoop,  a collection of MeshEdge
MeshRegion, a collection of MeshFace
MeshTopology

### ID in Cubit  mapped to what kind of ID in SpaceClaim?
face->id()  id is a hash function?

Currently, `Object.GetHashCode()` in C# is used.


