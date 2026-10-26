using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

public class LoggingInventoryTests
{
    [Fact]
    public void A_listed_event_with_its_listed_fields_passes()
    {
        var log = new ShippedLog(AuthLog.Category, LogLevel.Warning, "SignInFailed", ["sub", "blocked", "ActionName"]);

        Assert.Empty(LoggingInventory.Documented.Violations([log], []));
    }

    [Fact]
    public void A_field_the_inventory_doesnt_list_fails_naming_the_field()
    {
        var log = new ShippedLog(AuthLog.Category, LogLevel.Warning, "SignInFailed", ["sub", "email"]);

        var violation = Assert.Single(LoggingInventory.Documented.Violations([log], []));
        Assert.Contains("`email`", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void An_event_the_inventory_doesnt_list_fails()
    {
        var log = new ShippedLog(AuthLog.Category, LogLevel.Information, "SignedOut", ["sub"]);

        var violation = Assert.Single(LoggingInventory.Documented.Violations([log], []));
        Assert.Contains("SignedOut", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_from_another_section_fails()
    {
        var log = new ShippedLog(DenialDrainer.Category, LogLevel.Warning, "AccessDenied", ["sub", "deliveryId"]);

        var violation = Assert.Single(LoggingInventory.Documented.Violations([log], []));
        Assert.Contains("`deliveryId`", violation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LogLevel.Warning, 0)]
    [InlineData(LogLevel.Information, 1)]
    public void A_framework_category_is_exempt_at_warning_and_above(LogLevel level, int violations)
    {
        var log = new ShippedLog(
            "Microsoft.EntityFrameworkCore.Database.Command", level, "CommandError", ["commandText"]);

        Assert.Equal(violations, LoggingInventory.Documented.Violations([log], []).Count);
    }

    [Fact]
    public void A_laneway_counter_with_a_tag_the_inventory_doesnt_list_fails()
    {
        var counter = new ShippedCounter(AuthMeter.Name, AuthMeter.SignInAttemptsName, ["result", "sub"]);

        var violation = Assert.Single(LoggingInventory.Documented.Violations([], [counter]));
        Assert.Contains("`sub`", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_framework_meter_is_exempt()
    {
        var counter = new ShippedCounter("System.Net.Http", "http.client.request.duration", ["server.address"]);

        Assert.Empty(LoggingInventory.Documented.Violations([], [counter]));
    }

    [Fact]
    public void A_reshaped_table_fails_naming_its_section()
    {
        var reshaped = LoggingInventory.ReadMarkdown().Replace(
            "| Id | Event | Level | Fields |", "| Id | Event | Level | Attributes |", StringComparison.Ordinal);

        var thrown = Assert.Throws<InvalidDataException>(() => LoggingInventory.Parse(reshaped));

        Assert.Contains("Events", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_event_table_with_no_category_line_fails_naming_its_section()
    {
        var reshaped = LoggingInventory.ReadMarkdown().Replace(
            "Denials log under the `Laneway.Authorization` category", "Denials log here", StringComparison.Ordinal);

        var thrown = Assert.Throws<InvalidDataException>(() => LoggingInventory.Parse(reshaped));

        Assert.Contains("Authorization denials", thrown.Message, StringComparison.Ordinal);
    }
}
