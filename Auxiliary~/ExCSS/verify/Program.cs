using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// verify <OneJS.ExCSS.dll> <OneJS.ExCSS.pdb> <source folder>: exit 0 when the PDB is portable, every
// source file is in it, embedded, under a /_/ path, and the DLL points at it the same way; 1 otherwise.
static class Verify {
    static readonly Guid EmbeddedSource = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    static int Main(string[] args) {
        var failures = new List<string>();
        using (var pe = new PEReader(File.OpenRead(args[0]))) {
            var entries = pe.ReadDebugDirectory();
            if (!entries.Any(e => e.Type == DebugDirectoryEntryType.Reproducible)) failures.Add("the DLL is not marked deterministic (no Reproducible entry)");
            if (!entries.Any(e => e.Type == DebugDirectoryEntryType.PdbChecksum)) failures.Add("the DLL carries no PDB checksum");
            var codeView = entries.Where(e => e.Type == DebugDirectoryEntryType.CodeView).Select(e => pe.ReadCodeViewDebugDirectoryData(e).Path).ToList();
            if (codeView.Count != 1 || !codeView[0].StartsWith("/_/")) failures.Add($"the DLL's PDB path is {string.Join(", ", codeView)}, not mapped to /_/");
        }

        var bytes = File.ReadAllBytes(args[1]);
        if (bytes.Length < 4 || bytes[0] != (byte)'B' || bytes[1] != (byte)'S' || bytes[2] != (byte)'J' || bytes[3] != (byte)'B') failures.Add("the PDB is not a portable PDB");
        else {
            using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(bytes));
            var pdb = provider.GetMetadataReader();
            var embedded = new HashSet<DocumentHandle>();
            foreach (var h in pdb.CustomDebugInformation) {
                var cdi = pdb.GetCustomDebugInformation(h);
                if (cdi.Parent.Kind == HandleKind.Document && pdb.GetGuid(cdi.Kind) == EmbeddedSource) embedded.Add((DocumentHandle)cdi.Parent);
            }
            var names = new List<string>();
            foreach (var h in pdb.Documents) {
                var name = pdb.GetString(pdb.GetDocument(h).Name);
                names.Add(name);
                if (!name.StartsWith("/_/")) failures.Add($"{name}: not mapped to /_/");
                if (!embedded.Contains(h)) failures.Add($"{name}: its source is not embedded");
            }
            var sources = Directory.GetFiles(args[2], "*.cs", SearchOption.AllDirectories).Length;
            var fromSource = names.Count(n => n.StartsWith("/_/src/") && n.EndsWith(".cs"));
            if (fromSource != sources) failures.Add($"the PDB names {fromSource} source files under /_/src/, the folder holds {sources}");
            Console.WriteLine($"PDB: portable, {names.Count} documents, {embedded.Count} with embedded source, {fromSource} of {sources} source files");
        }

        foreach (var f in failures.Take(20)) Console.WriteLine("FAIL " + f);
        if (failures.Count > 20) Console.WriteLine($"FAIL and {failures.Count - 20} more");
        return failures.Count == 0 ? 0 : 1;
    }
}
