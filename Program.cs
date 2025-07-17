// Program.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using System.CommandLine;
using System.CommandLine.Invocation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

// =================================================================================
// 1. MAIN PROGRAM & CLI SETUP
// =================================================================================

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // --- CLI Options ---
        var targetSymbolOption = new Option<string>(
            new[] { "--target-symbol", "-s" },
            "Analyze a specific symbol using its full, exact name (e.g., 'MyNamespace.MyClass.MyMethod(...)'). Best for scripts."
        ) { ArgumentHelpName = "exact-symbol-name" };

        var targetMethodOption = new Option<string>(
            new[] { "--method", "-m" },
            "Analyze a method by its short name (e.g., 'MyMethod'). If the name is ambiguous, you will be prompted to choose."
        ) { ArgumentHelpName = "short-method-name" };

        var analysisModeOption = new Option<AnalysisMode>(
            new[] { "--analysis-mode", "-a" }, () => AnalysisMode.Full, "Analysis mode: 'References', 'Analyze', or 'Full'."
        ) { ArgumentHelpName = "mode" };

        var depthOption = new Option<int>(
            new[] { "--depth", "-d" }, () => 0, "Recursion depth for call chain analysis."
        ) { ArgumentHelpName = "level" };

        var outputOption = new Option<DirectoryInfo>(
            new[] { "--output", "-o" }, () => new DirectoryInfo(Directory.GetCurrentDirectory()), "Directory to write output files."
        ) { ArgumentHelpName = "directory" };

        var outputFormatOption = new Option<string>(
            new[] { "--output-format", "-f" }, () => "json", "Output format: json, md, txt."
        ) { ArgumentHelpName = "format" };
        
        var rootCommand = new RootCommand("A C# static analysis tool for deep code exploration.")
        {
            targetSymbolOption, targetMethodOption, analysisModeOption, depthOption, outputOption, outputFormatOption
        };

        // Ensure user provides one of the target options, but not both.
        rootCommand.AddValidator(result =>
        {
            if (result.FindResultFor(targetSymbolOption) != null && result.FindResultFor(targetMethodOption) != null)
            {
                result.ErrorMessage = "The options '--target-symbol' and '--method' cannot be used at the same time.";
            }
            if (result.FindResultFor(targetSymbolOption) == null && result.FindResultFor(targetMethodOption) == null)
            {
                result.ErrorMessage = "You must provide a target to analyze using either '--target-symbol' or '--method'.";
            }
        });

        rootCommand.SetHandler(async (context) =>
        {
            var parseResult = context.ParseResult;
            var options = new AnalysisOptions
            {
                TargetSymbol = parseResult.GetValueForOption(targetSymbolOption),
                TargetMethod = parseResult.GetValueForOption(targetMethodOption),
                Mode = parseResult.GetValueForOption(analysisModeOption),
                Depth = parseResult.GetValueForOption(depthOption),
                Output = parseResult.GetValueForOption(outputOption)!,
                OutputFormat = parseResult.GetValueForOption(outputFormatOption)!,
                InputSolution = new FileInfo(GetSlnOrCsprojPath())
            };
            context.ExitCode = await RunAnalysis(options);
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task<int> RunAnalysis(AnalysisOptions options)
    {
        StreamWriter? logWriter = null;
        try
        {
            if (!options.InputSolution.Exists)
            {
                Console.Error.WriteLine($"ERROR: Could not find solution or project file.");
                return 1;
            }
            options.Output.Create();

            string baseName = Path.GetFileNameWithoutExtension(options.InputSolution.Name);
            logWriter = new StreamWriter(Path.Combine(options.Output.FullName, $"{baseName}_analyzer.log"), false);
            Action<string> log = message =>
            {
                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
                Console.WriteLine(logMessage);
                logWriter?.WriteLine(logMessage);
                logWriter?.Flush();
            };

            log("Analysis engine starting.");
            log($"Solution/Project: {options.InputSolution.FullName}");
            log($"Analysis Mode: {options.Mode}");
            log($"Recursion Depth: {options.Depth}");

            var engine = new AnalysisEngine(options.InputSolution.FullName, log);
            SymbolAnalysisResult? analysisResult;

            if (!string.IsNullOrEmpty(options.TargetMethod))
            {
                log($"Starting analysis by SHORT NAME: '{options.TargetMethod}'");
                analysisResult = await engine.AnalyzeByShortNameAsync(options.TargetMethod, options.Mode, options.Depth);
            }
            else
            {
                log($"Starting analysis by EXACT NAME: '{options.TargetSymbol}'");
                analysisResult = await engine.AnalyzeByExactNameAsync(options.TargetSymbol!, options.Mode, options.Depth);
            }
            
            if (analysisResult == null)
            {
                log($"Analysis could not be completed for the specified target. See previous errors.");
                return 1;
            }

            log("Top-level analysis complete. Writing output...");
            await WriteOutput(options.Output, baseName, options.OutputFormat, analysisResult, log);
            log("Process finished successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL ERROR: {ex.Message}");
            logWriter?.WriteLine($"FATAL ERROR: {ex.ToString()}");
            return 2;
        }
        finally
        {
            logWriter?.Dispose();
        }
    }
    
    private static async Task WriteOutput(DirectoryInfo outputDir, string baseName, string format, SymbolAnalysisResult result, Action<string> log)
    {
        string ext = format.ToLowerInvariant();
        string outFilePath = Path.Combine(outputDir.FullName, $"{baseName}_analysis.{ext}");
        log($"Writing output to: {outFilePath}");

        using var writer = new StreamWriter(outFilePath, false);
        IOutputWriter outputWriter = ext switch
        {
            "md" => new MdOutputWriter(writer),
            "txt" => new TxtOutputWriter(writer),
            _ => new JsonOutputWriter(writer)
        };
        await outputWriter.WriteResultAsync(result);
        log("Output writing complete.");
    }
    
    private static string GetSlnOrCsprojPath()
    {
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null)
        {
            var file = currentDir.GetFiles("*.sln").FirstOrDefault() ?? currentDir.GetFiles("*.csproj").FirstOrDefault();
            if (file != null) return file.FullName;
            currentDir = currentDir.Parent;
        }
        return string.Empty;
    }

    private class AnalysisOptions
    {
        public string? TargetSymbol { get; set; }
        public string? TargetMethod { get; set; }
        public AnalysisMode Mode { get; set; }
        public int Depth { get; set; }
        public DirectoryInfo Output { get; set; } = null!;
        public string OutputFormat { get; set; } = "json";
        public FileInfo InputSolution { get; set; } = null!;
    }
}

