using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Collections.Generic;
using MSBATranslator.Core.Database;

namespace MSBATranslator.Core
{
    public class TableTargetConfig
    {
        public string FileName { get; set; } = string.Empty;
        public string[] TextFields { get; set; } = Array.Empty<string>();
    }

    public static class RepoGenerator
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static readonly List<TableTargetConfig> TargetConfigs = new()
        {
            new() { FileName = "Localize.json", TextFields = ["En"] },
            new() { FileName = "LocalizeError.json", TextFields = ["En"] },
            new() { FileName = "LocalizeSkill.json", TextFields = ["NameEn", "DescriptionEn"] },
            new() { FileName = "LocalizeEtc.json", TextFields = ["NameEn", "DescriptionEn"] },
            new() { FileName = "LocalizeGachaShop.json", TextFields = ["TabNameEn", "TitleNameEn", "SubTitleEn", "GachaDescriptionEn"] },
            new() {  FileName = "LocalizeCharProfile.json",  TextFields = ["StatusMessageEn", "FullNameEn", "FamilyNameEn", "FamilyNameRubyEn", "PersonalNameEn", "PersonalNameRubyEn", "ClubNameForGachaEn", "SchoolYearEn", "CharacterAgeEn", "BirthdayEn", "CharHeightEn", "HobbyEn",  "WeaponNameEn", "WeaponDescEn", "ProfileIntroductionEn", "CharacterSSRNewEn"] },
            new() { FileName = "ScenarioCharacterName.json", TextFields = ["NameEN", "NicknameEN"] },
            new() { FileName = "CharacterVoiceSubtitle.json", TextFields = ["LocalizeEN"] },
            new() { FileName = "CharacterDialogSubtitle.json", TextFields = ["LocalizeEN"] },
            new() { FileName = "AcademyMessanger.json", TextFields = ["MessageEN"] },
            new() { FileName = "CharacterDialogBattlePass.json", TextFields = ["LocalizeEN"] },
            new() { FileName = "CharacterDialog.json", TextFields = ["LocalizeEN"] },
            new() { FileName = "CharacterDialogEvent.json", TextFields = ["LocalizeEN"] },
            new() { FileName = "CharacterDialogEmoji.json", TextFields = ["LocalizeEN"] },
            new() { FileName = "ScenarioScript.json", TextFields = ["TextEn"] },
            new() { FileName = "TutorialCharacterDialog.json", TextFields = ["LocalizeEN"] }
        };

        public static bool GenerateRepositoryFiles(string inputFullJsonDir, string outputRepoDir)
        {
            if (!Directory.Exists(inputFullJsonDir))
            {
                Logger.Log($"- Входная папка с JSON не найдена: {inputFullJsonDir}");
                return false;
            }
            string rowIdDir = Path.Combine(outputRepoDir, "RowId");
            string mappedDir = Path.Combine(outputRepoDir, "Mapped");

            if (!Directory.Exists(rowIdDir)) Directory.CreateDirectory(rowIdDir);
            if (!Directory.Exists(mappedDir)) Directory.CreateDirectory(mappedDir);

            Logger.Log("* Начало генерации файлов");

            var combinedMappedData = new Dictionary<string, object>();
            int totalProcessedFiles = 0;
            int totalExtractedLines = 0;

            foreach (var cfg in TargetConfigs)
            {
                string inputFilePath = Path.Combine(inputFullJsonDir, cfg.FileName);
                if (!File.Exists(inputFilePath))
                {
                    Logger.Log($"* Пропущен (файл не найден): {cfg.FileName}");
                    continue;
                }

                try
                {
                    string jsonContent = File.ReadAllText(inputFilePath);
                    var rows = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonContent);
                    if (rows == null || rows.Count == 0) continue;

                    string baseName = Path.GetFileNameWithoutExtension(cfg.FileName);
                    string tableName = $"{baseName}ExcelTable";

                    var rowIdMap = new Dictionary<string, object>();
                    var mappedMap = new Dictionary<string, object>();
                    var groupCounters = new Dictionary<string, int>();

                    foreach (var row in rows)
                    {
                        string rowIdStr = row.TryGetValue("RowId", out var rVal) ? TableMapper.GetString(rVal) : "";
                        if (string.IsNullOrEmpty(rowIdStr)) continue;

                        string mappedKey = TableMapper.GetRowKey(tableName, row, groupCounters);

                        if (cfg.TextFields.Length == 1)
                        {
                            string field = cfg.TextFields[0];
                            string text = row.TryGetValue(field, out var tVal) ? TableMapper.GetString(tVal) : "";

                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                rowIdMap[rowIdStr] = text;
                                if (!string.IsNullOrEmpty(mappedKey))
                                    mappedMap[mappedKey] = text;

                                totalExtractedLines++;
                            }
                        }
                        else
                        {
                            var multiFieldRow = new Dictionary<string, string>();
                            foreach (var field in cfg.TextFields)
                            {
                                string text = row.TryGetValue(field, out var tVal) ? TableMapper.GetString(tVal) : "";
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    multiFieldRow[field] = text;
                                }
                            }

                            if (multiFieldRow.Count > 0)
                            {
                                rowIdMap[rowIdStr] = multiFieldRow;
                                if (!string.IsNullOrEmpty(mappedKey))
                                    mappedMap[mappedKey] = multiFieldRow;

                                totalExtractedLines += multiFieldRow.Count;
                            }
                        }
                    }

                    string outRowIdFile = Path.Combine(rowIdDir, cfg.FileName);
                    File.WriteAllText(outRowIdFile, JsonSerializer.Serialize(rowIdMap, JsonOptions));

                    string outMappedFile = Path.Combine(mappedDir, cfg.FileName);
                    File.WriteAllText(outMappedFile, JsonSerializer.Serialize(mappedMap, JsonOptions));

                    combinedMappedData[tableName] = mappedMap;
                    totalProcessedFiles++;

                    Logger.Log($"+ Таблица {cfg.FileName}: извлечено {rowIdMap.Count} строк перевода.");
                }
                catch (Exception ex)
                {
                    Logger.Log($"- Ошибка обработки {cfg.FileName}: {ex.Message}");
                }
            }

            try
            {
                string rawCombinedJson = JsonSerializer.Serialize(combinedMappedData);
                string compressedPatchPath = Path.Combine(outputRepoDir, "patch_data.json.gz");

                using (var outFile = File.Create(compressedPatchPath))
                using (var gz = new GZipStream(outFile, CompressionLevel.Optimal))
                using (var writer = new StreamWriter(gz))
                {
                    writer.Write(rawCombinedJson);
                }

                var gzInfo = new FileInfo(compressedPatchPath);
                Logger.Log($"+ Патч файл сжатый создан: {compressedPatchPath} (Размер: {gzInfo.Length / 1024 / 1024:N2} МБ)");
            }
            catch (Exception ex)
            {
                Logger.Log($"- Ошибка создания сжатого патча: {ex.Message}");
            }

            Logger.Log($"+ Генерация завершена, Файлов: {totalProcessedFiles}, Строк текста: {totalExtractedLines}");
            return true;
        }
    }
}