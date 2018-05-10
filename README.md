# upack.exe

upack.exe *(or upack.go)* is a command-line tool used to create and install universal packages; you can also see which packages are installed on a machine.

## Installation

The upack command-line tool does not require any installation; you can download directly from the [GitHub Releases Page](https://github.com/Inedo/upack/releases) or as a [Chocolatey Package](https://chocolatey.org/packages/upack).

## Command Line Reference

    upack «command»

Where command is one of the following:

### pack

Creates a new ProGet universal package using specified metadata and source directory.
    
    upack pack «source» [--metadata=«metadata»] [--targetDirectory=«targetDirectory»] [--group=«group»] [--name=«name»] [--version=«version»] [--title=«title»] [--description=«description»] [--icon=«icon»]

 - **`source`** - Directory containing files to add to the package.
 - `metadata` - Path of a valid upack.json metadata file.
 - `targetDirectory` - Directory where the .upack file will be created. If not specified, the current working directory is used.
 - `group` - Package group. If metadata file is provided, value will be ignored.
 - `name` - Package name. If metadata file is provided, value will be ignored.
 - `version` - Package version. If metadata file is provided, value will be ignored.
 - `title` - Package title. If metadata file is provided, value will be ignored.
 - `description` - Package description. If metadata file is provided, value will be ignored.
 - `icon` - Icon absolute Url. If metadata file is provided, value will be ignored.

### push

Pushes a ProGet universal package to the specified ProGet feed.

    upack push «package» «target» [--user=«authentication»]

 - **`package`** - Path of a valid .upack file.
 - **`target`** - URL of a upack API endpoint.
 - `user` - User name and password to use for servers that require authentication. Example: username:password

### unpack

Extracts the contents of a ProGet universal package to a directory.

    upack unpack «package» «target» [--overwrite]

 - **`package`** - Path of a valid .upack file.
 - **`target`** - Directory where the contents of the package will be extracted.
 - `overwrite` - When specified, overwrite files in the target directory.


### install

Downloads the specified ProGet universal package and extracts its contents to a directory.

    upack install «package» [«version»] --source=«source» --target=«target» [--user=«user»] [--comment=«comment»] [--overwrite] [--prerelease] [--userregistry] [--unregistered] [--cache]

 - **`package`** - Package name and group, such as group/name.
 - `version` - Package version. If not specified, the latest version is retrieved.
 - `source` - URL of a upack API endpoint.
 - `target` - Directory where the contents of the package will be extracted.
 - `user` - User name and password to use for servers that require authentication. Example: username:password
 - `overwrite` - When specified, Overwrite files in the target directory.
 - `prerelease` - When version is not specified, will install the latest prerelase version instead of the latest stable version.
 - `comment` - The reason for installing the package, for the local registry.
 - `userregistry` - Register the package in the user registry instead of the machine registry.
 - `unregistered` - Do not register the package in a local registry.
 - `cache` - Cache the contents of the package in the local registry.

### list

Lists packages installed in the local registry.

    upack list [--userregistry]

 - `userregistry` - List packages in the user registry instead of the machine registry.

### repack

Creates a new ProGet universal package from an existing package with optionally modified metadata.

    upack repack «source» [--manifest=«manifest»] [--targetDirectory=«targetDirectory»] [--group=«group»] [--name=«name»] [--version=«version»] [--title=«title»] [--description=«description»] [--icon=«icon»] [--overwrite]

 - **`source`** - The path of the existing upack file.
 - `manifest` - Path of upack.json file to merge.
 - `targetDirectory` - Directory where the .upack file will be created. If not specified, the current working directory is used.
 - `group` - Package group. If metadata file is provided, value will be ignored.
 - `name` - Package name. If metadata file is provided, value will be ignored.
 - `version` - Package version. If metadata file is provided, value will be ignored.
 - `title` - Package title. If metadata file is provided, value will be ignored.
 - `description` - Package description. If metadata file is provided, value will be ignored.
 - `icon` - Icon absolute Url. If metadata file is provided, value will be ignored.
 - `overwrite` - Overwrite existing package file if it already exists.


### verify

Verifies that a specified package hash matches the hash stored in a ProGet Universal feed.

    upack verify «package» «source» [--user=«user»]

 - **`package`** - Path of a valid .upack file.
 - **`source`** - URL of a upack API endpoint.
 - `user` - User name and password to use for servers that require authentication. Example: username:password

### hash

Calculates the SHA1 hash of a local package and writes it to standard output.

    upack hash «package»

 - **`package`** - Path of a valid .upack file.