public enum AnalysisMode { Full, References, Analyze }

// =================================================================================
// 2. CORE ANALYSIS ENGINE
// =================================================================================

public class AnalysisEngine
{
    private readonly string _solutionPath;
    private readonly Action<string> _log;
    private Solution _solution = null!;
    private string _solutionDir = "";

    public AnalysisEngine(string solutionPath, Action<string> logger)
    {
        _solutionPath = solutionPath;
        _log = logger;
    }
    
    private async Task LoadWorkspaceAsync()
    {
        if (_solution != null) return;
        _log("TRACE: Loading workspace...");
        using var workspace = MSBuildWorkspace.Create();
        _solution = Path.GetExtension(_solutionPath).Equals(".sln", StringComparison.OrdinalIgnoreCase)
            ? await workspace.OpenSolutionAsync(_solutionPath)
            : (await workspace.OpenProjectAsync(_solutionPath)).Solution;
        _solutionDir = Path.GetDirectoryName(_solutionPath)!;
        _log("TRACE: Workspace loaded successfully.");
    }

    public async Task<SymbolAnalysisResult?> AnalyzeByShortNameAsync(string shortName, AnalysisMode mode, int maxDepth)
    {
        await LoadWorkspaceAsync();
        _log($"TRACE: Searching for all methods with short name '{shortName}'...");
        var candidateSymbols = await FindSymbolsByShortNameAsync(shortName);
        _log($"TRACE: Found {candidateSymbols.Count} candidate(s).");

        if (!candidateSymbols.Any())
        {
            _log($"ERROR: No method with the short name '{shortName}' could be found in the solution.");
            return null;
        }

        ISymbol targetSymbol;
        if (candidateSymbols.Count == 1)
        {
            targetSymbol = candidateSymbols[0];
            _log($"TRACE: Found unique match. Proceeding with analysis for: {targetSymbol.ToDisplayString()}");
        }
        else
        {
            if (!Environment.UserInteractive)
            {
                _log($"ERROR: Ambiguous method name '{shortName}' found in a non-interactive environment.");
                _log("Please use the --target-symbol option with one of the following exact names:");
                candidateSymbols.ForEach(s => _log($"- {s.ToDisplayString()}"));
                return null;
            }
            targetSymbol = PromptForSymbolSelection(candidateSymbols);
            _log($"TRACE: User selected: {targetSymbol.ToDisplayString()}");
        }
        
        return await AnalyzeSymbolRecursivelyAsync(targetSymbol, mode, 0, maxDepth, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
    }

    public async Task<SymbolAnalysisResult?> AnalyzeByExactNameAsync(string exactName, AnalysisMode mode, int maxDepth)
    {
        await LoadWorkspaceAsync();
        _log($"TRACE: Searching for symbol with exact name '{exactName}'...");
        var symbol = await FindSymbolByExactNameAsync(exactName);
        if (symbol == null)
        {
             _log($"ERROR: Could not find a symbol with the exact name '{exactName}'.");
            return null;
        }
        _log($"TRACE: Found exact match. Proceeding with analysis for: {symbol.ToDisplayString()}");
        return await AnalyzeSymbolRecursivelyAsync(symbol, mode, 0, maxDepth, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
    }

    private ISymbol PromptForSymbolSelection(List<ISymbol> symbols)
    {
        _log("ACTION: Ambiguous method name found. Prompting user for selection.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\nAmbiguous method name found. Please choose the correct one:");
        for (int i = 0; i < symbols.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {symbols[i].ToDisplayString()}");
        }
        Console.ResetColor();

        while (true)
        {
            Console.Write("Enter number and press ENTER: ");
            string? input = Console.ReadLine();
            if (int.TryParse(input, out int choice) && choice > 0 && choice <= symbols.Count)
            {
                return symbols[choice - 1];
            }
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid selection. Please try again.");
            Console.ResetColor();
        }
    }
    
    private async Task<SymbolAnalysisResult?> AnalyzeSymbolRecursivelyAsync(ISymbol symbol, AnalysisMode mode, int currentDepth, int maxDepth, HashSet<ISymbol> visited)
    {
        if (currentDepth > maxDepth)
        {
            _log($"TRACE: Reached max depth ({maxDepth}). Stopping recursion at '{symbol.ToDisplayString()}'.");
            return null;
        }
        
        if (!visited.Add(symbol))
        {
            _log($"TRACE: Already visited '{symbol.ToDisplayString()}'. Skipping to prevent infinite recursion.");
            return null;
        }

        var location = symbol.Locations.FirstOrDefault();
        var result = new SymbolAnalysisResult
        {
            SymbolName = symbol.ToDisplayString(),
            SymbolKind = symbol.Kind.ToString(),
            DefinitionLocation = GetRelativePath(location?.SourceTree?.FilePath, location?.GetLineSpan().StartLinePosition.Line + 1)
        };

        _log($"-- Analyzing '{result.SymbolName}' at depth {currentDepth} --");

        // Outward Analysis
        if (mode is AnalysisMode.Full or AnalysisMode.References)
        {
            _log($"TRACE: Finding references for '{result.SymbolName}'...");
            var references = await SymbolFinder.FindReferencesAsync(symbol, _solution);
            foreach (var refSymbol in references)
                foreach (var loc in refSymbol.Locations)
                    result.References.Add(new ReferenceInfo { FilePath = GetRelativePath(loc.Document.FilePath, loc.Location.GetLineSpan().StartLinePosition.Line + 1) });
        }
        
        // Inward Analysis
        IEnumerable<ISymbol> calledMethodSymbols = Enumerable.Empty<ISymbol>();
        if (symbol is IMethodSymbol && mode is AnalysisMode.Full or AnalysisMode.Analyze)
        {
            _log($"TRACE: Performing inward analysis for '{result.SymbolName}'...");
            if (location != null && location.IsInSource)
            {
                var document = _solution.GetDocument(location.SourceTree);
                var methodDeclaration = (await location.SourceTree.GetRootAsync()).FindNode(location.SourceSpan) as MethodDeclarationSyntax;
                if (document != null && methodDeclaration != null)
                {
                    var semanticModel = await document.GetSemanticModelAsync();
                    if (semanticModel != null)
                    {
                        var walker = new PerformanceSyntaxWalker(semanticModel);
                        walker.Visit(methodDeclaration.Body ?? (SyntaxNode?)methodDeclaration.ExpressionBody);
                        result.InternalAnalysis = walker.GetResult();
                        calledMethodSymbols = walker.CalledMethodSymbols;
                        _log($"TRACE: Inward analysis found {calledMethodSymbols.Count()} method calls to trace.");
                    }
                }
            } else {
                _log($"TRACE: Skipping inward analysis for '{result.SymbolName}' because it has no source location.");
            }
        }

        // Recursive Step
        _log($"TRACE: Checking for sub-methods to analyze from '{result.SymbolName}'...");
        foreach (var calledSymbol in calledMethodSymbols)
        {
            var childResult = await AnalyzeSymbolRecursivelyAsync(calledSymbol, mode, currentDepth + 1, maxDepth, visited);
            if (childResult != null) result.CalledSymbols.Add(childResult);
        }
        
        return result;
    }

    private async Task<List<ISymbol>> FindSymbolsByShortNameAsync(string shortName)
    {
        var symbols = new List<ISymbol>();
        foreach (var project in _solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                var methods = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Identifier.ValueText.Equals(shortName, StringComparison.Ordinal));
                
                foreach (var method in methods)
                {
                    var symbol = model.GetDeclaredSymbol(method);
                    if (symbol != null) symbols.Add(symbol);
                }
            }
        }
        return symbols.Distinct(SymbolEqualityComparer.Default).ToList();
    }

