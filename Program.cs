// A tool for dumping the C# thread stacktraces from a stuck process

using System.Text;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Symbols;

var targetId = int.Parse(args[0]);

ManagedSymbolModule? symbolInfo = null;

if (args.Length > 1)
{
    var symbolReader = new SymbolReader(Console.Out);

    // Load any symbols
    symbolReader.SecurityCheck = _ => true;

    Console.WriteLine("Using symbol file: " + args[1]);
    symbolInfo = symbolReader.OpenSymbolFile(args[1]);
}

bool printPdbDebug = false;

if (args.Length > 2)
{
    printPdbDebug = bool.Parse(args[2]);
}

Console.WriteLine("Will dump threads of: " + targetId);

using (var target = DataTarget.CreateSnapshotAndAttach(targetId))
{
    var runtime = target.ClrVersions.First().CreateRuntime();

    // We can't get the thread name from the ClrThead objects, so we'll look for
    // Thread instances on the heap and get the names from those.
    var threadNameLookup = new Dictionary<int, string?>();
    foreach (var obj in runtime.Heap.EnumerateObjects())
    {
        if (!(obj.Type is null) && obj.Type.Name == "System.Threading.Thread")
        {
            // For finding new field names if they got updated
            // Console.WriteLine("Fields: " + string.Join(" ", obj.Type.Fields.Select(f => f.Name)));

            var threadId = obj.ReadField<int>("_managedThreadId");
            var threadName = obj.ReadStringField("_name");
            threadNameLookup[threadId] = threadName;
        }
    }

    var stringBuilder = new StringBuilder();

    foreach (var thread in runtime.Threads)
    {
        string? threadName = threadNameLookup.GetValueOrDefault(thread.ManagedThreadId, "Unknown");

        stringBuilder.AppendLine(
            $"ManagedThreadId: {thread.ManagedThreadId}, Name: {threadName}, OSThreadId: {thread.OSThreadId}, Thread: IsAlive: {thread.IsAlive}, State: {thread.State}");

        if (thread.CurrentException != null)
            stringBuilder.AppendLine(
                $"Thread has an exception: {thread.CurrentException.Type.Name}: {thread.CurrentException.Message}");

        int i = 0;

        foreach (var clrStackFrame in thread.EnumerateStackTrace())
        {
            ++i;

            if (clrStackFrame.Method == null)
            {
                stringBuilder.AppendLine($"{i}:\t[internal]");
                continue;
            }

            if (printPdbDebug && symbolInfo != null && clrStackFrame.Method.Type.Module.Pdb != null)
            {
                Console.WriteLine("PDB path: " + clrStackFrame.Method.Type.Module.Pdb.Path);
                Console.WriteLine("needs to math: " + symbolInfo.SymbolFilePath);
            }

            // Make sure only read the source info if we are using the relevant pdb file
            if (symbolInfo != null && clrStackFrame.Method.Type.Module.Pdb != null &&
                clrStackFrame.Method.Type.Module.Pdb.Path == symbolInfo.SymbolFilePath)
            {
                int ilOffset = -1;
                foreach (var map in clrStackFrame.Method.ILOffsetMap.OrderBy(m => m.StartAddress))
                {
                    // Sort should make the second condition not required
                    if (map.StartAddress <=
                        clrStackFrame.InstructionPointer /*&& map.EndAddress <= clrStackFrame.InstructionPointer*/)
                    {
                        ilOffset = map.ILOffset;
                    }
                }

                if (ilOffset >= 0)
                {
                    try
                    {
                        var source =
                            symbolInfo.SourceLocationForManagedCode((uint)clrStackFrame.Method.MetadataToken, ilOffset);

                        if (source != null)
                        {
                            stringBuilder.AppendLine(
                                $"{i}:\t{clrStackFrame.Method} at {source.SourceFile.BuildTimeFilePath}:{source.LineNumber}");
                            continue;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Cannot read symbols for method \"{clrStackFrame.Method}\": {e}");
                    }
                }
                else
                {
                    Console.WriteLine($"Didn't find IL offset for method \"{clrStackFrame.Method}\"");
                }
            }

            stringBuilder.AppendLine($"{i}:\t{clrStackFrame.Method}");
        }

        stringBuilder.AppendLine();
    }

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine("Thread stacktraces:");
    Console.Write(stringBuilder);
    Console.WriteLine();
    Console.WriteLine("End of threads");
}
