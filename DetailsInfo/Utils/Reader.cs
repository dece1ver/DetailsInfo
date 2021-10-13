using DetailsInfo.Properties;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using static DetailsInfo.Data.FileFormats;

namespace DetailsInfo.Data
{
    public static class Reader
    {
        public static readonly string ToolsListPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"DI\tools.json");
        public static readonly string UserSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"DI\settings.json");
        public static readonly string LocalLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"DI\log.txt");
        public static string LogPath => CheckPath(Settings.Default.netLogPath) ? Path.Combine(Settings.Default.netLogPath, $"{Environment.MachineName}.txt") : LocalLogPath;
        public const string Nameless = "Без названия";


        /// <summary>
        /// Ищет свободное имя
        /// </summary>
        /// <param name="targetFile">Отправляемый файл</param>
        /// <param name="targetFolder">Целевая директория</param>
        /// <param name="checkExtension">Учитывать ли расширение</param>
        /// <returns>Свободное имя в целевой директории для отправляемого файла</returns>
        public static string FindFreeName(string targetFile, string targetFolder, bool checkExtension = true)
        {
            string name = string.Empty;
            for (int i = 1; i < 9999; i++)
            {
                if (
                    File.Exists(Path.Combine(targetFolder, i.ToString())) ||
                    File.Exists(Path.Combine(targetFolder, i.ToString()) + Path.GetExtension(targetFile)))
                {
                    continue;
                }
                else
                {
                    if (checkExtension)
                    {
                        name = MachineExtensions.Contains(Path.GetExtension(targetFile).ToLower()) ?
                        Path.Combine(targetFolder, i.ToString()) + Path.GetExtension(targetFile) :
                        Path.Combine(targetFolder, i.ToString());
                    }
                    else
                    {
                        name = Path.Combine(targetFolder, i.ToString()) + Path.GetExtension(targetFile);
                    }

                    break;

                }
            }
            return name;
        }

        /// <summary>
        /// Пробует получить имя УП СЧПУ Fanuc i0 проверяя на % в первой строке и комментарий в следующей строке.
        /// </summary>
        /// <param name="file">Путь к УП Fanuc i0</param>
        /// <returns>Возвращает строку содержащую имя УП, при неудаче возвращает значение поля nameless</returns>
        public static string GetFanucName(string file)
        {
            string name;
            try
            {
                // если файл не пустой и начинается с %
                if (!string.IsNullOrWhiteSpace(File.ReadAllText(file)) && File.ReadAllLines(file)[0] == "%")
                {
                    name = File.ReadAllLines(file)[1].Split('(')[1].Split(')')[0]; // берем значение между скобок во второй строке
                }
                // если нет
                else
                {
                    name = "";
                }

            }
            // не найдя скобку выбивает эту ошибку, типа разделяет по скобке, а не разделилось, в итоге индекса[1] нет
            catch (IndexOutOfRangeException)
            {
                name = Nameless;
            }
            // любая другая ошибка, н-р ошибка доступа к файлу или что-нибудь еще
            catch (Exception)
            {
                name = "";
            }
            // заменяет все плохие символы на -, чтобы ошибки при записи не было
            foreach (char item in Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()))
            {
                name = name.Replace(item, '-');
            }
            return name;
        }

        /// <summary>
        /// Пробует получить имя УП СЧПУ Mazatrol Smart просматривая определенный диапазон символов.
        /// </summary>
        /// <param name="file">Путь к УП Mazatrol Smart</param>
        /// <returns>Возвращает строку содержащую имя УП, при неудаче возвращает значение readonly поля nameless класса Reader</returns>
        public static string GetMazatrolSmartName(string file)
        {
            try
            {
                return File.ReadAllText(file).Substring(80, 32).Trim().Trim('\0').Replace('/', '-').Replace('\\', '-');
            }
            catch
            {
                return Nameless;
            }

        }

        /// <summary>
        /// Формирует имя файла на основании имени УП, времени работы метода, исходного имени файла.
        /// </summary>
        /// <param name="file">Путь к исходному файлу</param>
        /// <returns>Возвращает строку содержащую имя файла, при неудаче возвращает значение поля nameless</returns>
        public static string CreateTempName(string file)
        {
            try
            {
                // Mazatrol Smart
                if (MazatrolExtensions.Contains(Path.GetExtension(file).ToLower()))
                {
                    string name = GetMazatrolSmartName(file) != Nameless ? GetMazatrolSmartName(file) : Path.GetFileNameWithoutExtension(file);
                    string extension = Path.GetExtension(file);
                    return $"{name} [{DateTime.Now:dd-MM-y HH-mm}]" + extension;
                }
                // Sinumerik

                if (SinumerikExtensions.Contains(Path.GetExtension(file).ToLower()))
                {
                    return Path.GetFileName(file); // дописать обработку
                }
                // Heidenhain

                if (HeidenhainExtensions.Contains(Path.GetExtension(file).ToLower()))
                {
                    return Path.GetFileName(file); // дописать обработку
                }

                return $"{GetFanucName(file)} [{DateTime.Now:dd-MM-y HH-mm}] ({Path.GetFileName(file)})";
            }
            catch
            {
                return Path.GetFileName(file);
            }
        }

        /// <summary>
        /// Проверяет может ли файл быть перемещен из архива в сетевую папку станка сравнивая с разрешенными расширениями
        /// </summary>
        /// <param name="file">Путь к файлу</param>
        /// <returns>true если файл может быть передан и false если нет</returns>
        public static bool CanBeTransfered(string file)
        {
            return !NonTransferableHash.Contains(Path.GetExtension(file).ToLower());
        }

        /// <summary>
        /// Читает CSV таблицу
        /// </summary>
        /// <param name="csvTable">Путь к файлу .CSV</param>
        /// <returns>Список строк с содержимым таблицы</returns>
        public static List<string> ReadCsvTable(string csvTable)
        {
            List<string> fileLines = new();

            //если в параметрах кодировка 0, то пытается читать определяя кодировку автоматически
            if (Settings.Default.fileEncoding == 0)
            {
                // автоматическое определение
                using StreamReader sr = new(csvTable, true);
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    fileLines.Add(line);
                }
            }
            else
            {
                // чтение с явно указанной кодировкой в параметрах
                using StreamReader sr = new(csvTable, Encoding.GetEncoding(Settings.Default.fileEncoding));
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    fileLines.Add(line);
                }
            }
            return fileLines;
        }

        /// <summary>
        /// Сортирует DataGrid имитируя клик по заголовку столбца.
        /// </summary>
        /// <param name="dataGrid">Экземпляр DataGrid</param>
        /// <param name="columnIndex">Индекс столбца</param>
        public static void SortColumn(DataGrid dataGrid, int columnIndex)
        {
            var performSortMethod = typeof(DataGrid)
                                    .GetMethod("PerformSort",
                                               BindingFlags.Instance | BindingFlags.NonPublic);

            performSortMethod?.Invoke(dataGrid, new[] { dataGrid.Columns[columnIndex] });
        }

        /// <summary>
        /// Пишет лог по DNC пути указанному в параметрах. Если такого пути не существует, то пишет по локальному пути в моих документах.
        /// </summary>
        /// <param name="info">Сообщение</param>
        public static async Task WriteLogAsync(string info)
        {
            try
            {
                using StreamWriter sw = new(LogPath, true, Encoding.UTF8);
                await sw.WriteLineAsync($"[{DateTime.Now:dd.MM.y HH:mm:ss}] {SystemInformation.UserName}@{Environment.MachineName}: {info}");
            }
            catch
            {
                using StreamWriter sw = new(LocalLogPath, true, Encoding.UTF8);
                await sw.WriteLineAsync($"[{DateTime.Now:dd.MM.y HH:mm:ss}] {SystemInformation.UserName}@{Environment.MachineName}: {info}");
            }
        }


        /// <summary>
        /// Проверяет доступность хоста
        /// </summary>
        /// <param name="adress">Адрес</param>
        /// <returns></returns>
        private static bool HostExists(string adress)
        {
            Ping pinger = new();

            try
            {
                return pinger.Send(adress).Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
            finally
            {
                pinger.Dispose();
            }

        }

        /// <summary>
        /// Собранный со стаковерфлоу костыль для быстрой проверки доступности UNC пути
        /// </summary>
        /// <param name="path">Путь</param>
        /// <returns></returns>
        public static bool CheckPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                Uri uri = new(path);
                if (uri.IsUnc && !HostExists(uri.Host)) return false;
            }
            catch (UriFormatException)
            {
                return false;
            }
            if (Directory.Exists(path) || File.Exists(path)) return true;
            if (string.IsNullOrEmpty(path)) return false;
            return true;
        }

        /// <summary>
        /// Добавляет текст в текстбокс и сдвигает каретку вправо
        /// </summary>
        /// <param name="textBox"></param>
        /// <param name="path"></param>
        public static void SetArchivePath(this System.Windows.Controls.TextBox textBox, string path)
        {
            textBox.Text = path;
            textBox.CaretIndex = textBox.Text.Length;
            textBox.ScrollToHorizontalOffset(double.MaxValue);
        }

        public static List<NcToolInfo> AnalyzeProgram(string programPath, out string caption, out string coordinates)
        {
            List<NcToolInfo> tools = new();
            bool millProgram = false;
            var lines = File.ReadLines(programPath);
            List<string> coordinateSystems = new();
            int currentTool = 0;
            string currentToolComment = string.Empty;
            int currentH = 0;
            int currentD = 0;
            int warningsH = 0;
            int warningsD = 0;
            
            foreach (var line in lines)
            {
                if (line.Contains("G54") && !coordinateSystems.Contains("G54"))
                {
                    coordinateSystems.Add("G54");
                }
                if (line.Contains("G55") && !coordinateSystems.Contains("G55"))
                {
                    coordinateSystems.Add("G55");
                }
                if (line.Contains("G56") && !coordinateSystems.Contains("G56"))
                {
                    coordinateSystems.Add("G56");
                }
                if (line.Contains("G57") && !coordinateSystems.Contains("G57"))
                {
                    coordinateSystems.Add("G57");
                }
                if (line.Contains("G58") && !coordinateSystems.Contains("G58"))
                {
                    coordinateSystems.Add("G58");
                }
                if (line.Contains("G59") && !coordinateSystems.Contains("G59"))
                {
                    coordinateSystems.Add("G59");
                }

                if (line.Contains("M30"))
                {
                    if (currentTool != 0 && currentToolComment != string.Empty)
                    {
                        tools.Add(new NcToolInfo() {Position = currentTool, Comment = currentToolComment, LengthCompensation = currentH, RadiusCompensation = currentD });
                    }
                }

                if (new Regex(@"T(\d+)", RegexOptions.Compiled).IsMatch(line) && line.Contains("M6") && !line.StartsWith('('))
                {
                    millProgram = true;

                    if (currentTool != 0 && currentToolComment != string.Empty)
                    {
                        tools.Add(new NcToolInfo() { Position = currentTool, Comment = currentToolComment, LengthCompensation = currentH, RadiusCompensation = currentD });
                    }

                    string toolLine = line.Contains('(') ? line.Split('T')[1].Replace("M6", string.Empty).Split('(')[0].Replace(" ", string.Empty) : line.Split('T')[1].Replace("M6", string.Empty).Replace(" ", string.Empty);
                    
                    if (int.TryParse(toolLine, out currentTool))
                    {
                        try
                        {
                            currentToolComment = $"(" + line.Split("(")[1].Trim();
                            //if (!tools.Contains($"{currentTool} {currentToolComment}")) tools.Add($"{currentTool} {currentToolComment}");
                        }
                        catch (IndexOutOfRangeException)
                        {
                            currentToolComment = $"(---)";
                            //if (!tools.Contains($"{currentTool} {currentToolComment}")) tools.Add($"{currentTool} {currentToolComment}");
                        }
                    }
                }
                if ((line.Contains("G41") || line.Contains("G42")) && line.Contains('D'))
                {
                    string compensationLine = line.Split("D")[1];
                    if (compensationLine.Contains('X')) compensationLine = compensationLine.Split('X')[0];
                    if (compensationLine.Contains('Y')) compensationLine = compensationLine.Split('Y')[0];
                    if (compensationLine.Contains('Z')) compensationLine = compensationLine.Split('Z')[0];
                    if (compensationLine.Contains('A')) compensationLine = compensationLine.Split('A')[0];
                    if (compensationLine.Contains('F')) compensationLine = compensationLine.Split('F')[0];
                    if (compensationLine.Contains('M')) compensationLine = compensationLine.Split('M')[0];
                    if (compensationLine.Contains('G')) compensationLine = compensationLine.Split('G')[0];
                    if (int.TryParse(compensationLine.Replace(" ", string.Empty), out currentD))
                    {
                        if (currentTool != currentD) warningsD++;
                        tools.Add(new NcToolInfo() { Position = currentTool, Comment = currentToolComment, LengthCompensation = currentH, RadiusCompensation = currentD });
                    }
                    //compensationsD.Add($"T{currentTool} D{int.Parse(compensationLine.Replace(" ", string.Empty))}");
                }
                if (line.Contains("G43") && line.Contains('H'))
                {
                    string compensationLine = line.Split("H")[1];
                    if (compensationLine.Contains('X')) compensationLine = compensationLine.Split('X')[0];
                    if (compensationLine.Contains('Y')) compensationLine = compensationLine.Split('Y')[0];
                    if (compensationLine.Contains('Z')) compensationLine = compensationLine.Split('Z')[0];
                    if (compensationLine.Contains('A')) compensationLine = compensationLine.Split('A')[0];
                    if (compensationLine.Contains('F')) compensationLine = compensationLine.Split('F')[0];
                    if (compensationLine.Contains('M')) compensationLine = compensationLine.Split('M')[0];
                    if (compensationLine.Contains('G')) compensationLine = compensationLine.Split('G')[0];
                    if (int.TryParse(compensationLine.Replace(" ", string.Empty), out currentH))
                    {
                        if (currentTool != currentH) warningsH++;
                        tools.Add(new NcToolInfo() { Position = currentTool, Comment = currentToolComment, LengthCompensation = currentH, RadiusCompensation = currentD });
                    }                    
                    //compensationsH.Add($"T{currentTool} H{int.Parse(compensationLine.Replace(" ", string.Empty))}");
                }
            }
            caption = $"{(millProgram ? "Фрезерная" : "Токарная")} программа";
            coordinates = $"{(coordinateSystems.Count == 1 ? $"Система координат {coordinateSystems[0]}\n" : $"Системы координат: {string.Join(',', coordinateSystems)}\n")}\n";
            
            return tools.Distinct().ToList();
        }

        #region Пользовательские настройки
        public static string WriteConfig()
        {
            string targetFolder = Path.GetDirectoryName(UserSettingsPath);
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }
            File.CreateText(UserSettingsPath).Dispose();
            UserConfig userConfig = new()
            {
                TablePath = Settings.Default.tablePath,
                FileEncoding = Settings.Default.fileEncoding,
                ArchivePath = Settings.Default.archivePath,
                MachinePath = Settings.Default.machinePath,
                TempPath = Settings.Default.tempPath,
                RefreshInterval = Settings.Default.refreshInterval,
                AutoRename = Settings.Default.autoRenameToMachine,
                NetLogPath = Settings.Default.netLogPath,
                EmailLogin = Settings.Default.emailLogin,
                EmailPass = Settings.Default.emailPass,
                PopServer = Settings.Default.popServer,
                PopPort = Settings.Default.popPort,
                SmtpServer = Settings.Default.smtpServer,
                SmtpPort = Settings.Default.smtpPort,
                UseSsl = Settings.Default.useSsl
            };

            using (var writer = File.CreateText(UserSettingsPath))
            {
                writer.Write(JsonConvert.SerializeObject(userConfig));
            }
            return "Файл конфигурации не обнаружен, установлены настройки по умолчанию. ";
        }

        public static bool ValidateConfig(UserConfig userConfig)
        {

            if (userConfig.ArchivePath == null ||
                userConfig.MachinePath == null ||
                userConfig.TempPath == null ||
                userConfig.PopServer == null ||
                userConfig.SmtpServer == null ||
                userConfig.PopPort == 0 ||
                userConfig.SmtpPort == 0)
            {
                return false;
            }
            return true;
        }

        public static string RepairConfig(UserConfig userConfig)
        {
            UserConfig tempUserConfig = new()
            {
                TablePath = !string.IsNullOrEmpty(userConfig.TablePath) ? userConfig.TablePath : Settings.Default.tablePath,
                FileEncoding = userConfig.FileEncoding,
                ArchivePath = !string.IsNullOrEmpty(userConfig.ArchivePath) ? userConfig.ArchivePath : Settings.Default.archivePath,
                MachinePath = !string.IsNullOrEmpty(userConfig.MachinePath) ? userConfig.MachinePath : Settings.Default.machinePath,
                TempPath = !string.IsNullOrEmpty(userConfig.TempPath) ? userConfig.TempPath : Settings.Default.tempPath,
                RefreshInterval = userConfig.RefreshInterval,
                AutoRename = userConfig.AutoRename,
                NetLogPath = userConfig.NetLogPath ?? Settings.Default.netLogPath,
                EmailLogin = userConfig.EmailLogin ?? Settings.Default.emailLogin,
                EmailPass = userConfig.EmailPass ?? Settings.Default.emailPass,
                PopServer = !string.IsNullOrEmpty(userConfig.PopServer) ? userConfig.PopServer : Settings.Default.popServer,
                PopPort = userConfig.PopPort != 0 ? userConfig.PopPort : Settings.Default.popPort,
                SmtpServer = !string.IsNullOrEmpty(userConfig.SmtpServer) ? userConfig.SmtpServer : Settings.Default.smtpServer,
                SmtpPort = userConfig.PopPort != 0 ? userConfig.SmtpPort : Settings.Default.smtpPort,
                UseSsl = userConfig.UseSsl
            };

            using (var writer = File.CreateText(UserSettingsPath))
            {
                writer.Write(JsonConvert.SerializeObject(tempUserConfig));
            }
            Settings.Default.tablePath = tempUserConfig.TablePath;
            Settings.Default.fileEncoding = tempUserConfig.FileEncoding;
            Settings.Default.archivePath = tempUserConfig.ArchivePath;
            Settings.Default.machinePath = tempUserConfig.MachinePath;
            Settings.Default.tempPath = tempUserConfig.TempPath;
            Settings.Default.refreshInterval = tempUserConfig.RefreshInterval;
            Settings.Default.autoRenameToMachine = tempUserConfig.AutoRename;
            Settings.Default.netLogPath = tempUserConfig.NetLogPath;
            Settings.Default.emailLogin = tempUserConfig.EmailLogin;
            Settings.Default.emailPass = tempUserConfig.EmailPass;
            Settings.Default.popServer = tempUserConfig.PopServer;
            Settings.Default.popPort = tempUserConfig.PopPort;
            Settings.Default.useSsl = tempUserConfig.UseSsl;
            Settings.Default.smtpServer = tempUserConfig.SmtpServer;
            Settings.Default.smtpPort = tempUserConfig.SmtpPort;
            Settings.Default.Save();
            return "Файл конфигурации исправлен. ";
        }

        public static string ReadConfig()
        {
            if (!File.Exists(UserSettingsPath))
            {
                return WriteConfig();
            }

            string rawUserConfig;
            using (var reader = File.OpenText(UserSettingsPath))
            {
                rawUserConfig = reader.ReadToEnd();
            }
            var userConfig = JsonConvert.DeserializeObject<UserConfig>(rawUserConfig);

            if (!ValidateConfig(userConfig))
            {
                return RepairConfig(userConfig);
            }

            Settings.Default.tablePath = userConfig.TablePath;
            Settings.Default.fileEncoding = userConfig.FileEncoding;
            Settings.Default.archivePath = userConfig.ArchivePath;
            Settings.Default.machinePath = userConfig.MachinePath;
            Settings.Default.tempPath = userConfig.TempPath;
            Settings.Default.refreshInterval = userConfig.RefreshInterval;
            Settings.Default.autoRenameToMachine = userConfig.AutoRename;
            Settings.Default.netLogPath = userConfig.NetLogPath;
            Settings.Default.emailLogin = userConfig.EmailLogin;
            Settings.Default.emailPass = userConfig.EmailPass;
            Settings.Default.popServer = userConfig.PopServer;
            Settings.Default.popPort = userConfig.PopPort;
            Settings.Default.Save();
            return $"Прочитан файл конфигурации. ";
        }
        #endregion

        #region Список инструмента
        public static BindingList<ToolNote> LoadTools()
        {
            if (!File.Exists(ToolsListPath))
            {
                string targetFolder = Path.GetDirectoryName(ToolsListPath);
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }
                File.CreateText(ToolsListPath).Dispose();
                return new BindingList<ToolNote>();
            }
            using var reader = File.OpenText(ToolsListPath);
            string rawTools = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<BindingList<ToolNote>>(rawTools);
        }

        public static void SaveTools(object toolNotes)
        {
            using var writer = File.CreateText(ToolsListPath);
            writer.Write(JsonConvert.SerializeObject(toolNotes));
        }
        #endregion
    }
}
