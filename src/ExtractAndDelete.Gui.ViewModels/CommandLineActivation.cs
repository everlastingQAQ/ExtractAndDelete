namespace ExtractAndDelete.Gui.ViewModels;

public static class CommandLineActivation
{
    public static string? TryGetArchivePath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], "--archive", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = arguments[index + 1];
                return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
            }
        }

        return null;
    }

    public static string? TryGetArchivePath(string? argumentString)
    {
        string[] arguments = SplitArguments(argumentString);
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], "--archive", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = arguments[index + 1];
                return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
            }
        }

        return null;
    }

    private static string[] SplitArguments(string? argumentString)
    {
        if (string.IsNullOrWhiteSpace(argumentString))
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char character in argumentString)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments.ToArray();
    }
}
