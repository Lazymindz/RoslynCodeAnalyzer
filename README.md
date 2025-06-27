# RoslynCodeAnalyzer

A C# console tool for analyzing .NET solutions and projects using Roslyn. Outputs a code context file (symbol definitions and references, with configurable detail and format) and a detailed log file.

---

## Features

- **Modern CLI**: Supports `--help`, `--input`/`-i`, `--output`/`-o`, `--symbol`/`-s`, `--relation-level`/`-r`, `--snippet-level`/`-n`, `--output-format`/`-f`.
- **Configurable Analysis**: Filter by symbol, control relationship and snippet detail, and choose output format.
- **Separation of Concerns**: Analysis output (context) and operational log are written to separate files.
- **Relative Paths**: All file paths in the analysis output are relative to the solution/project location.
- **Robustness**: Clear error handling, exit codes, and user-friendly messages.
- **Extensible**: Easily adaptable for future enhancements.

---

## Usage

### Command Line

```sh
RoslynCodeAnalyzer --input <path-to-solution.sln|project.csproj> [options]
RoslynCodeAnalyzer -i <path-to-solution.sln> -o <output-directory> -s <symbol-pattern> -r <relation-level> -n <snippet-level> -f <output-format>
RoslynCodeAnalyzer --help
```

### Options

| Option                | Alias | Description                                                                 | Required | Default                |
|-----------------------|-------|-----------------------------------------------------------------------------|----------|------------------------|
| --input               | -i    | Path to the .sln or .csproj file to analyze                                 | Yes      | -                      |
| --output              | -o    | Directory to write output files                                             | No       | Current directory      |
| --symbol              | -s    | Symbol name or pattern to filter (case-insensitive substring match)         | No       | (all symbols)          |
| --relation-level      | -r    | Relation level: direct, references, inheritance, all                        | No       | direct                 |
| --snippet-level       | -n    | Snippet level: none, line, block                                            | No       | none                   |
| --output-format       | -f    | Output format: txt, json, md                                                | No       | txt                    |
| --help                | -h/?  | Show help and usage information                                             | No       | -                      |
| --version             |       | Show version information                                                    | No       | -                      |

#### Option Details

- **--symbol / -s**: Only analyze symbols whose name contains the given pattern.
- **--relation-level / -r**:
  - `direct`: Only direct relationships (default).
  - `references`: Includes referenced and referencing symbols.
  - `inheritance`: Includes base/derived/interface relations.
  - `all`: Combines all above.
- **--snippet-level / -n**:
  - `none`: No code snippets (default).
  - `line`: Single line of code for each result.
  - `block`: Full code block for each result.
- **--output-format / -f**:
  - `txt`: Plain text (default).
  - `json`: Structured JSON.
  - `md`: Markdown.

---

## Output Files

- **Code Context File**: `<SolutionName>_codecontext.txt|json|md`
  - Contains symbol definitions and references, with detail and format controlled by CLI options.
  - All file paths are relative to the solution/project file.
  - Format and content depend on `--output-format` and `--snippet-level`.

- **Log File**: `<SolutionName>_analyzer.log`
  - Contains operational logs, progress, errors, and timestamps.

Both files are written to the specified output directory (or current directory by default).

---

## Examples

```sh
# Basic usage (default output format and detail)
RoslynCodeAnalyzer --input MySolution.sln

# Specify output directory and filter by symbol
RoslynCodeAnalyzer -i MySolution.sln -o ./analysis -s MyClass

# Include all relationships, output as Markdown with code blocks
RoslynCodeAnalyzer -i MySolution.sln -r all -n block -f md
```

This will produce files like:
- `./analysis/MySolution_codecontext.txt` (or `.json`, `.md` depending on options)
- `./analysis/MySolution_analyzer.log`

---

## Output Format

**Code Context File Example (txt, snippet-level: line):**
```
SYMBOL: MyNamespace.MyClass
Kind: Class
Definition: src/MyClass.cs:10
Signature: MyClass
Code Snippet:
--------------------------------------------------
public class MyClass
--------------------------------------------------
References:
  - src/OtherFile.cs:25
    Code Snippet:
    --------------------------------------------------
var obj = new MyClass();
    --------------------------------------------------
  - src/AnotherFile.cs:42
    Code Snippet:
    --------------------------------------------------
return new MyClass();
    --------------------------------------------------

SYMBOL: MyNamespace.MyClass.MyMethod()
Kind: Method
Definition: src/MyClass.cs:25
Signature: MyMethod()
References:
  (No references found in the solution.)
```

