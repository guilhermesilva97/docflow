using DocFlow.Domain;

namespace DocFlow.UnitTests.Domain;

public class DocumentTests
{
    private static Document Validating(double confidence = 0.95)
    {
        var document = Document.Receive(Guid.NewGuid(), "nf.pdf", "blob", TestData.Now);
        document.StartExtraction(TestData.Now);
        document.RecordExtraction(new ExtractionResult(TestData.ValidInvoice(), confidence, "llm"), TestData.Now);
        document.StartValidation(TestData.Now);
        return document;
    }

    [Fact]
    public void Happy_path_ends_approved_with_full_history()
    {
        var document = Validating();

        document.CompleteValidation([], 0.8, isDuplicate: false, TestData.Now);

        Assert.Equal(DocumentStatus.Approved, document.Status);
        Assert.Equal(
            [DocumentStatus.Received, DocumentStatus.Extracting, DocumentStatus.Extracted, DocumentStatus.Validating, DocumentStatus.Approved],
            document.History.Select(h => h.Status));
        Assert.Equal("11222333000181|123", document.DuplicateKey);
    }

    [Fact]
    public void Validation_issues_send_document_to_review()
    {
        var document = Validating();

        document.CompleteValidation([new ValidationIssue("total_mismatch", "x")], 0.8, isDuplicate: false, TestData.Now);

        Assert.Equal(DocumentStatus.NeedsReview, document.Status);
        Assert.Single(document.Issues);
    }

    [Fact]
    public void Low_confidence_sends_valid_document_to_review()
    {
        var document = Validating(confidence: 0.5);

        document.CompleteValidation([], 0.8, isDuplicate: false, TestData.Now);

        Assert.Equal(DocumentStatus.NeedsReview, document.Status);
        Assert.Contains("Confiança", document.LastChange.Detail);
    }

    [Fact]
    public void Duplicate_invoice_is_rejected()
    {
        var document = Validating();

        document.CompleteValidation([], 0.8, isDuplicate: true, TestData.Now);

        Assert.Equal(DocumentStatus.Rejected, document.Status);
    }

    [Fact]
    public void Failed_extraction_goes_to_review_with_reason()
    {
        var document = Document.Receive(Guid.NewGuid(), "nf.pdf", "blob", TestData.Now);
        document.StartExtraction(TestData.Now);

        document.FailExtraction("PDF sem texto", TestData.Now);

        Assert.Equal(DocumentStatus.NeedsReview, document.Status);
        Assert.Equal("extraction_failed", Assert.Single(document.Issues).Code);
    }

    [Fact]
    public void Invalid_transitions_are_refused()
    {
        var document = Document.Receive(Guid.NewGuid(), "nf.pdf", "blob", TestData.Now);

        Assert.Throws<InvalidTransitionException>(() => document.StartValidation(TestData.Now));
        Assert.Throws<InvalidTransitionException>(() => document.Reject("x", "ana", TestData.Now));
    }

    [Fact]
    public void Reviewer_can_fix_fields_and_approve()
    {
        var document = Validating();
        document.CompleteValidation([new ValidationIssue("total_mismatch", "x")], 0.8, false, TestData.Now);

        document.Approve(TestData.ValidInvoice(), "ana", TestData.Today, isDuplicate: false, TestData.Now);

        Assert.Equal(DocumentStatus.Approved, document.Status);
        Assert.Equal("ana", document.ReviewedBy);
        Assert.Empty(document.Issues);
    }

    [Fact]
    public void Reviewer_cannot_approve_invoice_that_still_breaks_rules()
    {
        var document = Validating();
        document.CompleteValidation([new ValidationIssue("total_mismatch", "x")], 0.8, false, TestData.Now);

        var error = Assert.Throws<InvoiceRejectedException>(() =>
            document.Approve(TestData.ValidInvoice() with { Total = 1m }, "ana", TestData.Today, false, TestData.Now));

        Assert.Contains(error.Issues, i => i.Code == "total_mismatch");
        Assert.Equal(DocumentStatus.NeedsReview, document.Status);
    }

    [Fact]
    public void Reviewer_cannot_approve_duplicate()
    {
        var document = Validating();
        document.CompleteValidation([new ValidationIssue("total_mismatch", "x")], 0.8, false, TestData.Now);

        Assert.Throws<DuplicateInvoiceException>(() =>
            document.Approve(TestData.ValidInvoice(), "ana", TestData.Today, isDuplicate: true, TestData.Now));
    }
}
