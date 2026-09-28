namespace DefaultNamespace;

public enum CopyPart
{
    Labels,
    Checklists,
    Attachments
}

public readonly record struct CopyKeep(bool Labels, bool Checklists, bool Attachments)
{
    private static CopyKeep Everything { get; } = new(true, true, true);

    public static CopyKeep Of(List<CopyPart>? parts) =>
        parts is null
            ? Everything
            : new CopyKeep(
                parts.Contains(CopyPart.Labels),
                parts.Contains(CopyPart.Checklists),
                parts.Contains(CopyPart.Attachments));

    public IReadOnlyList<CopyPart> Parts => Enum.GetValues<CopyPart>().Where(Has).ToList();

    private bool Has(CopyPart part) =>
        part switch
        {
            CopyPart.Labels => Labels,
            CopyPart.Checklists => Checklists,
            _ => Attachments
        };
}
