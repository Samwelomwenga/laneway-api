using Xunit;

namespace Laneway.Api.Tests;

public class MessageRouteTests
{
    public static TheoryData<MessageType> Types => [.. Enum.GetValues<MessageType>()];

    [Theory]
    [MemberData(nameof(Types))]
    public void Every_type_has_a_route(MessageType type) => Assert.True(MessageRoutes.All.ContainsKey(type));

    [Theory]
    [MemberData(nameof(Types))]
    public void Every_listed_channel_has_a_template(MessageType type)
    {
        foreach (var channel in MessageRoutes.For(type).Channels)
        {
            Assert.True(channel switch
            {
                MessageChannel.Email => EmailTemplates.Has(type),
                _ => false
            }, $"{type} lists {channel} but has no {channel} template.");
        }
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void An_email_by_nature_type_lists_only_email(MessageType type)
    {
        var route = MessageRoutes.For(type);
        if (route.EmailByNature)
        {
            Assert.Equal([MessageChannel.Email], route.Channels);
        }
    }
}
