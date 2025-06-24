// Program.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using System.CommandLine;
using System.CommandLine.Invocation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Define CLI options
        var inputOption = new Option<FileInfo>(
            new[] { "--input", "-i" },
            "Path to the solution (.sln) or project (.csproj) file to analyze."
        )
        {
            IsRequired = true,
            ArgumentHelpName = "input"
        };

        var outputOption = new Option<DirectoryInfo>(
            new[] { "--output", "-o" },
            () => new DirectoryInfo(Directory.GetCurrentDirectory()),
            "Directory to write output files. Defaults to current directory."
        )
        {
            ArgumentHelpName = "output"
        };

        var rootCommand = new RootCommand("RoslynCodeAnalyzer - Analyze C# solutions and output code context and logs.")
        {
            inputOption,
            outputOption
        };

        rootCommand.SetHandler(async (FileInfo input, DirectoryInfo output) =>
        {
            int exitCode = await RunAnalyzerAsync(input, output);
            Environment.ExitCode = exitCode;
        }, inputOption, outputOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task<int> RunAnalyzerAsync(FileInfo input, DirectoryInfo output)
    {
        StreamWriter? logWriter = null;
        StreamWriter? analysisWriter = null;

        try
        {
            // Validate input file
            if (input == null || !input.Exists)
            {
                Console.Error.WriteLine($"ERROR: Input file not found or not specified.");
                return 1;
            }

            // Validate output directory
            if (output == null)
            {
                output = new DirectoryInfo(Directory.GetCurrentDirectory());
            }
            if (!output.Exists)
            {
                output.Create();
            }

            // Determine base name for output files
            string baseName = Path.GetFileNameWithoutExtension(input.Name);
            string contextFileName = $"{baseName}_codecontext.log";
            string logFileName = $"{baseName}_analyzer.log";
            string contextFilePath = Path.Combine(output.FullName, contextFileName);
            string logFilePath = Path.Combine(output.FullName, logFileName);

            // Open writers
            logWriter = new StreamWriter(logFilePath, append: false);
            analysisWriter = new StreamWriter(contextFilePath, append: false);

            // Helper log function
            void Log(string message)
            {
                string timestamp = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                Console.WriteLine(message);
                logWriter?.WriteLine($"{timestamp} {message}");
                logWriter?.Flush();
            }

            Log("RoslynCodeAnalyzer started.");
            Log($"Input: {input.FullName}");
            Log($"Output directory: {output.FullName}");

            Log("Loading workspace... This might take a moment.");
            using var workspace = MSBuildWorkspace.Create();
            workspace.LoadMetadataForReferencedProjects = true;

            Solution solution;
            if (input.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
            {
                solution = await workspace.OpenSolutionAsync(input.FullName);
            }
            else
            {
                solution = (await workspace.OpenProjectAsync(input.FullName)).Solution;
            }

            Log("Workspace loaded. Finding symbols and references...");

            // --- Step 1: Discover all relevant symbols (classes, methods, etc.) ---
            var symbolsToAnalyze = new List<ISymbol>();
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    // Find all type (class, struct, interface) and method declarations
                    var declarations = root.DescendantNodes()
                        .OfType<BaseTypeDeclarationSyntax>()
                        .Cast<SyntaxNode>()
                        .Concat(root.DescendantNodes().OfType<MethodDeclarationSyntax>());

                    foreach (var declaration in declarations)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(declaration);
                        if (symbol != null)
                        {
                            symbolsToAnalyze.Add(symbol);
                        }
                    }
                }
            }

            symbolsToAnalyze = symbolsToAnalyze.Distinct(SymbolEqualityComparer.Default).ToList();
            Log($"Found {symbolsToAnalyze.Count} total symbols to analyze.");
            Log("");

            // Prepare for relative path calculation
            string solutionDir = input.DirectoryName ?? Directory.GetCurrentDirectory();

            int symbolIndex = 1;
            // --- Step 2: For each symbol, find its definition and references ---
            foreach (var symbol in symbolsToAnalyze)
            {
                string symbolDisplayName = symbol.ToDisplayString();
                string symbolKind = symbol.Kind.ToString();

                // Log progress (log file only)
                Log("=====================================================================");
                Log($"ANALYZING SYMBOL {symbolIndex} of {symbolsToAnalyze.Count}: {symbolDisplayName}");
                Log($"Kind: {symbolKind}");
                Log("=====================================================================");

                // --- Write to analysis file (no timestamps, no progress, just context) ---
                await analysisWriter.WriteLineAsync($"SYMBOL: {symbolDisplayName}");
                await analysisWriter.WriteLineAsync($"Kind: {symbolKind}");

                // --- Definition metadata ---
                var definitionLocation = symbol.Locations.FirstOrDefault();
                if (definitionLocation != null && definitionLocation.SourceTree != null)
                {
                    var absFilePath = definitionLocation.SourceTree.FilePath;
                    var relFilePath = absFilePath != null ? GetRelativePath(absFilePath, solutionDir) : "(unknown)";
                    var lineNumber = definitionLocation.GetLineSpan().StartLinePosition.Line + 1;
                    string signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                    await analysisWriter.WriteLineAsync($"Definition: {relFilePath}:{lineNumber}");
                    await analysisWriter.WriteLineAsync($"Signature: {signature}");
                }
                else
                {
                    await analysisWriter.WriteLineAsync("Definition: (No source location found)");
                }

                // --- References ---
                await analysisWriter.WriteLineAsync("References:");
                var references = await SymbolFinder.FindReferencesAsync(symbol, solution);

                int refCount = 0;
                foreach (var referencedSymbol in references)
                {
                    foreach (var location in referencedSymbol.Locations)
                    {
                        var doc = location.Document;
                        var loc = location.Location;
                        var absRefFilePath = doc.FilePath;
                        var relRefFilePath = absRefFilePath != null ? GetRelativePath(absRefFilePath, solutionDir) : "(unknown)";
                        var refLineNumber = loc.GetLineSpan().StartLinePosition.Line + 1;
                        await analysisWriter.WriteLineAsync($"  - {relRefFilePath}:{refLineNumber}");
                        refCount++;
                    }
                }
                if (refCount == 0)
                {
                    await analysisWriter.WriteLineAsync("  (No references found in the solution.)");
                }
                await analysisWriter.WriteLineAsync(""); // Blank line between symbols

                symbolIndex++;
            }

            Log("Analysis complete.");
            return 0;
        }
        catch (Exception ex)
        {
            string errorMsg = $"ERROR: {ex.Message}";
            Console.Error.WriteLine(errorMsg);
            logWriter?.WriteLine(errorMsg);
            logWriter?.WriteLine(ex.StackTrace ?? "");
            return 2;
        }
        finally
        {
            logWriter?.Dispose();
            analysisWriter?.Dispose();
        }
    }

    // Returns the relative path from 'baseDir' to 'fullPath'
    private static string GetRelativePath(string fullPath, string baseDir)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(baseDir));
            var fileUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
        catch
        {
            // Fallback to absolute path if any error
            return fullPath;
        }
    }

    private static string AppendDirectorySeparatorChar(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            return path + Path.DirectorySeparatorChar;
        return path;
    }
}
