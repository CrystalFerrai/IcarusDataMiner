Command line program to extra data from the survival game Icarus.

This is mainly just for my own use and is also used in some capacity to inform the [Icarus Intel](https://icarusintel.com) interactive map..

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