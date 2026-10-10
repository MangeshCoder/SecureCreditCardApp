using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SecureEmiCard.Application.Abstractions.Billing;
using SecureEmiCard.Application.Features.Billing;

namespace SecureEmiCard.Infrastructure.Billing;

/// <summary>
/// Module 8: the monthly statement as a PDF, laid out like a bank statement: summary first (what to pay,
/// by when), then the movements, then the terms. QuestPDF is free under its Community licence for
/// individuals and companies below USD 1M revenue; a bank would buy the commercial licence.
/// </summary>
public class QuestPdfStatementRenderer : IStatementPdfRenderer
{
    private const string Blue = "#1E3C72";

    static QuestPdfStatementRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(StatementDetailDto d)
    {
        var s = d.Statement;
        string Date(DateTime utc) => (utc + d.UtcOffset).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(9.5f));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Secure Credit EMI").FontSize(18).Bold().FontColor(Blue);
                    c.Item().Text("Credit card statement").FontSize(11).FontColor(Colors.Grey.Darken1);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text(d.CardholderName).Bold();
                    c.Item().AlignRight().Text(d.MaskedCardNumber);
                    c.Item().AlignRight().Text($"Credit limit {Money(d.CreditLimit)}");
                });
            });

            page.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(12);

                // What to pay, by when - the first thing a customer looks for.
                col.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
                {
                    Box(row, "Statement date", Date(s.PeriodEnd));
                    Box(row, "Payment due date", Date(s.DueDate));
                    Box(row, "Total amount due", Money(s.ClosingBalance), big: true);
                    Box(row, "Minimum amount due", Money(s.MinimumDue), big: true);
                });
                col.Item().Text($"Statement period {Date(s.PeriodStart)} – {Date(s.PeriodEnd)} · Status: {StatusText(s)}")
                   .FontColor(Colors.Grey.Darken2);

                col.Item().Text("Account summary").Bold().FontSize(11).FontColor(Blue);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(120); });
                    void Line(string label, decimal amount, bool total = false)
                    {
                        var labelCell = t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3);
                        var amountCell = t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).AlignRight();
                        if (total) { labelCell.Text(label).Bold(); amountCell.Text(Money(amount)).Bold(); }
                        else { labelCell.Text(label); amountCell.Text(Money(amount)); }
                    }
                    Line("Opening balance (previous statement)", s.OpeningBalance);
                    Line("+ Purchases", s.Purchases);
                    Line("+ Cash withdrawals", s.CashWithdrawals);
                    Line("+ Fees, interest and GST", s.FeesAndCharges);
                    Line("− Payments", s.Payments);
                    Line("− Refunds", s.Refunds);
                    Line("− Cashback", s.Cashback);
                    Line("− Moved to EMI", s.MovedToEmi);
                    Line("= Total amount due", s.ClosingBalance, total: true);
                });

                if (s.EmiInstallmentsDue > 0)
                    col.Item().Background(Colors.Blue.Lighten5).Padding(8).Text(
                        $"EMI installments due by {Date(s.DueDate)}: {Money(s.EmiInstallmentsDue)}. They are paid separately " +
                        "(EMI page) and are not part of the total amount due.");

                col.Item().Text("Transactions").Bold().FontSize(11).FontColor(Blue);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(70); c.RelativeColumn(); c.ConstantColumn(60); c.ConstantColumn(95); });
                    t.Header(h =>
                    {
                        foreach (var title in new[] { "Date", "Description", "Type" })
                            h.Cell().Background(Blue).Padding(4).Text(title).FontColor(Colors.White).Bold();
                        h.Cell().Background(Blue).Padding(4).AlignRight().Text("Amount").FontColor(Colors.White).Bold();
                    });
                    foreach (var line in d.Lines)
                    {
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(Date(line.Date));
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(line.Description);
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(KindText(line.Kind));
                        var amount = t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignRight();
                        if (line.Amount < 0) amount.Text($"{Money(-line.Amount)} Cr").FontColor(Colors.Green.Darken2);
                        else amount.Text(Money(line.Amount));
                    }
                    if (d.Lines.Count == 0)
                        t.Cell().ColumnSpan(4).Padding(6).Text("No transactions in this period.").Italic();
                });

                col.Item().Text("Important information").Bold().FontSize(11).FontColor(Blue);
                var r = d.Rules;
                col.Item().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8.5f).FontColor(Colors.Grey.Darken3));
                    text.Line("• Pay the total amount due by the due date: no interest is charged on your purchases.");
                    text.Line($"• Pay at least the minimum amount due ({r.MinimumDuePercent:0.##}% of the balance + all fees, interest " +
                              $"and GST, at least {Money(r.MinimumDueFloor)}) to avoid a late payment fee.");
                    text.Line($"• Interest on the unpaid part: {r.MonthlyInterestPercent:0.##}% per month " +
                              $"({r.MonthlyInterestPercent * 12:0.##}% a year). No interest is charged on unpaid fees, interest or GST.");
                    text.Line($"• Cash withdrawals: fee {r.CashAdvanceFeePercent:0.##}% (at least {Money(r.CashAdvanceMinimumFee)}) " +
                              "and interest from the day of withdrawal.");
                    text.Line($"• GST of {r.GstPercent:0.##}% applies to all fees and interest.");
                    text.Line("• Late payment fee by balance unpaid on the due date: " + string.Join(", ", r.LateFees.Select(f =>
                        f.UpTo is { } upTo ? $"up to {Money(upTo)}: {Money(f.Fee)}" : $"above: {Money(f.Fee)}")) + ".");
                });
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text("Secure Credit EMI - a learning project, not a real bank.").FontSize(8).FontColor(Colors.Grey.Medium);
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        })).GeneratePdf();
    }

    private static void Box(RowDescriptor row, string label, string value, bool big = false) =>
        row.RelativeItem().Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            var text = c.Item().Text(value).Bold();
            if (big) text.FontSize(13).FontColor(Blue);
        });

    private static string Money(decimal amount) =>
        "₹" + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture) + (amount < 0 ? " Cr" : "");

    private static string KindText(string kind) => kind switch
    {
        "Tax" => "GST",
        "Emi" => "EMI",
        _ => kind
    };

    private static string StatusText(StatementDto s) => s.Status switch
    {
        "Paid" => "paid",
        "MinimumPaid" => "minimum paid",
        "Overdue" => "overdue",
        _ => "open"
    };
}
