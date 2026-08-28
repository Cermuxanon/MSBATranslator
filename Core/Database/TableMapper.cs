using System.Text.Json;

namespace MSBATranslator.Core.Database
{
    public static class TableMapper
    {
        public static readonly HashSet<string> TargetLocalizeTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "LocalizeExcelTable",
            "LocalizeErrorExcelTable",
            "LocalizeSkillExcelTable",
            "LocalizeEtcExcelTable",
            "LocalizeGachaShopExcelTable",
            "LocalizeCharProfileExcelTable",
            "ScenarioCharacterNameExcelTable",
            "CharacterVoiceSubtitleExcelTable",
            "CharacterDialogSubtitleExcelTable",
            "AcademyMessangerExcelTable",
            "CharacterDialogBattlePassExcelTable",
            "CharacterDialogExcelTable",
            "CharacterDialogEventExcelTable",
            "CharacterDialogEmojiExcelTable",
            "ScenarioScriptExcelTable",
            "TutorialCharacterDialogExcelTable"
        };

        public static long GetLong(object? obj)
        {
            if (obj == null) return 0;
            if (obj is Enum) return Convert.ToInt64(obj);

            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out long lVal)) return lVal;
                if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out long sVal)) return sVal;
                return 0;
            }

            if (obj is int i) return i;
            if (obj is long l) return l;
            if (obj is uint u) return u;
            if (obj is ulong ul) return (long)ul;
            if (obj is short s) return s;
            if (obj is ushort us) return us;
            if (obj is byte b) return b;
            if (obj is sbyte sb) return sb;

            if (long.TryParse(obj.ToString(), out long parsed)) return parsed;
            return 0;
        }

        public static string GetString(object? obj)
        {
            if (obj == null) return "";

            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.String) return je.GetString() ?? "";
                if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined) return "";
                return je.ToString();
            }

            return obj.ToString() ?? "";
        }

        public static string GetRowKey(string tableName, Dictionary<string, object> row, Dictionary<string, int> groupCounters)
        {
            string baseName = TableNameHelper.NormalizeBaseName(tableName);

            switch (baseName)
            {
                case "Localize":
                case "LocalizeError":
                case "LocalizeSkill":
                case "LocalizeEtc":
                    return row.TryGetValue("Key", out var k) ? GetString(k) : "";

                case "CharacterVoiceSubtitle":
                case "CharacterDialogSubtitle":
                    return row.TryGetValue("TLMID", out var tlm) ? GetString(tlm) : "";

                case "AcademyMessanger":
                    {
                        string mgId = row.TryGetValue("MessageGroupId", out var mg) ? GetString(mg) : "0";
                        string id = row.TryGetValue("Id", out var iVal) ? GetString(iVal) : "0";
                        return $"{mgId}_{id}";
                    }

                case "LocalizeGachaShop":
                    return row.TryGetValue("GachaShopId", out var gShop) ? GetString(gShop) : "";

                case "LocalizeCharProfile":
                    return row.TryGetValue("CharacterId", out var cProf) ? GetString(cProf) : "";

                case "ScenarioCharacterName":
                    return row.TryGetValue("CharacterName", out var scName) ? GetString(scName) : "";

                case "CharacterDialogBattlePass":
                    {
                        long bpId = row.TryGetValue("BattlePassID", out var bp) ? GetLong(bp) : 0;
                        long cId = row.TryGetValue("CostumeUniqueId", out var cu) ? GetLong(cu) : 0;
                        long gId = row.TryGetValue("GroupId", out var g) ? GetLong(g) : 0;
                        long dOrder = row.TryGetValue("DisplayOrder", out var d) ? GetLong(d) : 0;
                        return $"{bpId}_{cId}_{gId}_{dOrder}";
                    }

                case "CharacterDialog":
                    {
                        long costumeId = row.TryGetValue("CostumeUniqueId", out var cVal) ? GetLong(cVal) : 0;
                        long charId = row.TryGetValue("CharacterId", out var chVal) ? GetLong(chVal) : 0;
                        long effectiveCid = costumeId != 0 ? costumeId : charId;

                        long cat = row.TryGetValue("DialogCategory", out var catVal) ? GetLong(catVal) : 0;
                        long gid = row.TryGetValue("GroupId", out var gVal) ? GetLong(gVal) : 0;

                        string gKey = $"{effectiveCid}_{cat}_{gid}";
                        int seq = groupCounters.GetValueOrDefault(gKey, 0);
                        groupCounters[gKey] = seq + 1;
                        return $"{gKey}_{seq}";
                    }

                case "CharacterDialogEvent":
                    {
                        long eid = row.TryGetValue("EventID", out var eVal) ? GetLong(eVal) : 0;
                        long cid = row.TryGetValue("CostumeUniqueId", out var cVal) ? GetLong(cVal) : 0;
                        long gid = row.TryGetValue("GroupId", out var gVal) ? GetLong(gVal) : 0;

                        string gKey = $"{eid}_{cid}_{gid}";
                        int seq = groupCounters.GetValueOrDefault(gKey, 0);
                        groupCounters[gKey] = seq + 1;
                        return $"{gKey}_{seq}";
                    }

                case "TutorialCharacterDialog":
                    {
                        long talkId = row.TryGetValue("TalkId", out var tVal) ? GetLong(tVal)
                            : (row.TryGetValue("GroupId", out var gVal) ? GetLong(gVal) : 0);
                        string gKey = talkId.ToString();
                        int seq = groupCounters.GetValueOrDefault(gKey, 0);
                        groupCounters[gKey] = seq + 1;
                        return $"{gKey}_{seq}";
                    }

                case "CharacterDialogEmoji":
                case "ScenarioScript":
                    {
                        long gid = row.TryGetValue("GroupId", out var gVal) ? GetLong(gVal) : 0;
                        string gKey = gid.ToString();
                        int seq = groupCounters.GetValueOrDefault(gKey, 0);
                        groupCounters[gKey] = seq + 1;
                        return $"{gKey}_{seq}";
                    }

                default:
                    return row.TryGetValue("Key", out var defK) ? GetString(defK) : "";
            }
        }

        public static Func<object, Dictionary<string, int>, Dictionary<long, int>, string> CompileKeyExtractor(string rawTableName, Type classType)
        {
            string baseName = TableNameHelper.NormalizeBaseName(rawTableName);

            if (baseName.Equals("ScenarioScript", StringComparison.OrdinalIgnoreCase) ||
                baseName.Equals("CharacterDialogEmoji", StringComparison.OrdinalIgnoreCase))
            {
                var pGid = classType.GetProperty("GroupId");
                if (pGid != null)
                {
                    return (inst, _, longCounters) =>
                    {
                        long gid = Convert.ToInt64(pGid.GetValue(inst));
                        int seq = longCounters.GetValueOrDefault(gid, 0);
                        longCounters[gid] = seq + 1;
                        return $"{gid}_{seq}";
                    };
                }
            }

            if (baseName.Equals("LocalizeGachaShop", StringComparison.OrdinalIgnoreCase))
            {
                var pShop = classType.GetProperty("GachaShopId") ?? classType.GetProperty("ShopId");
                if (pShop != null)
                {
                    return (inst, _, _) => pShop.GetValue(inst)?.ToString() ?? "";
                }
            }

            if (baseName.Equals("LocalizeCharProfile", StringComparison.OrdinalIgnoreCase))
            {
                var pChar = classType.GetProperty("CharacterId");
                if (pChar != null)
                {
                    return (inst, _, _) => pChar.GetValue(inst)?.ToString() ?? "";
                }
            }

            if (baseName.Equals("ScenarioCharacterName", StringComparison.OrdinalIgnoreCase))
            {
                var pName = classType.GetProperty("CharacterName");
                if (pName != null)
                {
                    return (inst, _, _) => pName.GetValue(inst)?.ToString() ?? "";
                }
            }

            if (baseName.Equals("CharacterDialogBattlePass", StringComparison.OrdinalIgnoreCase))
            {
                var pBp = classType.GetProperty("BattlePassID");
                var pCostume = classType.GetProperty("CostumeUniqueId");
                var pGid = classType.GetProperty("GroupId");
                var pOrder = classType.GetProperty("DisplayOrder");

                return (inst, _, _) =>
                {
                    long bpId = pBp != null ? Convert.ToInt64(pBp.GetValue(inst)) : 0;
                    long cId = pCostume != null ? Convert.ToInt64(pCostume.GetValue(inst)) : 0;
                    long gId = pGid != null ? Convert.ToInt64(pGid.GetValue(inst)) : 0;
                    long dOrder = pOrder != null ? Convert.ToInt64(pOrder.GetValue(inst)) : 0;
                    return $"{bpId}_{cId}_{gId}_{dOrder}";
                };
            }

            if (baseName.Contains("Subtitle", StringComparison.OrdinalIgnoreCase))
            {
                var pTlm = classType.GetProperty("TLMID");
                if (pTlm != null) return (inst, _, _) => pTlm.GetValue(inst)?.ToString() ?? "";
            }

            if (baseName.Equals("AcademyMessanger", StringComparison.OrdinalIgnoreCase))
            {
                var pMg = classType.GetProperty("MessageGroupId");
                var pId = classType.GetProperty("Id");
                if (pMg != null && pId != null)
                {
                    return (inst, _, _) => $"{pMg.GetValue(inst)}_{pId.GetValue(inst)}";
                }
            }

            if (baseName.Equals("CharacterDialog", StringComparison.OrdinalIgnoreCase))
            {
                var pCostume = classType.GetProperty("CostumeUniqueId");
                var pChar = classType.GetProperty("CharacterId");
                var pCat = classType.GetProperty("DialogCategory");
                var pGid = classType.GetProperty("GroupId");

                return (inst, groupCounters, _) =>
                {
                    long costumeId = pCostume != null ? Convert.ToInt64(pCostume.GetValue(inst)) : 0;
                    long charId = pChar != null ? Convert.ToInt64(pChar.GetValue(inst)) : 0;
                    long effectiveCid = costumeId != 0 ? costumeId : charId;
                    long cat = pCat != null ? Convert.ToInt64(pCat.GetValue(inst)) : 0;
                    long gid = pGid != null ? Convert.ToInt64(pGid.GetValue(inst)) : 0;

                    string gKey = $"{effectiveCid}_{cat}_{gid}";
                    int seq = groupCounters.GetValueOrDefault(gKey, 0);
                    groupCounters[gKey] = seq + 1;
                    return $"{gKey}_{seq}";
                };
            }

            if (baseName.Equals("CharacterDialogEvent", StringComparison.OrdinalIgnoreCase))
            {
                var pEvent = classType.GetProperty("EventID");
                var pCostume = classType.GetProperty("CostumeUniqueId");
                var pGid = classType.GetProperty("GroupId");

                return (inst, groupCounters, _) =>
                {
                    long eid = pEvent != null ? Convert.ToInt64(pEvent.GetValue(inst)) : 0;
                    long cid = pCostume != null ? Convert.ToInt64(pCostume.GetValue(inst)) : 0;
                    long gid = pGid != null ? Convert.ToInt64(pGid.GetValue(inst)) : 0;

                    string gKey = $"{eid}_{cid}_{gid}";
                    int seq = groupCounters.GetValueOrDefault(gKey, 0);
                    groupCounters[gKey] = seq + 1;
                    return $"{gKey}_{seq}";
                };
            }

            if (baseName.Equals("TutorialCharacterDialog", StringComparison.OrdinalIgnoreCase))
            {
                var pTalk = classType.GetProperty("TalkId") ?? classType.GetProperty("GroupId");
                if (pTalk != null)
                {
                    return (inst, _, longCounters) =>
                    {
                        long talkId = Convert.ToInt64(pTalk.GetValue(inst));
                        int seq = longCounters.GetValueOrDefault(talkId, 0);
                        longCounters[talkId] = seq + 1;
                        return $"{talkId}_{seq}";
                    };
                }
            }

            var pKey = classType.GetProperty("Key");
            if (pKey != null)
            {
                return (inst, _, _) => pKey.GetValue(inst)?.ToString() ?? "";
            }

            var pIdFallback = classType.GetProperty("Id") ?? classType.GetProperty("ID");
            if (pIdFallback != null)
            {
                return (inst, _, _) => pIdFallback.GetValue(inst)?.ToString() ?? "";
            }

            return (inst, _, _) => "";
        }
    }
}
