using DocFlow.Domain;

namespace DocFlow.UnitTests.Domain;

public class InvoiceValidatorTests
{
    [Fact]
    public void Valid_invoice_has_no_issues() =>
        Assert.Empty(InvoiceValidator.Validate(TestData.ValidInvoice(), TestData.Today));

    [Fact]
    public void Detects_total_that_differs_from_items_sum()
    {
        var issues = InvoiceValidator.Validate(TestData.ValidInvoice() with { Total = 2301m }, TestData.Today);

        Assert.Contains(issues, i => i.Code == "total_mismatch");
    }

    [Fact]
    public void Detects_item_whose_quantity_times_price_is_wrong()
    {
        var invoice = TestData.ValidInvoice() with
        {
            Items = [new InvoiceItem("Consultoria", 10, 150m, 1600m)],
            Total = 1600m,
        };

        var issues = InvoiceValidator.Validate(invoice, TestData.Today);

        Assert.Equal(["item_total_mismatch"], issues.Select(i => i.Code));
    }

    [Fact]
    public void Detects_invalid_issuer_cnpj()
    {
        var issues = InvoiceValidator.Validate(
            TestData.ValidInvoice() with { IssuerCnpj = "11.222.333/0001-82" },
            TestData.Today);

        Assert.Contains(issues, i => i.Code == "issuer_cnpj_invalid");
    }

    [Fact]
    public void Detects_future_issue_date_and_due_date_before_issue()
    {
        var invoice = TestData.ValidInvoice() with
        {
            IssueDate = TestData.Today.AddDays(5),
            DueDate = TestData.Today,
        };

        var codes = InvoiceValidator.Validate(invoice, TestData.Today).Select(i => i.Code).ToList();

        Assert.Contains("issue_date_future", codes);
        Assert.Contains("due_date_before_issue", codes);
    }

    [Fact]
    public void Reports_every_missing_field_of_an_empty_extraction()
    {
        var codes = InvoiceValidator.Validate(InvoiceData.Empty, TestData.Today).Select(i => i.Code).ToHashSet();

        Assert.Superset(
            new HashSet<string> { "issuer_name_missing", "number_missing", "issuer_cnpj_missing", "issue_date_missing", "total_missing" },
            codes);
    }
}
