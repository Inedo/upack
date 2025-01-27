# ⚠ This Library has been Discontinued ⚠

`upack`  has been discontinued in favor of `pgutil upack`. The commands are similar, but simpler:
* https://github.com/Inedo/pgutil
* https://docs.inedo.com/docs/proget/reference-api/proget-pgutil

Please post issues or discuss at https://forums.inedo.com/

-----------------------------------


## What is (was) upack?

upack is a command-line tool used to create and install universal packages; you can also see which packages are installed on a machine.

## Installation

##### Use dotnet tool install:

upack is available as a dotnet tool. You can install it using `dotnet tool install upack` and then execute it using `dotnet upack`.

##### Download Tool:

If you don't want to use upack as a dotnet tool, you can download it from the [GitHub Releases Page](https://github.com/Inedo/upack/releases) instead. You can either download a self-contained binary that targets your specific operating system, or a generic distribution that requires an installed .net version.

Self-contained OS-specific downloads have no external dependencies.

## Command Line Reference

##### On Windows:

    upack «command»

##### On Linux:

When running the .NET Core version of upack on Linux, you will need to use `dotnet upack.dll` instead of `upack`.

    dotnet upack.dll «command»

Where command is one of the following:

### pack

Creates a new universal package using specified metadata and source directory.
    
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

Pushes a universal package to the specified feed.

    upack push «package» «target» [--user=«authentication»]

 - **`package`** - Path of a valid .upack file.
 - **`target`** - URL of a upack API endpoint. If not specified, the `UPACK_FEED` environment variable is used.
 - `user` - Credentials to use for servers that require authentication. This can be either `«username»:«password»` or `api:«api-key»`

### unpack

Extracts the contents of a universal package to a directory.

    upack unpack «package» «target» [--overwrite]

 - **`package`** - Path of a valid .upack file.
 - **`target`** - Directory where the contents of the package will be extracted.
 - `overwrite` - When specified, overwrite files in the target directory.

### install

Downloads the specified universal package and extracts its contents to a directory.

    upack install «package» [«version»] --source=«source» --target=«target» [--user=«authentication»] [--comment=«comment»] [--overwrite] [--prerelease] [--userregistry] [--unregistered] [--cache]

 - **`package`** - Package name and group, such as group/name.
 - `version` - Package version. Supports wildcards (*). If not specified, the latest version is retrieved.
 - `source` - URL of a upack API endpoint. If not specified, the `UPACK_FEED` environment variable is used.
 - `target` - Directory where the contents of the package will be extracted.
 - `user` - Credentials to use for servers that require authentication. This can be either `«username»:«password»` or `api:«api-key»`. If not specified, the `UPACK_USER` environment variable is used.
 - `overwrite` - When specified, Overwrite files in the target directory.
 - `prerelease` - When version is not specified, will install the latest prerelase version instead of the latest stable version.
 - `comment` - The reason for installing the package, for the local registry.
 - `userregistry` - Register the package in the user registry instead of the machine registry.
 - `unregistered` - Do not register the package in a local registry.
 - `cache` - Cache the contents of the package in the local registry.

### update

Update the specified universal package.

    upack update «package» [«version»] --source=«source» --target=«target» [--user=«authentication»] [--comment=«comment»] [--prerelease] [--userregistry] [--unregistered] [--cache] [--force] [--check]

 - **`package`** - Package name and group, such as group/name.
 - `version` - Package version. Supports wildcards (*). If not specified, the latest version is retrieved.
 - `source` - URL of a upack API endpoint. If not specified, the URL in registry or `UPACK_FEED` environment variable is used.
 - `target` - (Optional) Directory where the package is installed.
 - `user` - Credentials to use for servers that require authentication. This can be either `«username»:«password»` or `api:«api-key»`. If not specified, the `UPACK_USER` environment variable is used.
 - `prerelease` - When version is not specified, will install the latest prerelase version instead of the latest stable version.
 - `comment` - The reason for updating the package, for the local registry.
 - `userregistry` - Register the package in the user registry instead of the machine registry.
 - `unregistered` - Do not register the package in a local registry.
 - `cache` - Cache the contents of the package in the local registry.
 - `clean` - Delete the directory of the package to perform a clean update.
 - `force` - Force the update even if it's already up-to-date.
 - `check` - Checks for new package update.

### remove

Remove the specified universal package.

    upack remove «package» --target=«target» [--userregistry] [--rmregistry]

 - **`package`** - Package name and group, such as group/name.
 - `target` - (Optional) Directory where the package to be removed is located.
 - `userregistry` - Use this if you registered the package installation using the user registry instead of the machine registry.
 - `rmregistry` - Use this if you need to remove a remaining registry from a package that has been deleted without using the tool. The command will still check if the package exists to remove it as well.


### get

Downloads a universal package from a feed without installing it.

    upack get «package» [«version»] --source=«source» --target=«target» [--user=«authentication»] [--overwrite] [--prerelease]

 - **`package`** - Package name and group, such as group/name.
 - `version` - Package version. Supports wildcards (*). If not specified, the latest version is retrieved.
 - `source` - URL of a upack API endpoint. If not specified, the `UPACK_FEED` environment variable is used.
 - `target` - Directory where the contents of the package will be extracted.
 - `user` - Credentials to use for servers that require authentication. This can be either `«username»:«password»` or `api:«api-key»`. If not specified, the `UPACK_USER` environment variable is used.
 - `overwrite` - When specified, overwrite files in the target directory.
 - `prerelease` - When version is not specified, will download the latest prerelase version instead of the latest stable version.

### list

Lists packages installed in the local registry.

    upack list [--userregistry]

 - `userregistry` - List packages in the user registry instead of the machine registry.

### repack

Creates a new universal package by repackaging an existing package with a new version number and audit information.

    upack repack «source» [--newVersion=«newVersion»] [--targetDirectory=«targetDirectory»] [--note=«auditNote»] [--overwrite] 

 - **`source`** - The path of the existing upack file.
 - `newVersion` - New package version to use.
 - `targetDirectory` - Directory where the .upack file will be created. If not specified, the current working directory is used. 
 - `note` - A description of the purpose for repackaging that will be entered as the audit note.
 - `overwrite` - Overwrite existing package file if it already exists.

### verify

Verifies that a specified package hash matches the hash stored in a universal feed.

    upack verify «package» «source» [--user=«authentication»]

 - **`package`** - Path of a valid .upack file.
 - **`source`** - URL of a upack API endpoint. If not specified, the `UPACK_FEED` environment variable is used.
 - `user` - Credentials to use for servers that require authentication. This can be either `«username»:«password»` or `api:«api-key»`. If not specified, the `UPACK_USER` environment variable is used.

### hash

Calculates the SHA1 hash of a local package and writes it to standard output.

    upack hash «package»

 - **`package`** - Path of a valid .upack file.

### metadata

Displays metadata for a remote universal package.

    upack metadata «package» [«version»] --source=«source» [--user=«authentication»] [--file=«file»]

 - **`package`** - Package name and group, such as group/name.
 - `version` - Package version. Supports wildcards (*). If not specified, the latest version is retrieved.
 - **`source`** - URL of a upack API endpoint. If not specified, the `UPACK_FEED` environment variable is used.
 - `user` - Credentials to use for servers that require authentication. This can be either `«username»:«password»` or `api:«api-key»`. If not specified, the `UPACK_USER` environment variable is used.
 - `file` - The metadata file to display relative to the .upack root; the default is upack.json.

### version

Outputs the installed version of upack.

    upack version
