namespace STIGForge.Content.Models;

/// <summary>
/// Exception thrown during content parsing with file context information.
/// Provides structured error reporting with file path and line number.
/// </summary>
public class ParsingException : Exception
{
    /// <summary>
    /// Path to the file being parsed when the error occurred
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Line number where the error occurred (if available)
    /// </summary>
    public int? LineNumber { get; set; }

    public ParsingException()
    {
    }

    public ParsingException(string message) : base(message)
    {
    }

    public ParsingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ParsingException(string message, string filePath, int? lineNumber = null) : base(message)
    {
        FilePath = filePath;
        LineNumber = lineNumber;
    }

    public ParsingException(string message, string filePath, int? lineNumber, Exception innerException) 
        : base(message, innerException)
    {
        FilePath = filePath;
        LineNumber = lineNumber;
    }

    public override string ToString()
    {
        var location = FilePath != null 
            ? $" at {FilePath}" + (LineNumber.HasValue ? $":line {LineNumber}" : "")
            : "";
        return $"{base.ToString()}{location}";
    }
}
