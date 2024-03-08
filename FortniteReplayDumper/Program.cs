using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace FortniteReplayDumper
{
    class Program
    {
        private static ReplayDumper _dumper = new ReplayDumper();

        static void Main(string[] args)
        {
            Console.WriteLine("=== Dumper Source: https://github.com/SL-x-TnT/FortniteReplayDumper ===");
            Console.WriteLine("=== Parser Source: https://github.com/Shiqan/FortniteReplayDecompressor ===");
            Console.WriteLine();

            if (args.Length == 0)
            {
                HandleSavedReplays();
            }
            else
            {
                DumpFile(args[0]);
            }

            Console.ReadLine();
        }

        static void HandleSavedReplays()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string demoFolder = Path.Combine(localAppData, "FortniteGame", "Saved", "Demos");

            if (!Directory.Exists(demoFolder))
            {
                Console.WriteLine($"Failed to find demo folder. Drag and drop replay file onto exe");

                return;
            }

            List<Tuple<string, Action>> options = new List<Tuple<string, Action>>();

            string[] replays = Directory.GetFiles(demoFolder, "*.replay");

            options.Add(new Tuple<string, Action>("All", () =>
                {
                    foreach(string replay in replays)
                    {
                        DumpFile(replay);
                    }
                }));

            foreach (string replay in replays)
            {
                options.Add(new Tuple<string, Action>(replay, () =>
                    {
                        DumpFile(replay);
                    }));
            }

            for (int i =0; i < options.Count;i++)
            {
                Console.WriteLine($"{i}: {options[i].Item1}");
            }

            int choice = -1;

            do
            {

                Console.Write("Choose file to dump: ");
                string strChoice = Console.ReadLine();

                if(!Int32.TryParse(strChoice, out choice))
                {
                    choice = -1;
                }

            } while (choice < 0 || choice >= options.Count);

            Console.Clear();

            options[choice].Item2();
        }

        static void DumpFile(string file)
        {
            if(!File.Exists(file))
            {
                Console.WriteLine($"File {file} doesn't exist");

                return;
            }

            ConsoleColor oldColor = Console.ForegroundColor;

            try
            {
                Console.WriteLine($"Dumping file: {file}");

                FileInfo fileInfo = new FileInfo(file);

                string destinationFile = Path.Combine(fileInfo.Directory.FullName, "dumped_" + fileInfo.Name);

                _dumper.DumpReplay(file, destinationFile);

                Console.ForegroundColor = ConsoleColor.Green;

                Console.WriteLine($"File dumped to: {destinationFile}");
            }
            catch(Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine($"Failed to dump file {file}");
                Console.WriteLine(ex.Message);
            }
            finally
            {
                Console.ForegroundColor = oldColor;
            }
        }
    }
}