    private async Task<ISymbol?> FindSymbolByExactNameAsync(string fullyQualifiedName)
    {
        foreach (var project in _solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;
            var symbols = SymbolFinder.FindDeclarationsAsync(project, fullyQualifiedName, false, SymbolFilter.All).Result;
            var symbol = symbols.FirstOrDefault(s => s.ToDisplayString().Equals(fullyQualifiedName, StringComparison.Ordinal));
            if (symbol != null) return symbol;
        }
        return null;
    }

    private string GetRelativePath(string? fullPath, int? line)
    {
        if (string.IsNullOrEmpty(fullPath)) return "Unknown location (likely from a compiled assembly)";
        string relativePath = fullPath.StartsWith(_solutionDir) ? Path.GetRelativePath(_solutionDir, fullPath) : fullPath;
        return line.HasValue ? $"{relativePath}:{line.Value}" : relativePath;
    }
}


// =================================================================================
// 3. DATA MODELS FOR HIERARCHICAL OUTPUT
// =================================================================================
public class SymbolAnalysisResult
{
    public string SymbolName { get; set; } = "";
    public string SymbolKind { get; set; } = "";
    public string DefinitionLocation { get; set; } = "";
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ReferenceInfo> References { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InternalAnalysisResult? InternalAnalysis { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<SymbolAnalysisResult> CalledSymbols { get; set; } = new();
}

public class ReferenceInfo
{
    public string FilePath { get; set; } = "";
}

public class InternalAnalysisResult
{
    public MethodMetrics Metrics { get; set; } = new();
    public List<DataAccessCall> DataAccessCalls { get; set; } = new();
    public List<LoopInfo> Loops { get; set; } = new();
    public List<CodeSmell> CodeSmells { get; set; } = new();
}

public class MethodMetrics
{
    public int LinesOfCode { get; set; }
    public int CyclomaticComplexity { get; set; }
    public int ParameterCount { get; set; }
}

public class DataAccessCall
{
    public string AccessType { get; set; } = "Unknown";
    public string Statement { get; set; } = "";
    public int LineNumber { get; set; }
    public bool IsInsideLoop { get; set; }
}

public class LoopInfo
{
    public string LoopType { get; set; } = "";
    public int NestingLevel { get; set; }
    public int LineNumber { get; set; }
}

public class CodeSmell
{
    public string SmellType { get; set; } = "";
    public string Description { get; set; } = "";
    public int LineNumber { get; set; }
}


// =================================================================================
// 4. PERFORMANCE SYNTAX WALKER (INWARD ANALYSIS)
// =================================================================================
public class PerformanceSyntaxWalker : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    private int _loopNestingLevel = 0;
    private readonly InternalAnalysisResult _result = new();
    public List<ISymbol> CalledMethodSymbols { get; } = new();
    
    private static readonly HashSet<string> DataAccessKeywords = new(StringComparer.OrdinalIgnoreCase)
    { "SqlCommand", "SqlConnection", "ExecuteReader", "ExecuteNonQuery", "SqlDataAdapter", "DbContext", "SaveChanges", "FromSqlRaw", "DataTable", "DataSet", "Select" };

    public PerformanceSyntaxWalker(SemanticModel semanticModel) : base(SyntaxWalkerDepth.Node)
    {
        _semanticModel = semanticModel;
        _result.Metrics.CyclomaticComplexity = 1; // Start with 1 for the method itself
    }
    
    public InternalAnalysisResult GetResult() => _result;

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var span = node.GetLocation().GetLineSpan();
        _result.Metrics.LinesOfCode = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        _result.Metrics.ParameterCount = node.ParameterList.Parameters.Count;
        base.VisitMethodDeclaration(node);
    }
    
    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var symbolInfo = _semanticModel.GetSymbolInfo(node);
        if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
        {
            CalledMethodSymbols.Add(methodSymbol);
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            
            if (DataAccessKeywords.Any(k => methodSymbol.ToDisplayString().Contains(k)))
            {
                 _result.DataAccessCalls.Add(new DataAccessCall { AccessType = "ADO/EF", Statement = node.Expression.ToString(), LineNumber = line, IsInsideLoop = _loopNestingLevel > 0 });
            }
        }
        base.VisitInvocationExpression(node);
    }
    
    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        if (_semanticModel.GetTypeInfo(node).Type?.Name.Contains("DataTable") == true)
        {
             _result.CodeSmells.Add(new CodeSmell { SmellType = "DataTable Usage", Description = $"Instantiation of DataTable.", LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1 });
        }
        base.VisitObjectCreationExpression(node);
    }

    public override void VisitForStatement(ForStatementSyntax node) { HandleLoop(node, "for", () => base.VisitForStatement(node)); }
    public override void VisitForEachStatement(ForEachStatementSyntax node) { HandleLoop(node, "foreach", () => base.VisitForEachStatement(node)); }
    public override void VisitWhileStatement(WhileStatementSyntax node) { HandleLoop(node, "while", () => base.VisitWhileStatement(node)); }

    private void HandleLoop(SyntaxNode node, string type, Action visitBase)
    {
        _result.Metrics.CyclomaticComplexity++;
        _loopNestingLevel++;
        _result.Loops.Add(new LoopInfo { LoopType = type, NestingLevel = _loopNestingLevel, LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1 });
        visitBase();
        _loopNestingLevel--;
    }

    public override void VisitIfStatement(IfStatementSyntax node) { _result.Metrics.CyclomaticComplexity++; base.VisitIfStatement(node); }
    public override void VisitConditionalExpression(ConditionalExpressionSyntax node) { _result.Metrics.CyclomaticComplexity++; base.VisitConditionalExpression(node); }
    public override void VisitSwitchSection(SwitchSectionSyntax node) { if (node.Labels.Any(l => l.Kind() == SyntaxKind.CaseSwitchLabel)) { _result.Metrics.CyclomaticComplexity++; } base.VisitSwitchSection(node); }
    public override void VisitBinaryExpression(BinaryExpressionSyntax node) { if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression)) { _result.Metrics.CyclomaticComplexity++; } base.VisitBinaryExpression(node); }
}

