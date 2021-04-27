#  Dagmc Toolbox for SpaceClaim

**Export MOAB DAGMC Neutronics simulation workflow in SpaceClaim, i.e. the same feature as Trelis DAGMC plugin in C++**

Qingfeng Xia
Copyright 2021 United Kingdom Atomic Energy Agency (UKAEA)
license: MIT


## Installation guide

Download the zipped Addin bundle for a specific SpaceClaim version (v19)

Extract to a specific location recongized by SpaceClaim

`C:\ProgramData\SpaceClaim\AddIns\Samples\V19` it is a user folder, can be created without admin prevliedge.

Ensure all dll files are located in `Dagmc_Toolbox` subfolder of `C:\ProgramData\SpaceClaim\AddIns\Samples\V19`

Start SpaceClaim.exe and look for a new Ribbon menu called `Dagmc_Toolbox`

### Compile for a different SpaceClaim version

There is no easy way until `MOABSharp` is packaged.

### Relation with CCFE_Toolbox Addin

Due to DAGMC complicate dependencies (native dll files are used), it is decided to make it an independent Addin.

For the time being, a new AddIn **DAGMC_Toolbox** ( output a new SpaceClaim AddIn), instead of merge into CCFE_Toolbox.dll at this development stage,
to avoid merge confliction on some resource and xml files.  In the future, DAMGC related feature can be mergetd into **CCFE_Toolbox**, a guide will be provided here to merge.

+ Copy constructor code of Dagmc_Toolbox_AddIn.cs (the source addin) into CCFE_Toolbox_AddIn.cs (the target addin)
+ Copy all DAGMC related business code,  add these files as **Existing files** in the target AddIn's C# project in visual studio
+ Resources:  icon files, Ribbon.xml,  Manifest.xml will be updagted automatically?
+ Properties/


## Usage
