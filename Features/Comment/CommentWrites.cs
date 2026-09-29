namespace DefaultNamespace;

public sealed record CommentTextWrite(string Text)
{
    public static CommentTextWrite Of(CommentTextDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CommentTextWrite(Writes.Required(dto.Text, "text"));
    }
}
