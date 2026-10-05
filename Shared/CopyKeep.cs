namespace Laneway.Api;

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

public enum BoardCopyPart
{
    Labels,
    Checklists,
    Attachments,
    Cards
}

public readonly record struct BoardCopyKeep(bool Labels, bool Checklists, bool Attachments, bool Cards)
{
    private static BoardCopyKeep Everything { get; } = new(true, true, true, true);

    public static BoardCopyKeep Of(List<BoardCopyPart>? parts) =>
        parts is null
            ? Everything
            : new BoardCopyKeep(
                parts.Contains(BoardCopyPart.Labels),
                parts.Contains(BoardCopyPart.Checklists),
                parts.Contains(BoardCopyPart.Attachments),
                parts.Contains(BoardCopyPart.Cards));

    public static bool NeedsCards(BoardCopyPart part) =>
        part is BoardCopyPart.Checklists or BoardCopyPart.Attachments;

    public CopyKeep OnCards => new(Labels, Checklists, Attachments);

    public IReadOnlyList<BoardCopyPart> Parts => Enum.GetValues<BoardCopyPart>().Where(Has).ToList();

    private bool Has(BoardCopyPart part) =>
        part switch
        {
            BoardCopyPart.Labels => Labels,
            BoardCopyPart.Checklists => Checklists,
            BoardCopyPart.Attachments => Attachments,
            _ => Cards
        };
}
