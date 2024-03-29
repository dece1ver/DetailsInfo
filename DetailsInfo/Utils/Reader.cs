﻿using DetailsInfo.Properties;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
using System.Windows.Shapes;
using MimeKit.Encodings;
using static DetailsInfo.Data.FileFormats;
using Path = System.IO.Path;
using static System.Windows.Forms.LinkLabel;

namespace DetailsInfo.Data
{
    public static class Reader
    {
        public static readonly string ToolsListPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"DI\tools.json");
        public static readonly string UserSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"DI\settings.json");
        public static readonly string LocalLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"DI\log.txt");
        public static string LogPath => CheckPath(Settings.Default.netLogPath) ? Path.Combine(Settings.Default.netLogPath, $"{Environment.MachineName}.txt") : LocalLogPath;
        public const string Nameless = "Без названия";

        public enum GetFileNameOptions {FullInfo, OnlyNcName }


        /// <summary>
        /// Ищет свободное имя
        /// </summary>
        /// <param name="targetFile">Отправляемый файл</param>
        /// <param name="targetFolder">Целевая директория</param>
        /// <param name="checkExtension">Учитывать ли расширение</param>
        /// <returns>Свободное имя в целевой директории для отправляемого файла</returns>
        public static string FindFreeName(string targetFile, string targetFolder, bool checkExtension = true)
        {
            var name = string.Empty;
            for (var i = Settings.Default.startProgramNumber; i < 9999; i++)
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
            return Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()).Aggregate(name, (current, item) => current.Replace(item, '-'));
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
        public static string CreateTempName(string file, GetFileNameOptions options = GetFileNameOptions.FullInfo)
        {
            try
            {
                // Mazatrol Smart
                if (MazatrolExtensions.Contains(Path.GetExtension(file).ToLower()))
                {
                    var name = GetMazatrolSmartName(file) != Nameless ? GetMazatrolSmartName(file) : Path.GetFileNameWithoutExtension(file);
                    var extension = Path.GetExtension(file);
                    if (options == GetFileNameOptions.FullInfo)
                    {
                        return $"{name} [{DateTime.Now:dd-MM-y HH-mm}]" + extension;
                    }
                    return name;
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
                return options == GetFileNameOptions.FullInfo ? $"{GetFanucName(file)} [{DateTime.Now:dd-MM-y HH-mm}] ({Path.GetFileName(file)})" : GetFanucName(file);
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
                while (sr.ReadLine() is { } line)
                {
                    fileLines.Add(line);
                }
            }
            else
            {
                // чтение с явно указанной кодировкой в параметрах
                using StreamReader sr = new(csvTable, Encoding.GetEncoding(Settings.Default.fileEncoding));
                while (sr.ReadLine() is { } line)
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
        public static void SortColumn(this DataGrid dataGrid, int columnIndex)
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
                await using StreamWriter sw = new(LogPath, true, Encoding.UTF8);
                await sw.WriteLineAsync($"[{DateTime.Now:dd.MM.y HH:mm:ss}] {SystemInformation.UserName}@{Environment.MachineName}: {info}");
            }
            catch
            {
                await using StreamWriter sw = new(LocalLogPath, true, Encoding.UTF8);
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
            return false;
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

        public static List<NcToolInfo> AnalyzeProgram(
            string programPath, 
            out string caption, 
            out string coordinates, 
            out List<string> warningsH, 
            out List<string> warningsD, 
            out List<string> warningsBracket, 
            out List<string> warningsDots,
            out List<string> warningsEmptyAddress,
            out List<string> warningsCoolant,
            out List<string> warningsPolar,
            out List<string> warningsCyclesCancel,
            out List<string> warningsMacroCallCancel,
            out List<string> warningsCustomCyclesCancel,
            out List<string> warningsFeedType,
            out List<string> warningsIncrement,
            out bool warningStartPercent,
            out bool warningEndPercent,
            out bool warningEndProgram,
            out List<string> warningsExcessText
            )
        {
            //Stopwatch sw = Stopwatch.StartNew();
            List<NcToolInfo> tools = new();
            warningsH = new List<string>();                   // корректор на длину
            warningsD = new List<string>();                   // корректор на радиус
            warningsBracket = new List<string>();             // скобки
            warningsDots = new List<string>();                // точки
            warningsEmptyAddress = new List<string>();        // пустые адреса
            warningsCoolant = new List<string>();             // СОЖ
            warningsPolar = new List<string>();               // выключение полярной СК
            warningsCyclesCancel = new List<string>();        // выключение циклов
            warningsMacroCallCancel = new List<string>();     // выключение моих циклов
            warningsCustomCyclesCancel = new List<string>();  // лишний текст в выключении моих циклов или модальных макро программ
            warningsFeedType = new List<string>();            // возврат подачи к мм/мин
            warningsIncrement = new List<string>();           // возврат к абсолютным координатам
            warningStartPercent = false;                      // процент в начале
            warningEndPercent = true;                         // процент в конце
            warningEndProgram = true;                         // процент в конце
            warningsExcessText = new List<string>();          // лишний текст (за скобками)
            var millProgram = false;
            var lazyAnalyze = false;
            var lines = File.ReadLines(programPath).ToImmutableList();
            List<string> coordinateSystems = new();
            NcToolInfo currentTool = new();
            var currentToolNo = 0;
            var currentToolComment = string.Empty;
            var currentH = 0;
            var currentD = 0;
            var currentCoolant = Coolant.Off;

            // проценты
            if (!lines.First().Trim().Equals("%")) warningStartPercent = true;
            if (!lines.TakeWhile(line => !string.IsNullOrEmpty(line) || !(line.StartsWith('(') && line.EndsWith(')'))).Last().Trim().Equals("%")) warningEndPercent = true;
            var fString = "D" + lines.Count.ToString().Length;
            var i = 1;
            foreach (var line in lines.Skip(1))
            {
                i++;
                var lineWithoutParenthesis = line.Trim();
                lineWithoutParenthesis = new Regex(@"[(][^)]+[)]", RegexOptions.Compiled).Matches(line)
                    .Aggregate(lineWithoutParenthesis, (current, match) => current.Replace(match.Value, string.Empty));
                if (string.IsNullOrEmpty(lineWithoutParenthesis)) continue;

                if (lineWithoutParenthesis.Trim().Contains("%"))
                {
                    warningEndPercent = false;
                    break;
                }
                if(lazyAnalyze) continue;

                if (line.StartsWith('<'))
                {
                    if (line.Count(c => c is '<') != line.Count(c => c is '>'))
                    {
                        warningsBracket.Add($"[{(i).ToString(fString)}]: {line}");
                    }
                    continue;
                }

                // системы координат
                if (line.Contains("G54") && !coordinateSystems.Contains("G54") && !line.Contains("G54.1") && !line.Contains("G54P"))
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

                if (new Regex(@"G54[.]1P\d{1,3}", RegexOptions.Compiled) is { } regex541 && regex541.IsMatch(line))
                {
                    var match = regex541.Match(line);
                    if (!coordinateSystems.Contains(match.Value)) coordinateSystems.Add(match.Value);
                    
                }
                if (new Regex(@"G54P\d{1,3}", RegexOptions.Compiled) is { } regex54 && regex54.IsMatch(line))
                {
                    var match = regex54.Match(line);
                    if (!coordinateSystems.Contains(match.Value)) coordinateSystems.Add(match.Value);
                    
                }
                
                // несовпадения скобок
                if (line.Count(c => c is '(') != line.Count(c => c is ')'))
                {
                    warningsBracket.Add($"[{(i).ToString(fString)}]: {line}");
                }
                if (line.Count(c => c is '[') != line.Count(c => c is ']'))
                {
                    warningsBracket.Add($"[{(i).ToString(fString)}]: {line}");
                }

                // пустые адреса
                if (!line.Contains('#') && new Regex("[A-Z]+[A-Z]|[A-Z]$", RegexOptions.Compiled) is { } matchEmptyAddress && 
                    matchEmptyAddress.IsMatch(lineWithoutParenthesis) && 
                    !matchEmptyAddress.Match(lineWithoutParenthesis).Value.Contains("GOTO") &&
                    !matchEmptyAddress.Match(lineWithoutParenthesis).Value.Contains("END"))
                {
                    warningsEmptyAddress.Add($"[{(i).ToString(fString)}]: {line}");
                }

                // лишние точки
                if (new Regex(@"[A-Z]+[-]?\d*[.]+\d*[.]", RegexOptions.Compiled).IsMatch(lineWithoutParenthesis))
                {
                    warningsDots.Add($"[{(i).ToString(fString)}]: {line}");
                }

                //// лишний текст 
                //if (!new Regex(@"[)]$", RegexOptions.Compiled).IsMatch(line.TrimEnd()) && line.Contains(')'))
                //{
                //    warningsExcessText.Add($"[{(i).ToString(fString)}]: {line}");
                //}

                // конец программы, добавляем последний инструмент, т.к. инструмент добавляется при вызове следующего, а у последнего следующего нет
                if (lineWithoutParenthesis.Contains("M30") || lineWithoutParenthesis.Contains("M99"))
                {
                    warningEndProgram = false;
                    lazyAnalyze = true;
                    if (currentToolNo != 0 && currentToolComment != string.Empty)
                    {
                        currentTool.Position = currentToolNo;
                        currentTool.Comment = currentToolComment;
                        currentTool.LengthCompensation = currentH;
                        currentTool.RadiusCompensation = currentD;
                        currentTool.PolarWarning = warningsPolar.Count > 0;
                        currentTool.FeedPerSpinWarning = warningsFeedType.Count > 0;
                        warningsPolar.Clear();
                        warningsFeedType.Clear();
                        currentTool.Line = i;
                        if (!tools.Contains(currentTool))
                        {
                            if (tools.FindAll(t =>
                                    t.Position == currentTool.Position &&
                                    t.Comment == currentTool.Comment &&
                                    t.LengthCompensation == currentTool.LengthCompensation &&
                                    t.RadiusCompensation == currentTool.RadiusCompensation).Count == 0)
                            {
                                tools.Add(currentTool);
                            }
                        }
                    }
                }

                // переключение ск в полярную и обратно
                if(lineWithoutParenthesis.Contains("G16") && 
                    !lineWithoutParenthesis.StartsWith("G161") && 
                    !lineWithoutParenthesis.StartsWith("G162") && 
                    !lineWithoutParenthesis.StartsWith("G163") && 
                    !lineWithoutParenthesis.StartsWith("G164") && 
                    !lineWithoutParenthesis.StartsWith("G165") && 
                    !lineWithoutParenthesis.StartsWith("G166") && 
                    !lineWithoutParenthesis.StartsWith("G167") && 
                    !lineWithoutParenthesis.StartsWith("G168") && 
                    !lineWithoutParenthesis.StartsWith("G169"))
                {
                    warningsPolar.Add($"[{(i).ToString(fString)}]: {line}");
                }
                if (lineWithoutParenthesis.Contains("G15") && warningsPolar.Count > 0)
                {
                    warningsPolar.Clear();
                }

                // тип подачи
                if(lineWithoutParenthesis.Contains("G95"))
                {
                    warningsFeedType.Add($"[{(i).ToString(fString)}]: {line}");
                }
                if (lineWithoutParenthesis.Contains("G94") && warningsFeedType.Count > 0)
                {
                    warningsFeedType.Clear();
                }

                // приращения
                if(lineWithoutParenthesis.Contains("G91"))
                {
                    warningsIncrement.Add($"[{(i).ToString(fString)}]: {line}");
                }
                if (lineWithoutParenthesis.Contains("G90") && warningsIncrement.Count > 0)
                {
                    warningsIncrement.Remove(warningsIncrement.Last());
                }

                // циклы
                if(lineWithoutParenthesis.Contains("G81") 
                    || lineWithoutParenthesis.Contains("G82") 
                    || lineWithoutParenthesis.Contains("G83") 
                    || lineWithoutParenthesis.Contains("G84") 
                    || lineWithoutParenthesis.Contains("G85"))
                {
                    warningsCyclesCancel.Add($"[{(i).ToString(fString)}]: {line}");
                }
                if (lineWithoutParenthesis.Contains("G80") && warningsCyclesCancel.Count > 0)
                {
                    warningsCyclesCancel.Remove(warningsCyclesCancel.Last());
                }

                // мои циклы и модальные макро программы
                if(lineWithoutParenthesis.Contains("G161") 
                    || lineWithoutParenthesis.Contains("G166")
                    || lineWithoutParenthesis.Contains("G66"))
                {
                    warningsMacroCallCancel.Add($"[{(i).ToString(fString)}]: {line}");
                }

                if (lineWithoutParenthesis.Contains("G67") && warningsMacroCallCancel.Count > 0)
                {
                    // лишний текст в отключении
                    if (!lineWithoutParenthesis.StartsWith('N'))
                    {
                        if(lineWithoutParenthesis != "G67")
                        {
                            warningsCustomCyclesCancel.Add($"[{(i).ToString(fString)}]: {line}");
                        }
                    } 
                    else
                    {
                        var re = new Regex(@"N\d*", RegexOptions.Compiled).Matches(lineWithoutParenthesis);
                        lineWithoutParenthesis = re.Aggregate(lineWithoutParenthesis, (current, match) => current.Replace(match.Value, string.Empty));
                        if (lineWithoutParenthesis != "G67")
                        {
                            warningsCustomCyclesCancel.Add($"[{(i).ToString(fString)}]: {line}");
                        }
                    }
                    warningsMacroCallCancel.Remove(warningsMacroCallCancel.Last());
                }

                // фрезерный инструмент
                if (new Regex(@"T(\d+)", RegexOptions.Compiled).IsMatch(line) && (line.Contains("M6") || line.Contains("M06")) && !line.StartsWith('('))
                {
                    millProgram = true;
                    
                    if (currentToolNo != 0 && currentToolComment != string.Empty)
                    {
                        currentTool.Position = currentToolNo;
                        currentTool.Comment = currentToolComment;
                        currentTool.LengthCompensation = currentH;
                        currentTool.RadiusCompensation = currentD;
                        currentTool.Line = i;
                        currentTool.PolarWarning = warningsPolar.Count > 0;
                        currentTool.FeedPerSpinWarning = warningsFeedType.Count > 0;
                        warningsPolar.Clear();
                        warningsFeedType.Clear();
                        
                        if (!tools.Contains(currentTool))
                        {
                            if (tools.FindAll(t =>
                                    t.Position == currentTool.Position &&
                                    t.Comment == currentTool.Comment &&
                                    t.LengthCompensation == currentTool.LengthCompensation &&
                                    t.RadiusCompensation == currentTool.RadiusCompensation).Count == 0)
                            {
                                tools.Add(currentTool);
                            }
                        }
                    } 
                    else
                    {
                        currentTool.PolarWarning = warningsPolar.Count > 0;
                        currentTool.FeedPerSpinWarning = warningsFeedType.Count > 0;
                    }

                    var toolLine = line.Contains('(') 
                        ? line.Split('T')[1]
                            .Replace("M6", string.Empty)
                            .Replace("M06", string.Empty)
                            .Split('(')[0]
                            .Replace(" ", string.Empty) 
                        : line.Split('T')[1]
                            .Replace("M6", string.Empty)
                            .Replace("M06", string.Empty)
                            .Replace(" ", string.Empty);
                    currentD = 0;
                    if (int.TryParse(toolLine, out currentToolNo))
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

                // токарный инструмент
                if (new Regex(@"T(\d+)", RegexOptions.Compiled).IsMatch(line) && !millProgram && !line.StartsWith('('))
                {
                    if (currentToolNo != 0 && currentToolComment != string.Empty)
                    {
                        currentTool.Position = currentToolNo;
                        currentTool.Comment = currentToolComment;
                        currentTool.LengthCompensation = 0;
                        currentTool.RadiusCompensation = 0;
                        currentTool.Line = i;
                        if (!tools.Contains(currentTool))
                        {
                            tools.Add(currentTool);
                        }
                    }

                    string toolLine = line.Contains('(') ? line.Split('T')[1].Split('(')[0].Replace(" ", string.Empty) : line.Split('T')[1].Replace(" ", string.Empty);
                    if (toolLine.Contains('X')) toolLine = toolLine.Split('X')[0];
                    if (toolLine.Contains('Y')) toolLine = toolLine.Split('Y')[0];
                    if (toolLine.Contains('Z')) toolLine = toolLine.Split('Z')[0];
                    if (toolLine.Contains('A')) toolLine = toolLine.Split('A')[0];
                    if (toolLine.Contains('F')) toolLine = toolLine.Split('F')[0];
                    if (toolLine.Contains('M')) toolLine = toolLine.Split('M')[0];
                    if (toolLine.Contains('G')) toolLine = toolLine.Split('G')[0];
                    currentD = 0;
                    if (int.TryParse(toolLine, out currentToolNo))
                    {
                        try
                        {
                            currentToolComment = $"(" + line.Split("(")[1].Trim();
                        }
                        catch (IndexOutOfRangeException)
                        {
                            currentToolComment = $"(---)";
                        }
                    }
                }

                if (lineWithoutParenthesis.Contains("M8") || lineWithoutParenthesis.Contains("M58"))
                {
                    currentCoolant = Coolant.On;
                }
                if (lineWithoutParenthesis.Contains("M9") || lineWithoutParenthesis.Contains("M59"))
                {
                    currentCoolant = Coolant.Off;
                }
                if (lineWithoutParenthesis.Contains("G01")
                    || lineWithoutParenthesis.Contains("G1")
                    || lineWithoutParenthesis.Contains("G72")
                    || lineWithoutParenthesis.Contains("G71"))
                {
                    currentTool.Coolant = currentCoolant;
                    //if (tools.Count > 0)
                    //{
                        //var lastTool = tools.Last();
                        //int index = tools.IndexOf(lastTool);
                        //lastTool.Coolant = currentCoolant;
                        //tools[index] = lastTool;
                    //}
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
                        if (currentToolNo != currentD && currentToolNo != 0)
                        {
                            warningsD.Add($"[{(i).ToString(fString)}]: {line} - (T{currentToolNo} D{currentD})");
                        }
                        else if (currentToolNo != currentD && currentToolNo == 0)
                        {
                            warningsD.Add($"[{(i).ToString(fString)}]: {line} - (D{currentD} - без инструмента)");
                        }
                        currentTool.Position = currentToolNo;
                        currentTool.Comment = currentToolComment;
                        currentTool.LengthCompensation = currentH;
                        currentTool.RadiusCompensation = currentD;
                        //currentTool.PolarWarning = warningsPolar.Count > 0 ? true : false;
                        //currentTool.FeedPerSpinWarning = warningsFeedType.Count > 0 ? true : false;
                        //warningsPolar.Clear();
                        //warningsFeedType.Clear();
                        if (!tools.Contains(currentTool))
                        {
                            if (tools.Count > 0)
                            {
                                var prevTool = tools[^1];
                                if (prevTool.Position == currentTool.Position && prevTool.Comment == currentTool.Comment &&
                                    prevTool.LengthCompensation == currentTool.LengthCompensation && prevTool.RadiusCompensation == 0)
                                {
                                    tools.Remove(prevTool);
                                }
                                
                            }
                            tools.Add(currentTool);
                        }
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
                        if (currentToolNo != currentH && currentToolNo != 0)
                        {
                            warningsH.Add($"[{(i).ToString(fString)}]: {line} - (T{currentToolNo} H{currentH})");
                        }
                        else if (currentToolNo != currentH && currentToolNo == 0) 

                        {
                            warningsH.Add($"[{(i).ToString(fString)}]: {line} - (H{currentH} - без инструмента)");
                        }
                        currentTool.Position = currentToolNo;
                        currentTool.Comment = currentToolComment;
                        currentTool.LengthCompensation = currentH;
                        currentTool.RadiusCompensation = currentD;
                        
                        currentTool.Position = currentToolNo;
                        currentTool.Comment = currentToolComment;
                        currentTool.LengthCompensation = currentH;
                        currentTool.RadiusCompensation = currentD;

                        //currentTool.PolarWarning = warningsPolar.Count > 0 ? true : false;
                        //currentTool.FeedPerSpinWarning = warningsFeedType.Count > 0 ? true : false;
                        //warningsPolar.Clear();
                        //warningsFeedType.Clear();
                        if (!tools.Contains(currentTool))
                        {
                            if (tools.FindAll(t =>
                                    t.Position == currentTool.Position &&
                                    t.Comment == currentTool.Comment &&
                                    t.LengthCompensation == currentTool.LengthCompensation &&
                                    t.RadiusCompensation == currentTool.RadiusCompensation &&
                                    t.RadiusCompensation != 0).Count == 0)
                            {
                                tools.Add(currentTool);
                            }
                        }
                    }
                }
            }
            if (!millProgram)
            {
                foreach (var tool in  tools.Where(x => x.Coolant is Coolant.Off))
                {
                    warningsCoolant.Add($"[{tool.Line}]: Т{tool.Position:D4} {tool.Comment}");
                }
            }
            warningsPolar.Clear();
            warningsFeedType.Clear();
            foreach (var tool in tools.FindAll(t => t.PolarWarning is true))
            {
                warningsPolar.Add($"[{tool.Line}]: Т{tool.Position} {tool.Comment}");
            }
            foreach (var tool in tools.FindAll(t => t.FeedPerSpinWarning is true))
            {
                warningsFeedType.Add($"[{tool.Line}]: Т{tool.Position} {tool.Comment}");
            }
            
            caption = $"{(millProgram ? "Фрезерная" : "Токарная")} программа";
            coordinates = coordinateSystems.Count switch
            {
                1 => $"Система координат {coordinateSystems[0]}\n\n",
                > 1 => $"Системы координат: {string.Join(", ", coordinateSystems)}\n\n",
                _ => "Системы координат отсутствуют\n\n"
            };
            //coordinates = $"Время: {sw.ElapsedMilliseconds} мс\n" + coordinates;

        return tools;
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
                IntegratedImageViewer = Settings.Default.integratedImageViewer,
                NetLogPath = Settings.Default.netLogPath,
                EmailLogin = Settings.Default.emailLogin,
                EmailPass = Settings.Default.emailPass,
                PopServer = Settings.Default.popServer,
                PopPort = Settings.Default.popPort,
                SmtpServer = Settings.Default.smtpServer,
                SmtpPort = Settings.Default.smtpPort,
                UseSsl = Settings.Default.useSsl,
                ToAdress = Settings.Default.toAdress,
                FromAdress = Settings.Default.fromAdress,
                NcAnalyzer = Settings.Default.ncAnalyzer,
                StartProgramNumber = Settings.Default.startProgramNumber,
            };

            using (var writer = File.CreateText(UserSettingsPath))
            {
                writer.Write(JsonConvert.SerializeObject(userConfig));
            }
            return "Файл конфигурации не обнаружен, установлены настройки по умолчанию. ";
        }

        public static bool ValidateConfig(UserConfig userConfig)
        {

            if (userConfig.ArchivePath is null ||
                userConfig.MachinePath is null ||
                userConfig.TempPath is null ||
                userConfig.PopServer is null ||
                userConfig.SmtpServer is null ||
                userConfig.PopPort == 0 ||
                userConfig.SmtpPort == 0 ||
                userConfig.ToAdress is null)
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
                IntegratedImageViewer = userConfig.IntegratedImageViewer,
                NetLogPath = userConfig.NetLogPath ?? Settings.Default.netLogPath,
                EmailLogin = userConfig.EmailLogin ?? Settings.Default.emailLogin,
                EmailPass = userConfig.EmailPass ?? Settings.Default.emailPass,
                PopServer = !string.IsNullOrEmpty(userConfig.PopServer) ? userConfig.PopServer : Settings.Default.popServer,
                PopPort = userConfig.PopPort != 0 ? userConfig.PopPort : Settings.Default.popPort,
                SmtpServer = !string.IsNullOrEmpty(userConfig.SmtpServer) ? userConfig.SmtpServer : Settings.Default.smtpServer,
                SmtpPort = userConfig.PopPort != 0 ? userConfig.SmtpPort : Settings.Default.smtpPort,
                UseSsl = userConfig.UseSsl,
                ToAdress = userConfig.ToAdress ?? Settings.Default.toAdress,
                FromAdress = userConfig.FromAdress ?? Settings.Default.fromAdress,
                NcAnalyzer = userConfig.NcAnalyzer,
                StartProgramNumber = 1,
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
            Settings.Default.integratedImageViewer = tempUserConfig.IntegratedImageViewer;
            Settings.Default.netLogPath = tempUserConfig.NetLogPath;
            Settings.Default.emailLogin = tempUserConfig.EmailLogin;
            Settings.Default.emailPass = tempUserConfig.EmailPass;
            Settings.Default.popServer = tempUserConfig.PopServer;
            Settings.Default.popPort = tempUserConfig.PopPort;
            Settings.Default.useSsl = tempUserConfig.UseSsl;
            Settings.Default.smtpServer = tempUserConfig.SmtpServer;
            Settings.Default.smtpPort = tempUserConfig.SmtpPort;
            Settings.Default.toAdress = tempUserConfig.ToAdress;
            Settings.Default.fromAdress = tempUserConfig.FromAdress;
            Settings.Default.ncAnalyzer = tempUserConfig.NcAnalyzer;
            Settings.Default.startProgramNumber = tempUserConfig.StartProgramNumber;
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
            Settings.Default.integratedImageViewer = userConfig.IntegratedImageViewer;
            Settings.Default.netLogPath = userConfig.NetLogPath;
            Settings.Default.emailLogin = userConfig.EmailLogin;
            Settings.Default.emailPass = userConfig.EmailPass;
            Settings.Default.popServer = userConfig.PopServer;
            Settings.Default.popPort = userConfig.PopPort;
            Settings.Default.useSsl = userConfig.UseSsl;
            Settings.Default.smtpServer = userConfig.SmtpServer;
            Settings.Default.smtpPort = userConfig.SmtpPort;
            Settings.Default.toAdress = userConfig.ToAdress;
            Settings.Default.fromAdress = userConfig.FromAdress;
            Settings.Default.ncAnalyzer = userConfig.NcAnalyzer;
            Settings.Default.startProgramNumber = userConfig.StartProgramNumber;
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
