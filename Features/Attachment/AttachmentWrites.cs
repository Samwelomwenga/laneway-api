namespace DefaultNamespace;

public sealed record CreateLinkAttachmentWrite(string Url, string? Name)
{
    public static CreateLinkAttachmentWrite Of(CreateLinkAttachmentDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new CreateLinkAttachmentWrite(Writes.Required(dto.Url, "url"), dto.Name);
    }
}

public sealed record UpdateAttachmentWrite(string Name)
{
    public static UpdateAttachmentWrite Of(UpdateAttachmentDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new UpdateAttachmentWrite(Writes.Required(dto.Name, "name"));
    }
}
