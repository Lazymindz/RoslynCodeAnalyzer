# RoslynCodeAnalyzer

**RoslynCodeAnalyzer** is a C# console tool for deep, configurable static analysis of .NET solutions and projects. Leveraging the Roslyn compiler platform, it outputs a comprehensive, hierarchical code analysis—detailing symbol definitions, references, call chains, and code metrics—alongside a detailed operational log. The tool is designed for both developers and AI coding agents, supporting advanced code navigation, refactoring, and context extraction in large C# codebases.

---

## Features

- **Modern CLI**: Analyze by exact symbol name or method short name with options for analysis mode, recursion depth, output directory, and format.
- **No Input Required**: Automatically detects the nearest `.sln` or `.csproj` file in the current or parent directories.
- **Configurable Analysis**:
  - Analyze by exact symbol (`--target-symbol`) or by method name (`--method`).
  - Select analysis mode: `Full`, `References`, or `Analyze`.
  - Control recursion depth for call chain exploration.
- **Multiple Output Formats**: JSON, Markdown, or plain text.
- **Separation of Concerns**: Analysis results and operational logs are written to separate files.
- **Relative Paths**: All file paths in the output are relative to the solution/project location.
- **Robust Error Handling**: Clear error messages, exit codes, and user-friendly CLI validation.
- **AI Agent Ready**: Output is machine-readable and suitable for integration with AI coding agents.

---

## Usage

### Command Line

```sh
RoslynCodeAnalyzer --target-symbol <exact-symbol-name> [options]
RoslynCodeAnalyzer --method <short-method-name> [options]
RoslynCodeAnalyzer -s <exact-symbol-name> -a Full -d 2 -o ./analysis -f json
RoslynCodeAnalyzer -m MyMethod -a Analyze -f md
RoslynCodeAnalyzer --help
```

**Note:** You must provide either `--target-symbol` or `--method`, but not both.

### Notes

- **Run Location**: The tool must be run from a directory within or above the target solution/project, as it searches upward for the nearest `.sln` or `.csproj` file.
- **Ambiguous Method Names**: If using `--method` and multiple matches are found, you will be prompted to select only in interactive mode. In non-interactive environments (e.g., CI), ambiguity will result in an error.

### Options

| Option                | Alias | Description                                                                                      | Required | Default                      |
|-----------------------|-------|--------------------------------------------------------------------------------------------------|----------|------------------------------|
| --target-symbol       | -s    | Analyze a specific symbol using its full, exact name (e.g., `MyNamespace.MyClass.MyMethod(...)`) | One of   | -                            |
| --method              | -m    | Analyze a method by its short name (e.g., `MyMethod`). Prompts if ambiguous.                     | One of   | -                            |
| --analysis-mode       | -a    | Analysis mode: `Full`, `References`, or `Analyze`                                                | No       | Full                         |
| --depth               | -d    | Recursion depth for call chain analysis                                                          | No       | 0 (no recursion)             |
| --output              | -o    | Directory to write output files                                                                  | No       | Current directory            |
| --output-format       | -f    | Output format: `json`, `md`, `txt`                                                               | No       | json                         |
| --help                |       | Show help and usage information                                                                  | No       | -                            |

#### Option Details

- **--target-symbol / -s**: Analyze a symbol by its fully qualified name (best for scripts and automation).
- **--method / -m**: Analyze by method short name. If ambiguous, prompts for selection (interactive mode only).
- **--analysis-mode / -a**:
  - `Full`: Analyze both references and inward calls (default).
  - `References`: Only find references to the symbol.
  - `Analyze`: Only analyze inward calls and metrics.
