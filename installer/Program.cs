using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("ArcaneThai Installer")]
[assembly: AssemblyDescription("Thai installer for ArcaneMP menus and Hogwarts Legacy game text")]
[assembly: AssemblyVersion("0.6.1.0")]
[assembly: AssemblyFileVersion("0.6.1.0")]

namespace ArcaneThai
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            if (args.Length >= 2 && args[0] != "--render") {
                try {
                    object result;
                    switch (args[0]) {
                        case "--inspect": result = PatchEngine.Inspect(args[1]); break;
                        case "--install": result = PatchEngine.Install(args[1]); break;
                        case "--restore": result = PatchEngine.Restore(args[1]); break;
                        case "--game-inspect": result = GameThaiEngine.Inspect(args[1], args.Length >= 4 ? args[3] : "arcanemp"); break;
                        case "--game-install": result = GameThaiEngine.Install(args[1], args.Length >= 4 ? args[3] : "arcanemp"); break;
                        case "--game-uninstall": result = GameThaiEngine.Uninstall(args[1], args.Length >= 4 ? args[3] : "arcanemp"); break;
                        case "--extract":
                            if (args.Length < 3) throw new ArgumentException("Specify an output HTML file.");
                            File.WriteAllText(args[2], PatchEngine.ExtractForTests(PatchEngine.ResolveTarget(args[1])), new System.Text.UTF8Encoding(false));
                            return 0;
                        default: throw new ArgumentException("Unknown command.");
                    }
                    string json = PatchEngine.Serialize(result);
                    if (args.Length >= 3) File.WriteAllText(args[2], json, new System.Text.UTF8Encoding(false));
                    else Console.WriteLine(json);
                    return 0;
                } catch (Exception error) {
                    string json = PatchEngine.Serialize(new { Error = error.Message });
                    if (args.Length >= 3) File.WriteAllText(args[2], json, new System.Text.UTF8Encoding(false));
                    else Console.Error.WriteLine(json);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new InstallerForm()) {
                if (args.Length >= 2 && args[0] == "--render") {
                    form.RenderPreview(args[1], args.Length >= 3 ? args[2] : "menu");
                    return 0;
                }
                Application.Run(form);
            }
            return 0;
        }
    }

}
