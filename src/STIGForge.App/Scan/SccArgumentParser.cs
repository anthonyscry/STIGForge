using System.Text;

namespace STIGForge.App;

internal static class SccArgumentParser
{
    internal readonly record struct SccArgumentAnalysis(
        bool HasAnyToken,
        bool HasOutputSwitch,
        bool HasOutputValue,
        bool MissingOutputValue,
        bool HasNonOutputDirective);

    internal static string QuoteCommandLineArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    internal static string AppendCommandLineArgumentWithValue(string existingArguments, string switchName, string value)
    {
        var prefix = string.IsNullOrWhiteSpace(existingArguments)
            ? string.Empty
            : existingArguments.Trim() + " ";

        return prefix + switchName + " " + QuoteCommandLineArgument(value);
    }

    internal static bool LooksLikeSwitchToken(string token, IEnumerable<string> sccOutputSwitches)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (token[0] == '-')
            return true;

        if (token[0] != '/')
            return false;

        return sccOutputSwitches
            .Where(candidate => candidate.StartsWith("/", StringComparison.Ordinal))
            .Any(candidate => string.Equals(candidate, token, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryGetSwitchAndInlineValue(string token, IEnumerable<string> sccOutputSwitches, out string switchToken, out string inlineValue)
    {
        switchToken = string.Empty;
        inlineValue = string.Empty;

        if (!LooksLikeSwitchToken(token, sccOutputSwitches))
            return false;

        var equalsIndex = token.IndexOf('=');
        if (equalsIndex > 0)
        {
            switchToken = token[..equalsIndex];
            inlineValue = token[(equalsIndex + 1)..];
            return true;
        }

        switchToken = token;
        return true;
    }

    internal static bool IsSccOutputSwitch(string switchToken, IEnumerable<string> sccOutputSwitches)
    {
        if (string.IsNullOrWhiteSpace(switchToken))
            return false;

        return sccOutputSwitches.Any(candidate => string.Equals(candidate, switchToken, StringComparison.OrdinalIgnoreCase));
    }

    internal static List<string> TokenizeCommandLineArguments(string arguments)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
            return tokens;

        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    internal static SccArgumentAnalysis AnalyzeSccArguments(string arguments, IEnumerable<string> sccOutputSwitches)
    {
        var tokens = TokenizeCommandLineArguments(arguments);
        if (tokens.Count == 0)
            return new SccArgumentAnalysis(false, false, false, false, false);

        var consumedAsOutputValue = new HashSet<int>();
        var hasOutputSwitch = false;
        var hasOutputValue = false;
        var missingOutputValue = false;
        var hasNonOutputDirective = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (consumedAsOutputValue.Contains(i))
                continue;

            var token = tokens[i];
            if (TryGetSwitchAndInlineValue(token, sccOutputSwitches, out var switchToken, out var inlineValue))
            {
                if (IsSccOutputSwitch(switchToken, sccOutputSwitches))
                {
                    hasOutputSwitch = true;

                    if (!string.IsNullOrWhiteSpace(inlineValue))
                    {
                        hasOutputValue = true;
                        continue;
                    }

                    if (i + 1 < tokens.Count && !LooksLikeSwitchToken(tokens[i + 1], sccOutputSwitches))
                    {
                        hasOutputValue = true;
                        consumedAsOutputValue.Add(i + 1);
                    }
                    else
                    {
                        missingOutputValue = true;
                    }

                    continue;
                }

                hasNonOutputDirective = true;
                continue;
            }

            hasNonOutputDirective = true;
        }

        return new SccArgumentAnalysis(
            HasAnyToken: true,
            HasOutputSwitch: hasOutputSwitch,
            HasOutputValue: hasOutputValue,
            MissingOutputValue: missingOutputValue,
            HasNonOutputDirective: hasNonOutputDirective);
    }

    internal static bool TryBuildSccHeadlessArguments(string rawArguments, string outputRoot, IEnumerable<string> sccOutputSwitches, out string effectiveArguments, out string validationError)
    {
        effectiveArguments = (rawArguments ?? string.Empty).Trim();
        validationError = string.Empty;

        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            validationError = "Output folder is required for SCC validation.";
            return false;
        }

        var initial = AnalyzeSccArguments(effectiveArguments, sccOutputSwitches);
        if (!initial.HasOutputSwitch)
            effectiveArguments = AppendCommandLineArgumentWithValue(effectiveArguments, "-u", outputRoot);

        var normalized = AnalyzeSccArguments(effectiveArguments, sccOutputSwitches);

        if (normalized.MissingOutputValue || (normalized.HasOutputSwitch && !normalized.HasOutputValue))
        {
            validationError = "SCC arguments include an output switch without a valid path value.";
            return false;
        }

        if (!normalized.HasAnyToken)
        {
            validationError = "SCC arguments are empty.";
            return false;
        }

        return true;
    }
}
