# Vineflower Java Decompiler (vfd)

A zero-configuration CLI wrapper and Windows GUI helper for [Vineflower](https://github.com/Vineflower/vineflower), designed for rapid, high-readability Java deobfuscation and bytecode inspection.

## Features

- **High-Readability Output**: Configured with full AST resugaring enabled (generics, switch expressions, lambdas, try-with-resources, and LVT variable names).
- **Auto Target Output**: Automatically generates a `<name>-src/` folder alongside the original `.jar`.
- **Flexible Invocation**: Supports command-line arguments, multi-file batch jobs, drag-and-drop onto the binary, and an interactive file browser dialog.
- **Runtime Resolution**: Automatically checks for an embedded JRE in `./jre/`, `$JAVA_HOME`, or system `PATH`.

## Usage

### Command Line

```bash
# Decompile a single jar
vfd "path/to/archive.jar"

# Alternate alias
jdecompile "path/to/archive.jar"

# Batch decompile multiple files
vfd plugin1.jar plugin2.jar mod.jar
```

### Drag & Drop
Drag any `.jar` file directly onto `vfd.exe` or `jdecompile.exe` in Windows Explorer.

### Interactive Mode
Run `vfd.exe` with no arguments to enter a file path manually, or press **Enter** to open the Windows File Dialog.

## Output Structure

Decompiling `MyPlugin.jar` extracts clean source files directly into an adjacent directory:

```
project-dir/
├── MyPlugin.jar
└── MyPlugin-src/
    └── com/example/
        └── Main.java
```

## Building from Source

Compile the C# CLI wrapper using the built-in .NET Framework compiler:

```cmd
csc /t:exe /r:System.Windows.Forms.dll /out:vfd.exe Program.cs
copy vfd.exe jdecompile.exe
```

## Requirements

- Windows 10 / 11
- Java 17+ (or an embedded OpenJDK in `jre/`)

## License

This wrapper is open-source under the MIT License.  
Vineflower is distributed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).
