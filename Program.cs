using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;

namespace JarDecompiler
{
    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        const int STD_OUTPUT_HANDLE = -11;
        const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 4;

        static void EnableAnsi()
        {
            try
            {
                IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
                uint mode;
                if (GetConsoleMode(handle, out mode))
                {
                    mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                    SetConsoleMode(handle, mode);
                }
            }
            catch { }
        }

        static void PrintColor(string text, ConsoleColor color)
        {
            ConsoleColor old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = old;
        }

        static void PrintColorLine(string text, ConsoleColor color)
        {
            PrintColor(text + Environment.NewLine, color);
        }

        static void PrintHeader()
        {
            try { Console.Clear(); } catch { }
            PrintColorLine("==================================================================", ConsoleColor.DarkGray);
            PrintColorLine("  Vineflower Java Decompiler CLI (vfd)                           ", ConsoleColor.White);
            PrintColorLine("  Engine: Vineflower 1.12.0                                      ", ConsoleColor.DarkGray);
            PrintColorLine("==================================================================", ConsoleColor.DarkGray);
            Console.WriteLine();
        }

        static string ResolveJava(string appDir)
        {
            // 1. Embedded JRE in app folder
            string embedded = Path.Combine(appDir, "jre", "bin", "java.exe");
            if (File.Exists(embedded)) return embedded;

            // 2. JAVA_HOME environment variable
            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                string homeJava = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(homeJava)) return homeJava;
            }

            // 3. System PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string entry in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(entry.Trim(), "java.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            return null;
        }

        [STAThread]
        static void Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Console.Title = "Vineflower Decompiler (vfd)"; } catch { }
            EnableAnsi();

            PrintHeader();

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string javaExe = ResolveJava(appDir);
            string vineflowerJar = Path.Combine(appDir, "vineflower.jar");

            if (string.IsNullOrEmpty(javaExe))
            {
                PrintColorLine("[ERROR] Java runtime not found.", ConsoleColor.Red);
                PrintColorLine("Please install Java 17+ or place an OpenJDK runtime in 'jre/' directory.", ConsoleColor.DarkGray);
                WaitExit(args);
                return;
            }

            if (!File.Exists(vineflowerJar))
            {
                PrintColorLine("[ERROR] 'vineflower.jar' not found in: " + appDir, ConsoleColor.Red);
                WaitExit(args);
                return;
            }

            if (args != null && args.Length == 1 && (args[0] == "-h" || args[0] == "--help" || args[0] == "/?"))
            {
                PrintColorLine("Usage:", ConsoleColor.White);
                PrintColorLine("  vfd <archive.jar> [archive2.jar ...]", ConsoleColor.DarkGray);
                PrintColorLine("  jdecompile <archive.jar>", ConsoleColor.DarkGray);
                Console.WriteLine();
                PrintColorLine("Features:", ConsoleColor.White);
                PrintColorLine("  - Automatic output directory: '<filename>-src/'", ConsoleColor.DarkGray);
                PrintColorLine("  - Full AST resugaring: generics, switch expressions, lambdas, LVT names", ConsoleColor.DarkGray);
                PrintColorLine("  - Drag & drop supported from Windows Explorer", ConsoleColor.DarkGray);
                return;
            }

            string[] targets = args;

            // No arguments passed: prompt or open dialog
            if (targets == null || targets.Length == 0)
            {
                PrintColorLine("Usage:", ConsoleColor.White);
                PrintColorLine("  vfd <archive.jar> [archive2.jar ...]", ConsoleColor.DarkGray);
                PrintColorLine("  Drag & drop any .jar onto this executable", ConsoleColor.DarkGray);
                Console.WriteLine();

                PrintColor("Enter path to .jar (or press Enter to browse): ", ConsoleColor.Yellow);
                string input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    using (OpenFileDialog ofd = new OpenFileDialog())
                    {
                        ofd.Title = "Select JAR file to decompile";
                        ofd.Filter = "Java Archive (*.jar)|*.jar|All Files (*.*)|*.*";
                        ofd.Multiselect = true;
                        if (ofd.ShowDialog() == DialogResult.OK)
                        {
                            targets = ofd.FileNames;
                        }
                        else
                        {
                            PrintColorLine("[*] Aborted.", ConsoleColor.DarkGray);
                            return;
                        }
                    }
                }
                else
                {
                    targets = new string[] { input.Trim().Trim('\"', '\'') };
                }
            }

            int success = 0;
            int total = targets.Length;

            for (int i = 0; i < total; i++)
            {
                if (ProcessJar(targets[i], javaExe, vineflowerJar, i + 1, total))
                {
                    success++;
                }
            }

            Console.WriteLine();
            PrintColorLine("------------------------------------------------------------------", ConsoleColor.DarkGray);
            if (success == total)
            {
                PrintColorLine(string.Format("Decompile completed: {0}/{1} file(s) successful.", success, total), ConsoleColor.Green);
            }
            else
            {
                PrintColorLine(string.Format("Decompile finished with errors: {0}/{1} successful.", success, total), ConsoleColor.Yellow);
            }
            PrintColorLine("------------------------------------------------------------------", ConsoleColor.DarkGray);

            if (args.Length == 0 || IsLaunchedFromExplorer())
            {
                WaitExit(args);
            }
        }

        static bool ProcessJar(string jarPath, string javaExe, string vineflowerJar, int index, int total)
        {
            jarPath = (jarPath ?? "").Trim('\"', '\'');
            if (!File.Exists(jarPath))
            {
                PrintColorLine(string.Format("[{0}/{1}] [ERROR] File not found: {2}", index, total, jarPath), ConsoleColor.Red);
                return false;
            }

            FileInfo fi = new FileInfo(jarPath);
            if (!fi.Extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))
            {
                PrintColorLine(string.Format("[{0}/{1}] [SKIP] Not a .jar file: {2}", index, total, fi.Name), ConsoleColor.Yellow);
                return false;
            }

            string outDir = Path.Combine(fi.DirectoryName ?? ".", Path.GetFileNameWithoutExtension(fi.Name) + "-src");

            Console.WriteLine();
            PrintColorLine(string.Format("[{0}/{1}] Decompiling: {2}", index, total, fi.Name), ConsoleColor.Cyan);
            PrintColorLine(string.Format("  Size:   {0:N2} MB", fi.Length / (1024.0 * 1024.0)), ConsoleColor.DarkGray);
            PrintColorLine(string.Format("  Output: {0}", outDir), ConsoleColor.DarkGray);

            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            int threads = System.Math.Max(2, System.Environment.ProcessorCount);
            string arguments = string.Format(
                "-Xmx2G -jar \"{0}\" " +
                "--folder " +
                "--decompile-generics=true " +
                "--decompile-enums=true " +
                "--decompile-finally=true " +
                "--decompile-switch-expressions=true " +
                "--use-lvt-names=true " +
                "--use-method-parameters=true " +
                "--remove-bridge=true " +
                "--remove-synthetic=true " +
                "--hide-default-constructor=true " +
                "--resugar-idea-notnull=true " +
                "--ascii-strings=false " +
                "--thread-count={1} " +
                "\"{2}\" \"{3}\"",
                vineflowerJar,
                threads,
                fi.FullName,
                outDir
            );

            int totalClasses = 0;
            try
            {
                using (System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(fi.FullName))
                {
                    foreach (System.IO.Compression.ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".class", System.StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.IndexOf('$') < 0)
                        {
                            totalClasses++;
                        }
                    }
                }
            }
            catch
            {
                totalClasses = 0;
            }

            Stopwatch sw = Stopwatch.StartNew();

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                CreateNoWindow = true
            };

            object syncLock = new object();
            string phase = "Starting JVM...";
            string currentClass = "";
            int doneClasses = 0;
            System.Collections.Generic.List<string> errLines = new System.Collections.Generic.List<string>();
            System.Collections.Generic.List<string> warnLines = new System.Collections.Generic.List<string>();

            try
            {
                using (Process proc = new Process())
                using (System.Threading.ManualResetEventSlim outDone = new System.Threading.ManualResetEventSlim(false))
                using (System.Threading.ManualResetEventSlim errDone = new System.Threading.ManualResetEventSlim(false))
                {
                    proc.StartInfo = psi;
                    proc.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            outDone.Set();
                            return;
                        }
                        string line = e.Data.Trim();
                        if (line.Length == 0) return;

                        lock (syncLock)
                        {
                            if (line.IndexOf("Scanning ", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                phase = "Scanning...";
                            }
                            else if (line.IndexOf("Loading Class:", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                phase = "Loading...";
                            }
                            else if (line.IndexOf("Decompiling class ", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                phase = "Decompiling";
                                int idx = line.IndexOf("Decompiling class ", System.StringComparison.OrdinalIgnoreCase);
                                string cls = line.Substring(idx + 18).Trim().Replace('/', '.');
                                currentClass = cls;
                            }
                            else if (line.IndexOf("... done", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                doneClasses++;
                            }
                            else if (line.StartsWith("ERROR:", System.StringComparison.OrdinalIgnoreCase) ||
                                     line.StartsWith("SEVERE:", System.StringComparison.OrdinalIgnoreCase))
                            {
                                if (errLines.Count < 50) errLines.Add(line);
                            }
                            else if (line.StartsWith("WARN:", System.StringComparison.OrdinalIgnoreCase))
                            {
                                if (warnLines.Count < 20) warnLines.Add(line);
                            }
                        }
                    };

                    proc.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            errDone.Set();
                            return;
                        }
                        string line = e.Data.Trim();
                        if (line.Length == 0) return;

                        lock (syncLock)
                        {
                            if (errLines.Count < 100) errLines.Add(line);
                        }
                    };

                    if (!proc.Start())
                    {
                        PrintColor("  Processing... ", ConsoleColor.Yellow);
                        PrintColorLine("FAILED (could not start java process)", ConsoleColor.Red);
                        return false;
                    }

                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    char[] spinner = new char[] { '|', '/', '-', '\\' };
                    int spinIdx = 0;
                    bool interactive = !Console.IsOutputRedirected;

                    if (!interactive)
                    {
                        PrintColorLine("  Processing...", ConsoleColor.Yellow);
                    }

                    while (!proc.WaitForExit(100))
                    {
                        if (!interactive) continue;

                        string snapPhase;
                        string snapClass;
                        int snapDone;
                        lock (syncLock)
                        {
                            snapPhase = phase;
                            snapClass = currentClass;
                            snapDone = doneClasses;
                        }

                        string progressPart;
                        if (totalClasses > 0 && snapDone > 0)
                        {
                            int pct = System.Math.Min(99, (snapDone * 100) / totalClasses);
                            progressPart = string.Format("{0} {1}/{2} ({3}%)", snapPhase, snapDone, totalClasses, pct);
                        }
                        else if (snapDone > 0)
                        {
                            progressPart = string.Format("{0} {1} classes", snapPhase, snapDone);
                        }
                        else
                        {
                            progressPart = snapPhase;
                        }

                        string status = string.Format("  [{0}] {1} | {2:N1}s", spinner[spinIdx++ % spinner.Length], progressPart, sw.Elapsed.TotalSeconds);
                        if (!string.IsNullOrEmpty(snapClass))
                        {
                            status += " | " + snapClass;
                        }

                        int maxWidth = 79;
                        try { maxWidth = System.Math.Max(20, Console.WindowWidth - 1); } catch { maxWidth = 79; }
                        if (status.Length > maxWidth)
                        {
                            status = status.Substring(0, maxWidth - 3) + "...";
                        }
                        else if (status.Length < maxWidth)
                        {
                            status = status.PadRight(maxWidth);
                        }

                        Console.Write("\r" + status);
                    }

                    proc.WaitForExit();
                    outDone.Wait(2000);
                    errDone.Wait(2000);
                    sw.Stop();

                    if (interactive)
                    {
                        int clearWidth = 79;
                        try { clearWidth = System.Math.Max(20, Console.WindowWidth - 1); } catch { clearWidth = 79; }
                        Console.Write("\r" + new string(' ', clearWidth) + "\r");
                    }

                    PrintColor("  Processing... ", ConsoleColor.Yellow);

                    if (proc.ExitCode == 0)
                    {
                        PrintColorLine("OK", ConsoleColor.Green);
                        int javaCount = Directory.Exists(outDir)
                            ? Directory.GetFiles(outDir, "*.java", SearchOption.AllDirectories).Length
                            : 0;
                        PrintColorLine(string.Format("  Time:   {0:N2}s", sw.Elapsed.TotalSeconds), ConsoleColor.DarkGray);
                        PrintColorLine(string.Format("  Source: {0} .java files generated", javaCount), ConsoleColor.White);
                        return true;
                    }
                    else
                    {
                        PrintColorLine(string.Format("FAILED (exit code {0})", proc.ExitCode), ConsoleColor.Red);
                        foreach (string err in errLines)
                        {
                            PrintColorLine("  " + err, ConsoleColor.Red);
                        }
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                PrintColorLine("ERROR", ConsoleColor.Red);
                PrintColorLine("  " + ex.Message, ConsoleColor.Red);
                return false;
            }
        }

        static bool IsLaunchedFromExplorer()
        {
            try
            {
                uint[] procList = new uint[2];
                uint count = GetConsoleProcessList(procList, 2);
                return count <= 1;
            }
            catch
            {
                return false;
            }
        }

        static void WaitExit(string[] args)
        {
            try
            {
                if (Console.IsInputRedirected) return;
                Console.WriteLine();
                PrintColor("Press any key to exit...", ConsoleColor.DarkGray);
                Console.ReadKey(true);
            }
            catch { }
        }
    }
}
