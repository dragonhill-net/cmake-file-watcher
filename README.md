# CMakeFileWatcher

## Installation

The CMakeFileWatcher is indented to be used with the `dotnet tool` command.

Steps to install it as local tool in a project:
- If not present create a tool manifest (execute at project root): `dotnet new tool-manifest`
- Install the tool: `dotnet tool install --local Dragonhill.CMakeFileWatcher`
- Run the tool: `dotnet tool cmake-file-watcher`

## Configuration

Add a `.config/cmake-file-watcher-config.yaml` file to your project (the .config folder has to be in a common root directory for all watched paths).

### Example

#### Config file: .config/cmakeFileWatcherConfig.yaml
```
roots:
  - path: subdirectory-a
    generatedFilePath: subdirectory-a/list.cmake
    patternGroups:
      - extensions: [cpp, h]
        listName: dep_list
      - extensions: [po]
        listName: translation_list
```

#### File tree

```
root/
├─ demo/
│  ├─ module/
│  │  ├─ module.cpp
│  │  ├─ module.h
│  ├─ translation/
│  │  ├─ english.po
│  ├─ main.cpp
│  ├─ readme.txt
├─ .config
│  ├─ cmakeFileWatcherConfig.yaml
```

#### Resulting file: demo/list.cmake

```
list(APPEND dep_list
    "main.cpp"
    "module/module.cpp"
    "module/module.h"
)

list(APPEND translation_list
    "translation/english.po"
)

```

## Usage

Execute the tool (tool name is `cmake-file-watcher`) in the directory where the `.config` folder is.

## Creating a release of the tool itself

### With a release git tag available

```
dotnet msbuild -t:ReleasePackGitTag
```

### Manual version specification

```
dotnet msbuild /p:Version=1.0.0-pre1 -t:pack
```
