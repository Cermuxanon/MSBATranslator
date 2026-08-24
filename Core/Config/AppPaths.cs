namespace MSBATranslator.Core.Config
{
    public static class AppPaths
    {
        public static string BaseDir { get; } = 
            Path.GetDirectoryName(Environment.ProcessPath) 
            ?? AppDomain.CurrentDomain.BaseDirectory;

        public static string DataDir => Path.Combine(BaseDir, "Data");
        public static string ConfigPath => Path.Combine(DataDir, "Config.json");
        public static string BackupsDir => Path.Combine(DataDir, "Backups");
        public static string OriginalBackupFile => Path.Combine(BackupsDir, "ExcelDB_original.db");
        public static string GeneratedFlatDataDir => Path.Combine(DataDir, "GeneratedFlatData");
        public static string DummyDllDir => Path.Combine(DataDir, "DummyDll");
        public static string TargetBlueArchiveDll => Path.Combine(DummyDllDir, "BlueArchive.dll");
        public static string RepositoryDir => Path.Combine(DataDir, "Repository");
        public static string DefaultPatchFile => Path.Combine(RepositoryDir, "patch_data.json.gz");
        public static string JsonExportDir => Path.Combine(DataDir, "Json_Export");
        public static string Il2CppInspectorDir => Path.Combine(DataDir, "Il2CppInspector");
        public static string TempDir => Path.Combine(BaseDir, "Temp");
    }
}