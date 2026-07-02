namespace MacExplorer.Models;

/// <summary>Rule type for batch rename operations.</summary>
public enum BatchRenameRuleType
{
    FindReplace,
    AddPrefix,
    AddSuffix,
    Sequence,
    Date,
    CaseConversion
}

/// <summary>Case conversion mode.</summary>
public enum CaseConversionMode
{
    Uppercase,
    Lowercase,
    TitleCase
}

/// <summary>A single rename rule to apply.</summary>
public class BatchRenameRule
{
    public BatchRenameRuleType Type { get; set; }

    // FindReplace
    public string FindText { get; set; } = "";
    public string ReplaceText { get; set; } = "";

    // AddPrefix / AddSuffix
    public string PrefixText { get; set; } = "";
    public string SuffixText { get; set; } = "";

    // Sequence
    public int SequenceStart { get; set; } = 1;
    public int SequenceStep { get; set; } = 1;
    public int SequencePadding { get; set; } = 2;

    // Date
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    // CaseConversion
    public CaseConversionMode CaseMode { get; set; } = CaseConversionMode.Uppercase;

    // Whether to apply this rule to the file extension
    public bool ApplyToExtension { get; set; } = false;

    // Whether this rule is enabled
    public bool IsEnabled { get; set; } = true;
}
