namespace WebsiteCollector.Features.SelectorManagement.Models;

public sealed record Selector(string FieldName, string CssSelector, string? AttributeName);
