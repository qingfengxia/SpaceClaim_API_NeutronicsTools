#  Dagmc Toolbox for SpaceClaim

export MOAB DAGMC Neutronics simulation workflow in SpaceClaim, to dispense Trelis, DAGMC plugin

Qingfeng Xia
Copyright 2021 United Kingdom Atomic Energy Agency (UKAEA)
license: MIT


Due to DAGMC complicate dependencies, it is decided to make it an independent Addin


### Dependencies
+ nuget System.Half
  website
  > add as a nuget references, it is a prerelease package
  Used to compute [parallel-preprocessor]() style goemetry hashing ID

+ MOABSharp.dll: not packaged yet, together with CppSharp.Runtime.dll
  website:
  > This C# project can be added into the visual studio solution, and as a project dependency of Damgc_Toolbox
  https://docs.microsoft.com/en-us/visualstudio/ide/managing-references-in-a-project?view=vs-2019#:~:text=To%20add%20a%20reference%2C%20right,node%20and%20select%20Add%20%3E%20Reference.


+ MOAB native DLL built (together with HDF5 DLL)
  website:
  > if they are on PATH, should be able to load on developer machine, 
  copy to output folder by csproj xml file?


### Deployment Addin

1. SpaceClaim system AddIn folder is not normal user writtable
`C:\Program Files\ANSYS Inc\v195\scdm\Addins`
One DLL file such as `AnalysisAddIn.dll` , onw Manifest.xml file, and one `AnalysisAddIn.config` file
Only sufolders for each language containing research file

4. `C:\ProgramData\SpaceClaim\AddIns\Samples\V18`  
why Visual studio can create folder without admin previledge
Windows 10 per program virtualization, `C:\ProgramData\SpaceClaim` is a redirection of user folder
C:\Users\\AppData\Local\VirtualStore\ProgramData\

3. why output Addin is so big, it has included all system dll even like win32.dll

https://docs.microsoft.com/en-us/dotnet/core/deploying/trim-self-contained
The trim mode for the applications is configured with the TrimMode setting. The default value is copyused and bundles referenced assemblies with the application.
Trimming is an experimental feature in .NET Core 3.1 and .NET 5.0. Trimming is only available to applications that are published self-contained.

4. CopyLocal property for Reference
Right click on the Reference itme in **Solution Explorer**, select Property in the dropdown menu.
In the property pane, check "CopyLocal"

Microsoft.Scripting.dll has been moved into System.Core.dll in dotnet 4

5 supportedRuntime config file
Three files  `Dagmc_Toolbox.config` is an xml file: 
`<startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.7.2"/></startup>`
Manifest.xml use the file name without path, since dll asseblmy file is always in the same folder

### Questions
1. how csscript can be called in Addin GUI?
2. why 2 spaceclaim API versions?  only V18 is used
3. how a new command be added to Ribbon tab?

### Relation with CCFE_Toolbox Addin

For the time being, a new AddIn **DAGMC_Toolbox** ( output a new SpaceClaim AddIn), instead of merge into CCFE_Toolbox.dll at this development stage,
to avoid merge confliction on some resource and xml files.  In the future, DAMGC related feature can be mergetd into **CCFE_Toolbox**, a guide will be provided here to merge.

+ Copy constructor code of Dagmc_Toolbox_AddIn.cs (the source addin) into CCFE_Toolbox_AddIn.cs (the target addin)
+ Copy all DAGMC related business code,  add these files as **Existing files** in the target AddIn's C# project in visual studio
+ Resources:  icon files, Ribbon.xml,  Manifest.xml will be updagted automatically?
+ Properties/

### API.V19 is needed for GeometryCheck

V18 may be used by call a script with GeometryCheck API in IronPython 
Alternative, use Internal method of V18.

### Parallel processing

It might be that API in Scripting namespace, may can only run in MainThread.

### Debug and Unit test

In visual studio, select SpaceClaim.exe as the start up propraom in the  "Debug" page in a project property.
Start the debug process normally, the debug should stop at breakpoint in the user source code file as user operates 

SpaceClaim.exe has batch mode to run IronPython script, which may be used for unit test..