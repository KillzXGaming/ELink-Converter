using System;
using System.IO;
using System.Linq;

namespace ELinkConverter
{
    class Program
    {
        class CommandLineArgs
        {
            public bool ExtractAll = false;
            public bool InjectAll = false;
            public string OutputFilePath = "elinkNEW.bin";
            public string InputFilePath = "elink.bin";
            public string FolderDump = "ELink";
        }

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine($"Tool by KillzXGaming.");
                Console.WriteLine($"Usage:");
                Console.WriteLine($"-f filePathHere (the elink.bin file path)");
                Console.WriteLine($"-o filePathHere (the elink.bin output file path for injecting)");
                Console.WriteLine($"-i (Inject files from ELink folder)");
                Console.WriteLine($"-e (Extract files into ELink folder)");
                return;
            }

            CommandLineArgs argData = new CommandLineArgs();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-i") argData.InjectAll = true;
                if (args[i] == "-e") argData.ExtractAll = true;
                if (args[i] == "-f") argData.InputFilePath = args[i + 1];
                if (args[i] == "-o") argData.OutputFilePath = args[i + 1];
            }

            if (!Directory.Exists(argData.FolderDump))
                Directory.CreateDirectory(argData.FolderDump);

            Console.WriteLine($"Loading elink file.");

            ELink link = new ELink(argData.InputFilePath);

            if (argData.InjectAll)
            {
                Console.WriteLine($"Injecting files..");

                foreach (var file in Directory.GetFiles(argData.FolderDump)) {

                    Console.WriteLine($"Injecting {Path.GetFileNameWithoutExtension(file)}");

                    var effectList = UserHeader.Import(file);
                    effectList.Name = Path.GetFileNameWithoutExtension(file);

                    if (link.Effects.ContainsKey(effectList.Name))
                        link.Effects.Remove(effectList.Name);

                    link.Effects.Add(effectList.Name, effectList);
                }

                Console.WriteLine($"Saving Elink file...");

                link.Save(argData.OutputFilePath);
            }
            if (argData.ExtractAll)
            {
                Console.WriteLine($"Extracting files..");

                foreach (var effect in link.Effects.Values)
                    effect.Export($"{argData.FolderDump}\\{effect.Name}.json");
            }

            Console.WriteLine($"Finshed!");
        }
    }
}
