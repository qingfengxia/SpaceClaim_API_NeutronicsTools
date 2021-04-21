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

Cubit has `Body` class contains `RefVolume`

```c++
//! RefVolume class.
class CUBIT_GEOM_EXPORT RefVolume : public BasicTopologyEntity
{
public :
  
  typedef RefFace ChildType;
  typedef Body ParentType;


  //! Body class.
class CUBIT_GEOM_EXPORT Body : public GroupingEntity,  public RefEntity
```

`ITrimmedSpace` in SpaceClaim   hasArea hasVolume

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


## Todo
### ID in Cubit  mapped to what kind of ID in SpaceClaim?
`face->id()`  returns a integer id (a hash function?) this ID may change if face is modified.

Currently, `Object.GetHashCode()` in C# is used, but it may be not unique!!! see MSDN doc
> The default implementation of the GetHashCode method does not guarantee unique return values for different objects. 
https://stackoverflow.com/questions/7458139/net-is-type-gethashcode-guaranteed-to-be-unique
https://docs.microsoft.com/en-us/dotnet/api/system.object.gethashcode?redirectedfrom=MSDN&view=net-5.0#System_Object_GetHashCode

SpaceClaim API  `Face.Moniker<>` maybe used, but `Moniker<>`  does not have an integer id property.

`BasicMoniker` has `Id` property of `PersistentId` type, 
`PersistentId` is a value type, has the integer ID.
`ObjectId`  has `Version`

If goemetry is imported from other CAD software, there may be an `@id` attribute for body

### Imprint

Select all bodies and imprint, it is a command with several iteration
Body has a method `public void Imprint(	Body other )`

`Accuracy.EqualVolumes(double v1, double v2)`
Compares two volume values to see if they are equal within a predefined tolerance based on `LinearResolution`.


### Mesh on Shared face may has been written twice

needs to find a way to detect shared interior face, then save triangle once mesh.

vertex hash may need. 

FaceTessellation may not generate for shared face between bodies,    non-manifold

### Curve and Face sense/side-ness, etc 

`Face.Reversed() -> bool` is the API to check face sense,   

>  Trimmed curves and trimmed surfaces also have an IsReversed property, which tells you whether the sense of the object is the opposite of the sense of its geometry. The sense of a trimmed curve is its direction, and the sense of a trimmed surface is which way its normals face.
>
> excerpt from "SpaceClaim developer Guide"


### Unit test

spaceclaim may run IronPython file in batch mode?



### find out the output of `Debug.WriteLine() `

https://stackoverflow.com/questions/1159755/where-does-system-diagnostics-debug-write-output-appear

> While debugging `System.Diagnostics.Debug.WriteLine` will display in the output window (Ctrl+Alt+O), you can also add a `TraceListener` to the `Debug.Listeners` collection to specify `Debug.WriteLine` calls to output in other locations.

>  Note: `Debug.WriteLine` calls may not display in the output window if you have the Visual Studio option "Redirect all Output Window text to the Immediate Window" checked under the menu *Tools* → *Options* → *Debugging* → *General*. To display "*Tools* → *Options* → *Debugging*", check the box next to "*Tools* → *Options* → *Show All Settings*".