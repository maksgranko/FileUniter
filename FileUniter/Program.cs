using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FileUniter
{
    internal class Program
    {
        // Кэш для хранения результатов поиска файлов
        private static readonly ConcurrentDictionary<string, string[]> _fileCache = new ConcurrentDictionary<string, string[]>();

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
                // Проверяем, есть ли результат в кэше
                string cacheKey = $"{path}|{recursive}";
                if (_fileCache.TryGetValue(cacheKey, out string[] cachedResult))
                {
                    return cachedResult;
                }

                // Нормализуем путь за одну операцию
                string normalizedPath = Regex.Replace(path.Replace('\\', '/'), "//+", "/");

                string[] result;
                if (normalizedPath.Contains("/*/"))
                {
                    result = SearchFilesInAllSubdirectories(normalizedPath, recursive);
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
                        result = Array.Empty<string>();
                    }
                    else
                    {
                        result = Directory.GetFiles(directory, pattern,
                            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                    }
                }
                else
                {
                    string dir = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
                    string pat = Path.GetFileName(path) ?? "*";

                    result = Directory.GetFiles(dir, pat,
                        recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                }

                // Сохраняем результат в кэш
                _fileCache[cacheKey] = result;
                return result;
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

                string baseDir = parts[0].Replace('/', Path.DirectorySeparatorChar);
                string filePattern = parts[1].Replace('/', Path.DirectorySeparatorChar);

                if (!Directory.Exists(baseDir))
                {
                    return Array.Empty<string>();
                }

                string[] subdirectories = Directory.GetDirectories(baseDir);

                // Используем параллельную обработку для поиска файлов
                ConcurrentBag<string> resultFiles = new ConcurrentBag<string>();

                Parallel.ForEach(subdirectories, dir =>
                {
                    try
                    {
                        string searchDir = Path.GetDirectoryName(Path.Combine(dir, filePattern)) ?? dir;
                        string searchPattern = Path.GetFileName(filePattern) ?? "*";

                        if (Directory.Exists(searchDir))
                        {
                            string[] files = Directory.GetFiles(searchDir, searchPattern,
                                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                            foreach (string file in files)
                            {
                                resultFiles.Add(file);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Пропускаем директории с ошибками
                    }
                });

                return resultFiles.ToArray();
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка обработки пути {normalizedPath}: {ex.Message}");
            }
        }
    }
}