- **--depth / -d**: Maximum recursion depth for call chain analysis (0 = only the target symbol).
- **--output / -o**: Directory to write output files (created if it doesn't exist).
- **--output-format / -f**: Output format for the analysis file: `json`, `md`, or `txt`.

---

## Output Files

- **Analysis File**: `<SolutionName>_analysis.json|md|txt`
  - Contains hierarchical analysis of the target symbol, including references, call chains, metrics, and code smells.
  - Format and content depend on `--output-format`.
- **Log File**: `<SolutionName>_analyzer.log`
  - Contains operational logs, progress, errors, and timestamps.

Both files are written to the specified output directory (or current directory by default).

---

## Examples

```sh
# Analyze a method by its exact symbol name, output as JSON
RoslynCodeAnalyzer --target-symbol MyNamespace.MyClass.MyMethod(System.String) -f json

# Analyze by short method name, output as Markdown, with recursion depth 2
RoslynCodeAnalyzer -m ProcessOrder -a Full -d 2 -f md

# Output to a specific directory in plain text format
RoslynCodeAnalyzer -s MyNamespace.MyClass -o ./analysis -f txt
```

This will produce files like:
- `./analysis/MySolution_analysis.json` (or `.md`, `.txt` depending on options)
- `./analysis/MySolution_analyzer.log`

---

## Output Format

**Analysis File Example (json):**
```json
{
  "SymbolName": "MyNamespace.MyClass.MyMethod(System.String)",
  "SymbolKind": "Method",
  "DefinitionLocation": "src/MyClass.cs:25",
  "References": [
    { "FilePath": "src/OtherFile.cs:42" }
  ],
  "InternalAnalysis": {
    "Metrics": { "LinesOfCode": 12, "CyclomaticComplexity": 3, "ParameterCount": 1 },
    "DataAccessCalls": [
      { "AccessType": "ADO/EF", "Statement": "db.SaveChanges()", "LineNumber": 10, "IsInsideLoop": false }
    ],
    "Loops": [
      { "LoopType": "for", "NestingLevel": 1, "LineNumber": 8 }
    ],
    "CodeSmells": [
      { "SmellType": "DataTable Usage", "Description": "Instantiation of DataTable.", "LineNumber": 15 }
    ]
  },
  "CalledSymbols": [
    { /* ...recursive structure... */ }
  ]
}
```

**Analysis File Example (Markdown):**
```markdown
# Code Analysis Report

## Method: `MyNamespace.MyClass.MyMethod(System.String)`
- **Defined At:** `src/MyClass.cs:25`
- **References:**
  - `src/OtherFile.cs:42`
- **Internal Analysis:**
  - **Metrics:** LinesOfCode=`12`, CyclomaticComplexity=`3`
  - **DB Call:** `db.SaveChanges()` at line 10
  - **Loop:** A `for` loop with nesting level `1` at line 8
  - **Smell:** DataTable Usage at line 15
- **Calls To:**
  - (nested symbol analysis...)
```

**Analysis File Example (txt):**
```
SYMBOL: MyNamespace.MyClass.MyMethod(System.String) (Method)
  Defined At: src/MyClass.cs:25
  References (1):
    - src/OtherFile.cs:42
  Internal Analysis:
    Metrics: LOC=12, Complexity=3
    Loop: for (depth 1) at line 8
    DB Call: db.SaveChanges() at line 10 
    Smell: DataTable Usage at line 15
  Calls To:
    (nested symbol analysis...)
```

**Log File Example:**
```
2025-07-14 15:20:00 | Analysis engine starting.
2025-07-14 15:20:00 | Solution/Project: /path/to/MySolution.sln
2025-07-14 15:20:00 | Analysis Mode: Full
2025-07-14 15:20:00 | Recursion Depth: 2
...
2025-07-14 15:20:10 | Process finished successfully.
```

---

## Best Practices

- Use relative paths in output for portability.
- Separate analysis results from operational logs for clarity.
- Use robust argument parsing and provide clear help messages.
- Return appropriate exit codes for success and error conditions.
- Validate all user inputs and handle exceptions gracefully.

---

## Integrating with AI Coding Agents

RoslynCodeAnalyzer is designed for seamless integration with AI coding agents such as **Cline**, **Cursor**, **Claude Code**, **Gemini CLI**, and others that support shell command execution and file parsing. This enables advanced code navigation, refactoring, and context-aware code actions on large C# codebases.

### How AI Agents Leverage RoslynCodeAnalyzer

- **Shell Invocation**: AI agents issue a shell command to run RoslynCodeAnalyzer on your solution or project.
- **Machine-Readable Output**: The tool produces output in JSON, Markdown, or plain text, which agents can parse for symbol definitions, references, and code metrics.
- **Automated Code Actions**: Agents use the parsed context to suggest, automate, or apply code changes, refactorings, or navigation actions.

#### Example Integration Workflow

1. **Agent issues shell command**:
    ```sh
    RoslynCodeAnalyzer -s MyNamespace.MyClass.MyMethod(System.String) -f json -d 1
    ```
2. **Agent parses the output** (e.g., in Python, TypeScript, or directly in the agent's runtime):
    ```python
    import json
    with open("./analysis/MySolution_analysis.json") as f:
        context = json.load(f)
    # Use 'context' for code search, navigation, or refactoring
    ```
3. **Agent suggests or applies code actions** based on the analysis.

---

## Building

Requires [.NET 9.0 SDK](https://dotnet.microsoft.com/).

```sh
dotnet build
```

---

## License

MIT License
