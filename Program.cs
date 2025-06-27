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

        // New CLI options
        var symbolOption = new Option<string>(
            new[] { "--symbol", "-s" },
            "Symbol name or pattern to filter (optional)."
        )
        {
            ArgumentHelpName = "symbol"
        };

        var relationLevelOption = new Option<string>(
            new[] { "--relation-level", "-r" },
            () => "direct",
            "Relation level: direct, references, inheritance, all (default: direct)."
        )
        {
            ArgumentHelpName = "relation-level"
        };

        var snippetLevelOption = new Option<string>(
            new[] { "--snippet-level", "-n" },
            () => "none",
            "Snippet level: none, line, block (default: none)."
        )
        {
            ArgumentHelpName = "snippet-level"
        };

        var outputFormatOption = new Option<string>(
            new[] { "--output-format", "-f" },
            () => "txt",
            "Output format: txt, json, md (default: txt)."
        )
        {
            ArgumentHelpName = "output-format"
        };

        var rootCommand = new RootCommand("RoslynCodeAnalyzer - Analyze C# solutions and output code context and logs.")
        {
            inputOption,
            outputOption,
            symbolOption,
            relationLevelOption,
            snippetLevelOption,
            outputFormatOption
        };

        rootCommand.SetHandler(async (FileInfo input, DirectoryInfo output, string symbolPattern, string relationLevel, string snippetLevel, string outputFormat) =>
        {
            int exitCode = await RunAnalyzerAsync(input, output, symbolPattern, relationLevel, snippetLevel, outputFormat);
            Environment.ExitCode = exitCode;
        }, inputOption, outputOption, symbolOption, relationLevelOption, snippetLevelOption, outputFormatOption);

        return await rootCommand.InvokeAsync(args);
    }

    // Updated RunAnalyzerAsync signature to accept new options
