# ThreadDumper
Simple tool to dump C# thread callstacks

## Running

Can be ran like the following as long as dotnet SDK is installed:

```
dotnet run -- PID PATH_TO_SYMBOLS.pdb
```

When PID is replaced with the target process id and a path to the
wanted assembly dll symbols are provided (to get accurate line
information). If symbols aren't available that parameter can be left
out to get a slightly less informative dump.
