Command line program to extra data from the game Icarus: First Cohort. This is mainly just for my own use to update data each week when the game updates.

I use the output of this to update various Google sheets located in [this folder](https://drive.google.com/drive/u/0/folders/1u5mE7nfmRmxpvYBbmeWl1gAIoOxvmtRE).

## Usage

Run the program with no parameters to print the usage.
```
Usage: IcarusDataMiner [content dir] [output dir] [[miners]]

  content dir   Path the the game's Content directory (Icarus/Content)

  output dir    Path to directory where mined data will be output

  miners        (Optional) Comma separated list of miners to run. If not
                specified, all default miners will run. Specify 'all' to force
                all miners to run.
```

This will also print a list of the names of all available miners.

## Releases

There are no releases of this tool for the time being. If you wish to try it, you will need to build it.

## Building

Clone the repository, including submodules.
```
git clone --recursive https://github.com/CrystalFerrai/IcarusDataMiner.git
```

You can then open and build IcarusDataMiner.sln.