using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.Database;

namespace MSBATranslator.Core.FlatData
{
    public static class RoslynCompiler
    {
        public static Assembly? CompiledAssembly { get; private set; }
        public static bool EnsureCompiled(IEnumerable<string>? targetTableNames = null)
        {
            if (CompiledAssembly != null) return true;

            string csDir = AppPaths.GeneratedFlatDataDir;
            return CompileFlatDataInMemory(csDir, targetTableNames);
        }

        public static bool CompileFlatDataInMemory(string csDirectory, IEnumerable<string>? targetTableNames = null)
        {
            if (!Directory.Exists(csDirectory))
            {
                Logger.Log($"- Папка сгенерированных FlatData не найдена: {csDirectory}");
                return false;
            }

            var allCsFiles = Directory.GetFiles(csDirectory, "*.cs");
            if (allCsFiles.Length == 0)
            {
                Logger.Log("- В папке GeneratedFlatData отсутствуют .cs файлы. Сначала выполните генерацию на вкладке FlatData.");
                return false;
            }

            try
            {
                var filesToCompile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allFileMap = allCsFiles.ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => f, StringComparer.OrdinalIgnoreCase);

                string stubs = Path.Combine(csDirectory, "UnityStubs.cs");
                if (File.Exists(stubs)) filesToCompile.Add(stubs);

                foreach (var (k, fullPath) in allFileMap)
                {
                    string firstLines = File.ReadLines(fullPath).Take(5).Aggregate("", (a, b) => a + " " + b);
                    if (firstLines.Contains("public enum"))
                    {
                        filesToCompile.Add(fullPath);
                    }
                }

                if (targetTableNames != null && targetTableNames.Any())
                {
                    foreach (var rawName in targetTableNames)
                    {
                        string baseName = TableNameHelper.NormalizeBaseName(rawName);
                        string[] candidates = { $"{baseName}Excel", $"{baseName}Table", baseName, rawName };
                        foreach (var cand in candidates)
                        {
                            if (allFileMap.TryGetValue(cand, out var fullPath))
                            {
                                filesToCompile.Add(fullPath);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    foreach (var f in allCsFiles) filesToCompile.Add(f);
                }

                bool addedNew = true;
                int pass = 0;
                while (addedNew && pass++ < 5)
                {
                    addedNew = false;
                    var currentBatch = filesToCompile.ToList();
                    foreach (var filePath in currentBatch)
                    {
                        string code = File.ReadAllText(filePath);
                        foreach (var (typeName, candidatePath) in allFileMap)
                        {
                            if (!filesToCompile.Contains(candidatePath) && code.Contains(typeName))
                            {
                                filesToCompile.Add(candidatePath);
                                addedNew = true;
                            }
                        }
                    }
                }

                Logger.Log($"* Roslyn компиляция: {filesToCompile.Count} файлов в RAM");

                var syntaxTrees = new List<SyntaxTree>();
                foreach (var file in filesToCompile)
                {
                    string code = File.ReadAllText(file);
                    syntaxTrees.Add(CSharpSyntaxTree.ParseText(code, path: Path.GetFileName(file)));
                }

                var references = new List<MetadataReference>();

                string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
                if (!string.IsNullOrEmpty(trustedPlatformAssemblies))
                {
                    foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
                    {
                        if (!string.IsNullOrEmpty(path))
                            references.Add(MetadataReference.CreateFromFile(path));
                    }
                }

                references.Add(MetadataReference.CreateFromFile(typeof(Google.FlatBuffers.ByteBuffer).Assembly.Location));

                var compilation = CSharpCompilation.Create(
                    assemblyName: $"DynamicFlatData_{Guid.NewGuid():N}.dll",
                    syntaxTrees: syntaxTrees,
                    references: references,
                    options: new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Release,
                        allowUnsafe: true)
                );

                using var ms = new MemoryStream();
                var result = compilation.Emit(ms);

                if (!result.Success)
                {
                    int errCount = 0;
                    foreach (var diag in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        if (errCount++ < 5)
                            Logger.Log($"- Ошибка Roslyn: {diag.GetMessage()} ({diag.Location.GetLineSpan()})");
                    }
                    return false;
                }

                ms.Seek(0, SeekOrigin.Begin);
                CompiledAssembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(ms);

                Logger.Log($"+ Сборка FlatData в RAM успешно создана ({filesToCompile.Count} файлов)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"- Ошибка при компиляции Roslyn: {ex.Message}");
                return false;
            }
        }
    }
}