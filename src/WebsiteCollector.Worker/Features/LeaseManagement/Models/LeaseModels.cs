using WebsiteCollector.Features.WebsiteCollection.Models;

namespace WebsiteCollector.Features.LeaseManagement.Models;

public sealed record LeaseRenewalRequest(ClaimedWebsiteSelector Job, int LeaseSeconds);
public sealed record LeaseStatus(bool IsValid, string? Reason);
