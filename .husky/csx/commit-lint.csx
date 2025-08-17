/// <summary>
/// Commit Linter for Conventional Commits
/// This script validates commit messages against the Conventional Commits specification.
/// It checks for correct format, length, and provides suggestions for common mistakes.
/// </summary>
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

public class CommitLinter
{
    // Valid commit types based on conventional commits specification
    private static readonly string[] ValidTypes = {
        "build", "feat", "ci", "chore", "docs", "fix", 
        "perf", "refactor", "revert", "style", "test"
    };
    
    // Main pattern for conventional commits
    private static readonly string Pattern = 
        @"^(?<type>build|feat|ci|chore|docs|fix|perf|refactor|revert|style|test)" +
        @"(?:\((?<scope>.+)\))?" +
        @"(?<breaking>!)?" +
        @": " +
        @"(?<subject>.{1,})" +
        @"(?:\s+#(?<issue>\d+))?$";
    
    private static readonly Regex CommitRegex = new(Pattern, RegexOptions.Compiled);
    
    public static int Main(string[] args)
    {
        try
        {
            // Validate command line arguments
            if (args.Length == 0)
            {
                PrintError("Usage: CommitLinter <commit-message-file>");
                return 1;
            }

            if (!File.Exists(args[0]))
            {
                PrintError($"Commit message file not found: {args[0]}");
                return 1;
            }

            // Read commit message
            var lines = File.ReadAllLines(args[0]);
            if (lines.Length == 0)
            {
                PrintError("Commit message file is empty");
                return 1;
            }

            var commitMessage = lines[0].Trim();
            
            // Validate commit message
            var validationResult = ValidateCommitMessage(commitMessage);
            
            if (validationResult.IsValid)
            {
                PrintSuccess($"✓ Valid commit message: {commitMessage}");
                return 0;
            }
            
            // Print detailed error information
            PrintValidationErrors(commitMessage, validationResult);
            return 1;
        }
        catch (Exception ex)
        {
            PrintError($"Unexpected error: {ex.Message}");
            return 1;
        }
    }
    
    private static ValidationResult ValidateCommitMessage(string message)
    {
        var result = new ValidationResult();
        
        // Check if message is empty or whitespace
        if (string.IsNullOrWhiteSpace(message))
        {
            result.Errors.Add("Commit message cannot be empty");
            return result;
        }
        
        // Check length constraints
        if (message.Length > 100)
        {
            result.Errors.Add($"Commit message too long ({message.Length} chars). Maximum 100 characters recommended.");
        }
        
        if (message.Length < 10)
        {
            result.Errors.Add($"Commit message too short ({message.Length} chars). Minimum 10 characters recommended.");
        }
        
        // Check for conventional commit format
        var match = CommitRegex.Match(message);
        if (!match.Success)
        {
            result.Errors.Add("Message doesn't follow conventional commit format");
            
            // Provide specific guidance based on common issues
            AnalyzeCommonIssues(message, result);
            return result;
        }
        
        // Extract and validate components
        var type = match.Groups["type"].Value;
        var scope = match.Groups["scope"].Value;
        var subject = match.Groups["subject"].Value;
        var isBreaking = match.Groups["breaking"].Success;
        var issue = match.Groups["issue"].Value;
        
        // Validate subject
        ValidateSubject(subject, result);
        
        // Validate scope if present
        if (!string.IsNullOrEmpty(scope))
        {
            ValidateScope(scope, result);
        }
        
        // Store parsed components for potential future use
        result.Type = type;
        result.Scope = scope;
        result.Subject = subject;
        result.IsBreakingChange = isBreaking;
        result.IssueNumber = issue;
        
        result.IsValid = result.Errors.Count == 0;
        return result;
    }
    
    private static void ValidateSubject(string subject, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            result.Errors.Add("Subject cannot be empty");
            return;
        }
        
        // Subject should not start with uppercase (conventional commits preference)
        if (char.IsUpper(subject[0]))
        {
            result.Warnings.Add("Subject should start with lowercase letter");
        }
        
        // Subject should not end with period
        if (subject.EndsWith("."))
        {
            result.Errors.Add("Subject should not end with a period");
        }
        
