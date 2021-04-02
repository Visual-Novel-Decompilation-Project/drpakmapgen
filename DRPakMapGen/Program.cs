using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace DRPakMapGen {
    public class Options
    {
        [Option('f', "file", Required = true, HelpText = "EXE file")]
        public string File { get; set; }
    }
    
    public class FileEntry {
        public string PakName;
        public List<string> files = new List<string>();
    }

    public struct OffsetTable {
        public int PakOffset;
        public int FinalArrayEntry;
        public int DataDelta;
        public int RDataDelta;
    }

    class Program {
        // Define our offsets table.
        private const int Dr1UsPakOffset = 0x28_AAB8;
        private const int Dr1UsFinalArrayEntry = 0x28_B8D8;
        private const int Dr1UsDataDelta = 0x40_1C00;
        private const int Dr1UsRDataDelta = 0x40_1200;

        
        private const int Dr2UsPakOffset = 0x2F_DB78;
        private const int Dr2UsFinalArrayEntry = 0x2F_E9A0;
        private const int Dr2UsDataDelta = 0x40_1C00;
        private const int Dr2UsRDataDelta = 0x40_1600;
        
        private const string BgConst = "bg_";
        
        static void Main(string[] args) {
            // Parse command line arguments.
            var arg = Parser.Default.ParseArguments<Options>(args);
            if (arg.Errors.Any())
                return;
            
            var opt = arg.Value;

            using (var file = File.OpenRead(opt.File)) {
                var filename = Path.GetFileName(opt.File);
                
                if (filename == "DR1_us.exe") {
                    ExportMapping(
                        file, 
                        "dr1_us.mappings.json", 
                        new Span<byte>(new byte[] { 0xC0, 0x99, 0x65, 0x00 }), 
                        new OffsetTable { PakOffset = Dr1UsPakOffset, FinalArrayEntry = Dr1UsFinalArrayEntry, DataDelta = Dr1UsDataDelta, RDataDelta = Dr1UsRDataDelta});
                }
                else if (filename == "DR2_us.exe") {
                    ExportMapping(
                        file, 
                        "dr2_us.mappings.json", 
                        new Span<byte>(new byte[] { 0xD0, 0x8D, 0x6C, 0x00 }),
                        new OffsetTable { PakOffset = Dr2UsPakOffset, FinalArrayEntry = Dr2UsFinalArrayEntry, DataDelta = Dr2UsDataDelta, RDataDelta = Dr2UsRDataDelta});
                }
                else
                    Console.WriteLine("Not a valid DR EXE (Only valid with `_us` exes if you want to try.");
            }
        }

        static void ExportMapping(Stream stream, string mappingFileName, Span<byte> eof, OffsetTable offsets) {
            stream.Seek(0, SeekOrigin.Begin);
            
            Stopwatch s = new Stopwatch();
            s.Start();
            
            List<FileEntry> fileEntries = new List<FileEntry>();

            var buffer = new Span<byte>(new byte[4]);
            var zeroByte = new Span<byte>(new byte[] {0, 0, 0, 0});

            int currentListingIter;
            int currentIter = 0;

            // Seek to start of the root pak listing.
            stream.Seek(offsets.PakOffset, SeekOrigin.Begin);
            currentListingIter = offsets.PakOffset;
            var currentFileListIterOffset = 0;

            while (true) {
                // Console.WriteLine("Iter at {0} | FO: {2} | SP: {3} | {1} ", currentIter, s.Elapsed, currentFileListIterOffset, stream.Position);
                Console.WriteLine("Parsing {0}.pak", currentIter);
                // If we're at the final entry, we're done.
                if (currentListingIter == offsets.FinalArrayEntry)
                    break;

                stream.Seek(currentListingIter, SeekOrigin.Begin);

                // Get the reference to the file list.
                stream.Read(buffer);

                StringBuilder bdebug = new StringBuilder();
                buffer.ToArray().ToList().ForEach(x => bdebug.Append(x.ToString("X")));
                //Console.WriteLine("VA: {0}", bdebug);

                // end of listing
                if (buffer.SequenceEqual(eof))
                    break;
                if (buffer.SequenceEqual(zeroByte)) {
                    currentListingIter += 4;
                    currentIter++;
                    continue;
                }

                // Convert it to an int offset we can use.
                currentFileListIterOffset = BitConverter.ToInt32(buffer);
                currentFileListIterOffset -= offsets.DataDelta;

                // Seek to that offset.
                stream.Seek(currentFileListIterOffset, SeekOrigin.Begin);

                // Create a new entry.
                FileEntry e = new FileEntry();
                e.PakName = BgConst + currentIter.ToString("000");

                while (true) {
                    stream.Read(buffer);

                    // eof
                    if (buffer.SequenceEqual(eof))
                        break;

                    // Seek to the string entry.
                    var currentStringListing = BitConverter.ToInt32(buffer);

                    currentStringListing = currentStringListing - offsets.RDataDelta;

                    stream.Seek(currentStringListing, SeekOrigin.Begin);

                    // Read string.
                    StringBuilder b = new StringBuilder();
                    while (true) {
                        int bit = stream.ReadByte();

                        if (bit == 0)
                            break;

                        b.Append(Convert.ToChar(bit));
                    }
                    e.files.Add(b.ToString());
                    b.Clear();

                    // Move the iter offset by 4.
                    currentFileListIterOffset += 4;
                    stream.Seek(currentFileListIterOffset, SeekOrigin.Begin);
                }

                fileEntries.Add(e);

                // Move the total iter.
                currentIter++;
                currentListingIter += 4;
            }

            s.Stop();
            var fcnt = 0;
            fileEntries.ForEach(x => fcnt += x.files.Count);
            Console.WriteLine("Got {0} paks, {1} files in {2}", fileEntries.Count, fcnt, s.Elapsed);

            var dict = fileEntries.ToDictionary(item => item.PakName, item => item.files);
            File.WriteAllText(mappingFileName, System.Text.Json.JsonSerializer.Serialize(dict));
        }
    }
}