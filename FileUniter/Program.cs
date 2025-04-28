using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FileUniter
{
    internal class Program
    {
        [DllImport("user32.dll")]
        internal static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        internal static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        internal static extern bool SetClipboardData(uint uFormat, IntPtr data);
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Введите пути к файлам (разделяйте точкой с запятой):");
                string[] links = Console.ReadLine().Replace("\"", "").Split(';');

                Console.WriteLine("Введите разделители (по умолчанию ```):");
                string separators = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(separators))
                    separators = "```";

                Console.WriteLine("Рекурсивный поиск? (y/n, по умолчанию n):");
                bool recursive = Console.ReadLine().ToLower() == "y";

                StringBuilder results = new StringBuilder();
                string outputFile = "results.txt";

                foreach (string link in links)
                {
                    if (string.IsNullOrWhiteSpace(link))
                        continue;

                    try
                    {
                        string[] files = GetFilesByPattern(link.Trim(), recursive);
                        foreach (string file in files)
                        {
                            try
                            {
                                
                                string content = File.ReadAllText(file);
                                results.AppendLine($"\"{file}\":");
                                results.AppendLine(separators);
                                results.AppendLine(content);
                                results.AppendLine(separators);
                                results.AppendLine();
                            }
                            catch (Exception ex)
                            {
                                results.AppendLine($"Ошибка чтения файла {file}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"Ошибка обработки пути {link}: {ex.Message}");
                    }
                }

                Console.Clear();
                Console.TreatControlCAsInput = true;
                Console.WriteLine(results.ToString());
                File.WriteAllText(outputFile, results.ToString());
                Console.WriteLine($"Результаты сохранены в {outputFile}");
                while (true)
                {
                    ConsoleKeyInfo cki = Console.ReadKey();
                    if (cki.Modifiers == ConsoleModifiers.Control && cki.Key == ConsoleKey.C)
                    {
                        try
                        {
                            OpenClipboard(IntPtr.Zero);
                            IntPtr ptr = Marshal.StringToHGlobalUni(results.ToString());
                            SetClipboardData(13, ptr);
                            CloseClipboard();
                            Marshal.FreeHGlobal(ptr);
                            Console.WriteLine("Текст скопирован в буфер обмена!");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка копирования: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }

        static string[] GetFilesByPattern(string path, bool recursive)
        {
            try
            {
                string normalizedPath = path.Replace('\\', '/');
                normalizedPath = Regex.Replace(normalizedPath, "//", "/");

                if (normalizedPath.Contains("/*/"))
                {
                    return SearchFilesInAllSubdirectories(normalizedPath, recursive);
                }
                else if (path.Contains("*"))
                {
                    string directory;
                    string pattern;

                    int lastSlashBeforeStar = path.LastIndexOf(Path.DirectorySeparatorChar, path.IndexOf('*'));
                    if (lastSlashBeforeStar >= 0)
                    {
                        directory = path.Substring(0, lastSlashBeforeStar);
                        pattern = path.Substring(lastSlashBeforeStar + 1);
                    }
                    else
                    {
                        directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
                        pattern = Path.GetFileName(path) ?? "*";
                    }

                    if (!Directory.Exists(directory))
                    {
                        return Array.Empty<string>();
                    }

                    return Directory.GetFiles(directory, pattern,
                        recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                }

                string dir = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
                string pat = Path.GetFileName(path) ?? "*";

                return Directory.GetFiles(dir, pat,
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка обработки пути {path}: {ex.Message}");
            }
        }

        static string[] SearchFilesInAllSubdirectories(string normalizedPath, bool recursive)
        {
            try
            {
                string[] parts = normalizedPath.Split(new[] { "/*/" }, StringSplitOptions.None);
                if (parts.Length != 2)
                {
                    return Array.Empty<string>();
                }

                string baseDir = parts[0];
                string filePattern = parts[1];

                baseDir = baseDir.Replace('/', Path.DirectorySeparatorChar);
                filePattern = filePattern.Replace('/', Path.DirectorySeparatorChar);

                List<string> resultFiles = new List<string>();

                if (Directory.Exists(baseDir))
                {
                    string[] subdirectories = Directory.GetDirectories(baseDir);

                    foreach (string dir in subdirectories)
                    {
                        try
                        {
                            string searchDir = Path.GetDirectoryName(Path.Combine(dir, filePattern)) ?? dir;
                            string searchPattern = Path.GetFileName(filePattern) ?? "*";

                            string[] files = Directory.GetFiles(searchDir, searchPattern,
                                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                            resultFiles.AddRange(files);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                return resultFiles.ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка обработки пути {normalizedPath}: {ex.Message}");
            }
        }

        static void SearchSubdirectoriesForFile(string baseDir, string filePattern, bool recursive, List<string> resultFiles)
        {
            try
            {
                string[] directories = Directory.GetDirectories(baseDir);
                foreach (string dir in directories)
                {
                    try
                    {
                        string searchDir = dir;
                        string searchPattern = filePattern;

                        if (filePattern.Contains(Path.DirectorySeparatorChar.ToString()))
                        {
                            string patternDir = Path.GetDirectoryName(filePattern);
                            searchDir = Path.Combine(dir, patternDir);
                            searchPattern = Path.GetFileName(filePattern);
                        }

                        if (Directory.Exists(searchDir))
                        {
                            string[] files = Directory.GetFiles(searchDir, searchPattern);
                            resultFiles.AddRange(files);
                        }

                        if (recursive)
                        {
                            SearchSubdirectoriesForFile(dir, filePattern, true, resultFiles);
                        }
                    }
                    catch (Exception) {}
                }
            }
            catch (UnauthorizedAccessException) {}
            catch (DirectoryNotFoundException) { }
        }
    }
}