**Code Context File Example (json, snippet-level: none):**
```json
[
  {
    "symbol": "MyNamespace.MyClass",
    "kind": "Class",
    "definition": "src/MyClass.cs:10",
    "signature": "MyClass",
    "codeSnippet": null,
    "references": [
      { "path": "src/OtherFile.cs", "line": 25, "codeSnippet": null },
      { "path": "src/AnotherFile.cs", "line": 42, "codeSnippet": null }
    ]
  }
]
```

**Code Context File Example (md, snippet-level: block):**
```markdown
## MyNamespace.MyClass
**Kind:** Class  
**Definition:** src/MyClass.cs:10  
**Signature:** `MyClass`  

```csharp
public class MyClass
{
    // ...
}
```

**References:**
- `src/OtherFile.cs:25`
  ```csharp
  var obj = new MyClass();
  ```
- `src/AnotherFile.cs:42`
  ```csharp
  return new MyClass();
  ```

## MyNamespace.MyClass.MyMethod()
**Kind:** Method  
**Definition:** src/MyClass.cs:25  
**Signature:** `MyMethod()`  

```csharp
public void MyMethod() { ... }
```

**References:**
- `(No references found in the solution.)`
```

**Log File Example:**
```
2025-06-24 12:15:33 RoslynCodeAnalyzer started.
2025-06-24 12:15:33 Input: /path/to/MySolution.sln
2025-06-24 12:15:33 Output directory: /path/to/analysis
2025-06-24 12:15:33 Loading workspace... This might take a moment.
2025-06-24 12:15:36 Workspace loaded. Finding symbols and references...
2025-06-24 12:15:44 Found 16387 total symbols to analyze.
...

---

## Best Practices

- Use relative paths in output for portability.
- Separate analysis results from operational logs for clarity.
- Use robust argument parsing and provide clear help messages.
- Return appropriate exit codes for success and error conditions.
- Validate all user inputs and handle exceptions gracefully.

---

## Integrating with AI Coding Agents

RoslynCodeAnalyzer can be seamlessly integrated with AI coding agents such as **Cline**, **Cursor**, **Claude Code**, **Gemini CLI**, and others that support shell command execution and file parsing. This enables advanced code navigation, refactoring, and context-aware code actions on large C# codebases.

### How AI Agents Leverage RoslynCodeAnalyzer

- **Shell Invocation**: AI agents issue a shell command to run RoslynCodeAnalyzer on your solution or project.
- **Machine-Readable Output**: The tool produces output in JSON, Markdown, or plain text, which agents can parse for symbol definitions, references, and code snippets.
- **Automated Code Actions**: Agents use the parsed context to suggest, automate, or apply code changes, refactorings, or navigation actions—especially valuable for large codebases.

### Example Integration Workflow

1. **Agent issues shell command** (e.g., via terminal or subprocess):
    ```sh
    RoslynCodeAnalyzer -i MySolution.sln -o ./analysis -f json -n line
    ```
2. **Agent parses the output** (e.g., in Python, TypeScript, or directly in the agent's runtime):
    ```python
    import json
    with open("./analysis/MySolution_codecontext.json") as f:
        context = json.load(f)
    # Use 'context' for code search, navigation, or refactoring
    ```
3. **Agent suggests or applies code actions** based on the analysis.

#### Example: Using with Cline, Cursor, Claude Code, Gemini CLI

- **Cline**: Issues the CLI command, reads the JSON/Markdown output, and uses it for code search, navigation, or automated refactoring.
- **Cursor**: Integrates the output for in-editor navigation, symbol search, or context-aware code actions.
- **Claude Code / Gemini CLI**: Consumes Markdown/JSON output to provide context for code suggestions, explanations, or automated changes.

### Tips for Large C# Codebases

- Use the `-s`/`--symbol` option to filter analysis to specific symbols or patterns.
- Adjust `--relation-level` and `--snippet-level` to control output size and detail.
- Prefer `-f json` for programmatic consumption by AI agents.
- For very large solutions, process output in manageable chunks or filter by project/module.

### Workflow Diagram

```mermaid
graph TD
  A[AI Agent] -->|Run CLI| B[RoslynCodeAnalyzer]
  B -->|Output (JSON/MD)| C[AI Agent]
  C -->|Parse & Analyze| D[Suggest/Apply Code Actions]
  D -->|Repeat as needed| A
```

---

## Building

Requires [.NET 9.0 SDK](https://dotnet.microsoft.com/).

```sh
dotnet build
```

---

## License

MIT License