// =================================================================================
// 5. OUTPUT WRITERS
// =================================================================================

public interface IOutputWriter
{
    Task WriteResultAsync(SymbolAnalysisResult result);
}

public class JsonOutputWriter : IOutputWriter
{
    private readonly StreamWriter _writer;
    public JsonOutputWriter(StreamWriter writer) => _writer = writer;
    public async Task WriteResultAsync(SymbolAnalysisResult result)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
        var json = JsonSerializer.Serialize(result, options);
        await _writer.WriteLineAsync(json);
    }
}

public abstract class HierarchicalTextOutputWriter : IOutputWriter
{
    protected readonly StreamWriter _writer;
    protected HierarchicalTextOutputWriter(StreamWriter writer) => _writer = writer;
    public abstract Task WriteResultAsync(SymbolAnalysisResult result);
    protected abstract void WriteNode(SymbolAnalysisResult node, string indent);
}

public class TxtOutputWriter : HierarchicalTextOutputWriter
{
    public TxtOutputWriter(StreamWriter writer) : base(writer) { }
    public override Task WriteResultAsync(SymbolAnalysisResult result)
    {
        WriteNode(result, "");
        return Task.CompletedTask;
    }

    protected override void WriteNode(SymbolAnalysisResult node, string indent)
    {
        _writer.WriteLine($"{indent}SYMBOL: {node.SymbolName} ({node.SymbolKind})");
        _writer.WriteLine($"{indent}  Defined At: {node.DefinitionLocation}");

        if (node.References.Any())
        {
            _writer.WriteLine($"{indent}  References ({node.References.Count}):");
            node.References.Take(5).ToList().ForEach(r => _writer.WriteLine($"{indent}    - {r.FilePath}"));
            if (node.References.Count > 5) _writer.WriteLine($"{indent}    ...and {node.References.Count - 5} more.");
        }
        
        if (node.InternalAnalysis != null)
        {
            var analysis = node.InternalAnalysis;
            _writer.WriteLine($"{indent}  Internal Analysis:");
            _writer.WriteLine($"{indent}    Metrics: LOC={analysis.Metrics.LinesOfCode}, Complexity={analysis.Metrics.CyclomaticComplexity}");
            analysis.Loops.ForEach(l => _writer.WriteLine($"{indent}    Loop: {l.LoopType} (depth {l.NestingLevel}) at line {l.LineNumber}"));
            analysis.DataAccessCalls.ForEach(d => _writer.WriteLine($"{indent}    DB Call: {d.Statement} at line {d.LineNumber} {(d.IsInsideLoop ? "[IN LOOP]" : "")}"));
            analysis.CodeSmells.ForEach(s => _writer.WriteLine($"{indent}    Smell: {s.SmellType} at line {s.LineNumber}"));
        }

        if (node.CalledSymbols.Any())
        {
            _writer.WriteLine($"{indent}  Calls To:");
            foreach (var child in node.CalledSymbols)
            {
                WriteNode(child, indent + "    ");
            }
        }
        _writer.WriteLine();
    }
}

