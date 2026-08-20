namespace ExtractAndDelete.Core;

public class ExtractionResult
{
    public bool Success
    {
        get;
        set;
    }

    public string? ErrorMessage
    {
        get;
        set;
    }
}

public class CleanupResult
{
    public bool Success
    {
        get;
        set;
    }

    public string? ErrorMessage
    {
        get;
        set;
    }
}

public class ExtractAndDeleteResult
{
    public bool Success 
    { 
        get; 
        set; 
    }
    public string? ErrorMessage
    { 
        get; 
        set; 
    }
}