        // Check for imperative mood (basic check)
        var discouragedStarts = new[] { "added", "fixed", "changed", "updated", "removed" };
        var subjectLower = subject.ToLower();
        
        foreach (var discouraged in discouragedStarts)
        {
            if (subjectLower.StartsWith(discouraged))
            {
                result.Warnings.Add($"Consider using imperative mood: '{discouraged}' → '{GetImperativeForm(discouraged)}'");
                break;
            }
        }
    }
    
    private static void ValidateScope(string scope, ValidationResult result)
    {
        if (scope.Length > 20)
        {
            result.Warnings.Add($"Scope is quite long ({scope.Length} chars). Consider using shorter, more focused scopes.");
        }
        
        if (scope.Contains(" "))
        {
            result.Warnings.Add("Scope should not contain spaces. Use kebab-case or camelCase.");
        }
    }
    
    private static void AnalyzeCommonIssues(string message, ValidationResult result)
    {
        // Check if it might be missing the colon and space
        if (ValidTypes.Any(type => message.StartsWith(type)) && !message.Contains(": "))
        {
            result.Suggestions.Add("Missing ': ' after type/scope. Format: 'type(scope): subject'");
        }
        
        // Check if type might be invalid
        var possibleType = message.Split(new[] { '(', ':', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
        if (!ValidTypes.Contains(possibleType.ToLower()))
        {
            var closest = FindClosestValidType(possibleType);
            if (!string.IsNullOrEmpty(closest))
            {
                result.Suggestions.Add($"Unknown type '{possibleType}'. Did you mean '{closest}'?");
            }
            else
            {
                result.Suggestions.Add($"Valid types: {string.Join(", ", ValidTypes)}");
            }
        }
    }
    
    private static string FindClosestValidType(string input)
    {
        input = input.ToLower();
        return ValidTypes.FirstOrDefault(type => 
            type.StartsWith(input) || 
            input.StartsWith(type) || 
            LevenshteinDistance(input, type) <= 2);
    }
    
    private static int LevenshteinDistance(string s1, string s2)
    {
        var matrix = new int[s1.Length + 1, s2.Length + 1];
        
        for (int i = 0; i <= s1.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) matrix[0, j] = j;
        
        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(Math.Min(
                    matrix[i - 1, j] + 1,
                    matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }
        
        return matrix[s1.Length, s2.Length];
    }
    
    private static string GetImperativeForm(string pastTense)
    {
        return pastTense switch
        {
            "added" => "add",
            "fixed" => "fix",
            "changed" => "change",
            "updated" => "update",
            "removed" => "remove",
            _ => pastTense
        };
    }
    
    private static void PrintValidationErrors(string message, ValidationResult result)
    {
        PrintError("Invalid commit message format");
        Console.WriteLine();
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Message: {message}");
        Console.ResetColor();
        Console.WriteLine();
        
        if (result.Errors.Any())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("✗ Errors:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  • {error}");
            }
            Console.ResetColor();
            Console.WriteLine();
        }
        
        if (result.Warnings.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠ Warnings:");
            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"  • {warning}");
            }
            Console.ResetColor();
            Console.WriteLine();
        }
        
        if (result.Suggestions.Any())
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("💡 Suggestions:");
            foreach (var suggestion in result.Suggestions)
            {
                Console.WriteLine($"  • {suggestion}");
            }
            Console.ResetColor();
            Console.WriteLine();
        }
        
        PrintUsageExamples();
    }
    
    private static void PrintUsageExamples()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Valid examples:");
        Console.ResetColor();
        
        var examples = new[]
        {
            "feat: add user authentication",
            "fix(auth): resolve login validation bug",
            "docs: update API documentation",
            "feat!: remove deprecated endpoints",
            "chore(deps): update lodash to v4.17.21"
        };
        
        foreach (var example in examples)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"  {example}");
        }
        Console.ResetColor();
        Console.WriteLine();
        
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Format: <type>[optional scope][!]: <description>");
        Console.WriteLine("More info: https://www.conventionalcommits.org/en/v1.0.0/");
        Console.ResetColor();
    }
    
    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ {message}");
        Console.ResetColor();
    }
    
    private static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Suggestions { get; } = new();
    
    // Parsed commit components
    public string Type { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public bool IsBreakingChange { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
}
