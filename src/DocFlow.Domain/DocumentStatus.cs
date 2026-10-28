namespace DocFlow.Domain;

public enum DocumentStatus
{
    Received,
    Extracting,
    Extracted,
    Validating,
    NeedsReview,
    Approved,
    Rejected,
}