private static async Task<int> RunAnalyzerAsync(FileInfo input, DirectoryInfo output, string symbolPattern, string relationLevel, string snippetLevel, string outputFormat)
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
            string contextFileName = $"{baseName}_codecontext.txt";
            string logFileName = $"{baseName}_analyzer.log";
            string contextFilePath = Path.Combine(output.FullName, contextFileName);
            string logFilePath = Path.Combine(output.FullName, logFileName);

            // Open writers
            logWriter = new StreamWriter(logFilePath, append: false);

            // Select output file extension and writer
            string outExt = outputFormat.ToLowerInvariant() switch
            {
                "json" => "json",
                "md" => "md",
                _ => "txt"
            };
            string outFileName = $"{baseName}_codecontext.{outExt}";
            string outFilePath = Path.Combine(output.FullName, outFileName);
            analysisWriter = new StreamWriter(outFilePath, append: false);

            IContextWriter contextWriter = outputFormat.ToLowerInvariant() switch
            {
                "json" => new JsonContextWriter(analysisWriter),
                "md" => new MdContextWriter(analysisWriter),
                _ => new TxtContextWriter(analysisWriter)
            };

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
    var declaredSymbol = semanticModel.GetDeclaredSymbol(declaration);
    if (declaredSymbol != null)
    {
        symbolsToAnalyze.Add(declaredSymbol);
    }
}
                }
            }

            symbolsToAnalyze = symbolsToAnalyze.Distinct(SymbolEqualityComparer.Default).ToList();

            // Filter by symbol name/pattern if provided
            if (!string.IsNullOrWhiteSpace(symbolPattern))
            {
                // Case-insensitive substring match
                symbolsToAnalyze = symbolsToAnalyze
                    .Where(s => s.Name != null && s.Name.IndexOf(symbolPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                Log($"Filtered symbols by pattern '{symbolPattern}': {symbolsToAnalyze.Count} match(es) found.");
            }
            else
            {
                Log($"Found {symbolsToAnalyze.Count} total symbols to analyze.");
            }
            Log("");

            // Expand symbols to include related symbols as per relationLevel
            symbolsToAnalyze = await ExpandRelatedSymbolsAsync(symbolsToAnalyze, solution, relationLevel, Log);

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

                // --- Definition metadata ---
                var definitionLocation = symbol.Locations.FirstOrDefault();
                string defAbsFilePath = definitionLocation?.SourceTree?.FilePath ?? "";
                string defRelFilePath = defAbsFilePath != "" ? GetRelativePath(defAbsFilePath, solutionDir) : "(unknown)";
int defLineNumber = definitionLocation?.GetLineSpan().StartLinePosition.Line + 1 ?? -1;
                string signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                // --- Code snippet for symbol definition ---
                string defSnippet = "";
                if (snippetLevel != null && snippetLevel.ToLowerInvariant() != "none" && definitionLocation != null && definitionLocation.SourceTree != null)
                {
                    var tree = definitionLocation.SourceTree;
                    var span = definitionLocation.SourceSpan;
                    var text = tree.GetText();
                    if (snippetLevel.ToLowerInvariant() == "line")
                    {
                        var line = text.Lines.GetLineFromPosition(span.Start);
                        defSnippet = line.ToString();
                    }
                    else if (snippetLevel.ToLowerInvariant() == "block")
                    {
                        var root = await tree.GetRootAsync();
                        var node = root.FindNode(span);
                        defSnippet = node.ToFullString();
                    }
                }

                await contextWriter.WriteSymbolAsync(
                    symbol,
                    symbolKind,
                    defRelFilePath != "(unknown)" && defLineNumber > 0 ? $"{defRelFilePath}:{defLineNumber}" : defRelFilePath,
                    signature,
                    defSnippet
                );

                // --- References ---
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

                        // Code snippet for reference
                        string refSnippet = "";
                        if (snippetLevel != null && snippetLevel.ToLowerInvariant() != "none" && loc.SourceTree != null)
                        {
                            var tree = loc.SourceTree;
                            var span = loc.SourceSpan;
                            var text = tree.GetText();
                            if (snippetLevel.ToLowerInvariant() == "line")
                            {
                                var line = text.Lines.GetLineFromPosition(span.Start);
                                refSnippet = line.ToString();
                            }
                            else if (snippetLevel.ToLowerInvariant() == "block")
                            {
                                var root = await tree.GetRootAsync();
                                var node = root.FindNode(span);
                                refSnippet = node.ToFullString();
                            }
                        }

                        await contextWriter.WriteReferenceAsync(relRefFilePath, refLineNumber, refSnippet);
                        refCount++;
                    }
                }
                if (refCount == 0)
                {
                    await contextWriter.WriteReferenceAsync("(No references found in the solution.)", -1, "");
                }
                await contextWriter.NextSymbolAsync();

                symbolIndex++;
            }

            await contextWriter.CompleteAsync();

            // Helper: Expand related symbols based on relationLevel
            static async Task<List<ISymbol>> ExpandRelatedSymbolsAsync(List<ISymbol> inputSymbols, Solution solution, string relationLevel, Action<string> log)
            {
                var comparer = SymbolEqualityComparer.Default;
                var result = new HashSet<ISymbol>(inputSymbols, comparer);

                if (string.Equals(relationLevel, "direct", StringComparison.OrdinalIgnoreCase))
                    return result.ToList();

                // Add referenced and referencing symbols
                if (string.Equals(relationLevel, "references", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relationLevel, "all", StringComparison.OrdinalIgnoreCase))
                {
                    var toAdd = new HashSet<ISymbol>(comparer);
foreach (var inputSymbol in inputSymbols)
{
    // Referenced symbols (symbols this symbol uses)
    if (inputSymbol is INamedTypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IMethodSymbol || member is IPropertySymbol || member is IFieldSymbol)
                toAdd.Add(member);
        }
        if (typeSymbol.BaseType != null)
            toAdd.Add(typeSymbol.BaseType);
        foreach (var iface in typeSymbol.AllInterfaces)
            toAdd.Add(iface);
    }
    // Referencing symbols (symbols that use this symbol)
    var references = await SymbolFinder.FindReferencesAsync(inputSymbol, solution);
    foreach (var referencedSymbol in references)
    {
        foreach (var location in referencedSymbol.Locations)
        {
            var doc = location.Document;
            var model = await doc.GetSemanticModelAsync();
            if (model != null)
            {
                var node = await location.Location.SourceTree.GetRootAsync();
                var refNode = node.FindNode(location.Location.SourceSpan);
                var refSymbol = model.GetDeclaredSymbol(refNode);
                if (refSymbol != null)
                    toAdd.Add(refSymbol);
            }
        }
    }
}
                    foreach (var s in toAdd)
                        result.Add(s);
                }

                // Add inheritance relations
                if (string.Equals(relationLevel, "inheritance", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relationLevel, "all", StringComparison.OrdinalIgnoreCase))
                {
                    var toAdd = new HashSet<ISymbol>(comparer);
foreach (var inputSymbol in inputSymbols)
{
    if (inputSymbol is INamedTypeSymbol typeSymbol)
    {
        // Base type
        if (typeSymbol.BaseType != null)
            toAdd.Add(typeSymbol.BaseType);
        // Derived types
        var derived = await SymbolFinder.FindDerivedClassesAsync(typeSymbol, solution);
        foreach (var d in derived)
            toAdd.Add(d);
        // Interfaces
        foreach (var iface in typeSymbol.AllInterfaces)
            toAdd.Add(iface);
    }
}
                    foreach (var s in toAdd)
                        result.Add(s);
                }

                log($"Expanded to {result.Count} symbols after applying relation level '{relationLevel}'.");
                return result.ToList();
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

    // --- Output Writers Abstraction ---

    public interface IContextWriter : IDisposable
    {
        Task WriteSymbolAsync(ISymbol symbol, string kind, string definition, string signature, string codeSnippet);
        Task WriteReferenceAsync(string refPath, int refLine, string codeSnippet);
        Task NextSymbolAsync();
        Task CompleteAsync();
    }

    public class TxtContextWriter : IContextWriter
    {
        private readonly StreamWriter _writer;
        public TxtContextWriter(StreamWriter writer) { _writer = writer; }
        public Task WriteSymbolAsync(ISymbol symbol, string kind, string definition, string signature, string codeSnippet)
        {
            _writer.WriteLine($"SYMBOL: {symbol.ToDisplayString()}");
            _writer.WriteLine($"Kind: {kind}");
            _writer.WriteLine($"Definition: {definition}");
            _writer.WriteLine($"Signature: {signature}");
            if (!string.IsNullOrEmpty(codeSnippet))
            {
                _writer.WriteLine("Code Snippet:");
                _writer.WriteLine("--------------------------------------------------");
                _writer.WriteLine(codeSnippet);
                _writer.WriteLine("--------------------------------------------------");
            }
            return Task.CompletedTask;
        }
        public Task WriteReferenceAsync(string refPath, int refLine, string codeSnippet)
        {
            _writer.WriteLine($"  - {refPath}:{refLine}");
            if (!string.IsNullOrEmpty(codeSnippet))
            {
                _writer.WriteLine("    Code Snippet:");
                _writer.WriteLine("    --------------------------------------------------");
                foreach (var line in codeSnippet.Split('\n'))
                    _writer.WriteLine("    " + line.TrimEnd('\r'));
                _writer.WriteLine("    --------------------------------------------------");
            }
            return Task.CompletedTask;
        }
        public Task NextSymbolAsync()
        {
            _writer.WriteLine();
            return Task.CompletedTask;
        }
        public Task CompleteAsync() => Task.CompletedTask;
        public void Dispose() => _writer.Dispose();
    }

    public class JsonContextWriter : IContextWriter
    {
        private readonly StreamWriter _writer;
        private readonly List<object> _symbols = new();
        private object? _currentSymbol;
        private List<object>? _currentReferences;
        public JsonContextWriter(StreamWriter writer) { _writer = writer; }
        public Task WriteSymbolAsync(ISymbol symbol, string kind, string definition, string signature, string codeSnippet)
        {
            _currentReferences = new List<object>();
            _currentSymbol = new Dictionary<string, object?>
            {
                ["symbol"] = symbol.ToDisplayString(),
                ["kind"] = kind,
                ["definition"] = definition,
                ["signature"] = signature,
                ["codeSnippet"] = string.IsNullOrEmpty(codeSnippet) ? null : codeSnippet,
                ["references"] = _currentReferences
            };
            _symbols.Add(_currentSymbol);
            return Task.CompletedTask;
        }
        public Task WriteReferenceAsync(string refPath, int refLine, string codeSnippet)
        {
            _currentReferences?.Add(new Dictionary<string, object?>
            {
                ["path"] = refPath,
                ["line"] = refLine,
                ["codeSnippet"] = string.IsNullOrEmpty(codeSnippet) ? null : codeSnippet
            });
            return Task.CompletedTask;
        }
        public Task NextSymbolAsync() => Task.CompletedTask;
        public Task CompleteAsync()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_symbols, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            _writer.WriteLine(json);
            return Task.CompletedTask;
        }
        public void Dispose() => _writer.Dispose();
    }

    public class MdContextWriter : IContextWriter
    {
        private readonly StreamWriter _writer;
        public MdContextWriter(StreamWriter writer) { _writer = writer; }
        public Task WriteSymbolAsync(ISymbol symbol, string kind, string definition, string signature, string codeSnippet)
        {
            _writer.WriteLine($"## {symbol.ToDisplayString()}");
            _writer.WriteLine($"**Kind:** {kind}  ");
            _writer.WriteLine($"**Definition:** {definition}  ");
            _writer.WriteLine($"**Signature:** `{signature}`  ");
            if (!string.IsNullOrEmpty(codeSnippet))
            {
                _writer.WriteLine();
                _writer.WriteLine("```csharp");
                _writer.WriteLine(codeSnippet);
                _writer.WriteLine("```");
            }
            _writer.WriteLine();
            _writer.WriteLine("**References:**");
            return Task.CompletedTask;
        }
        public Task WriteReferenceAsync(string refPath, int refLine, string codeSnippet)
        {
            _writer.Write($"- `{refPath}:{refLine}`");
            if (!string.IsNullOrEmpty(codeSnippet))
            {
                _writer.WriteLine();
                _writer.WriteLine("  ```csharp");
                _writer.WriteLine(codeSnippet);
                _writer.WriteLine("  ```");
            }
            else
            {
                _writer.WriteLine();
            }
            return Task.CompletedTask;
        }
        public Task NextSymbolAsync()
        {
            _writer.WriteLine();
            return Task.CompletedTask;
        }
        public Task CompleteAsync() => Task.CompletedTask;
        public void Dispose() => _writer.Dispose();
    }
}
