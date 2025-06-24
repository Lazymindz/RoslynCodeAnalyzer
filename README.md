# RoslynCodeAnalyzer

A C# console tool for analyzing .NET solutions and projects using Roslyn. Outputs a code context file (symbol definitions and references) and a detailed log file.

---

## Features

- **Modern CLI**: Supports `--help`, `--input`/`-i`, `--output`/`-o`.
- **Separation of Concerns**: Analysis output (context) and operational log are written to separate files.
- **Relative Paths**: All file paths in the analysis output are relative to the solution/project location.
- **Robustness**: Clear error handling, exit codes, and user-friendly messages.
- **Extensible**: Easily adaptable for future enhancements.

---

## Usage

### Command Line

```sh
RoslynCodeAnalyzer --input <path-to-solution.sln|project.csproj> [--output <output-directory>]
RoslynCodeAnalyzer -i <path-to-solution.sln> -o <output-directory>
RoslynCodeAnalyzer --help
```

### Options

| Option            | Alias | Description                                                      | Required | Default                |
|-------------------|-------|------------------------------------------------------------------|----------|------------------------|
| --input           | -i    | Path to the .sln or .csproj file to analyze                      | Yes      | -                      |
| --output          | -o    | Directory to write output files                                  | No       | Current directory      |
| --help            |       | Show help and usage information                                  | No       | -                      |

---

## Output Files

- **Code Context File**: `<SolutionName>_codecontext.log`
  - Contains symbol definitions and references (no timestamps, no progress logs).
  - All file paths are relative to the solution/project file.

- **Log File**: `<SolutionName>_analyzer.log`
  - Contains operational logs, progress, errors, and timestamps.

Both files are written to the specified output directory (or current directory by default).

> **Note:** All file paths and examples in this documentation are placeholders and do not reference any real user or system data.

---

## Example

```sh
RoslynCodeAnalyzer --input MySolution.sln --output ./analysis/
```

This will produce:
- `./analysis/MySolution_codecontext.log`
- `./analysis/MySolution_analyzer.log`

---

## Output Format

**Code Context File Example:**
```
SYMBOL: MyNamespace.MyClass
Kind: Class
Definition: src/MyClass.cs:10
Signature: MyClass
References:
  - src/OtherFile.cs:25
  - src/AnotherFile.cs:42

SYMBOL: MyNamespace.MyClass.MyMethod()
Kind: Method
Definition: src/MyClass.cs:25
Signature: MyMethod()
References:
  (No references found in the solution.)
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
```

---

## Best Practices

- Always use relative paths in output for portability.
- Separate analysis results from operational logs for clarity.
- Use robust argument parsing and provide clear help messages.
- Return appropriate exit codes for success and error conditions.
- Validate all user inputs and handle exceptions gracefully.

---

## Building

Requires [.NET 9.0 SDK](https://dotnet.microsoft.com/).

```sh
dotnet build
```

---

## License

MIT License