public class MdOutputWriter : HierarchicalTextOutputWriter
{
    public MdOutputWriter(StreamWriter writer) : base(writer) { }
    public override async Task WriteResultAsync(SymbolAnalysisResult result)
    {
        await _writer.WriteLineAsync("# Code Analysis Report");
        WriteNode(result, "");
    }

    protected override void WriteNode(SymbolAnalysisResult node, string indent)
    {
        string header = new string('#', indent.Length / 2 + 2);
        _writer.WriteLine($"\n{header} {node.SymbolKind}: `{node.SymbolName}`");
        _writer.WriteLine($"- **Defined At:** `{node.DefinitionLocation}`");

        if (node.References.Any())
        {
            _writer.WriteLine("- **References:**");
            foreach (var r in node.References) _writer.WriteLine($"  - `{r.FilePath}`");
        }
        
        if (node.InternalAnalysis != null)
        {
            var analysis = node.InternalAnalysis;
            _writer.WriteLine("- **Internal Analysis:**");
            _writer.WriteLine($"  - **Metrics:** LinesOfCode=`{analysis.Metrics.LinesOfCode}`, CyclomaticComplexity=`{analysis.Metrics.CyclomaticComplexity}`");
            foreach (var d in analysis.DataAccessCalls) _writer.WriteLine($"  - **DB Call:** `{d.Statement}` at line {d.LineNumber} {(d.IsInsideLoop ? "**(IN LOOP)**" : "")}");
            foreach (var l in analysis.Loops) _writer.WriteLine($"  - **Loop:** A `{l.LoopType}` loop with nesting level `{l.NestingLevel}` at line {l.LineNumber}");
        }

        if (node.CalledSymbols.Any())
        {
            _writer.WriteLine("- **Calls To:**");
            // Markdown doesn't have arbitrary indent, but we can use nested blockquotes
            // For simplicity, we just increase the header level
            foreach (var child in node.CalledSymbols)
            {
                WriteNode(child, indent + "  ");
            }
        }
    }
}