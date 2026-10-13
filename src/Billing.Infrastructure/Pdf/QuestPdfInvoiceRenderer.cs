using Billing.Application;
using Billing.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Billing.Infrastructure.Pdf;

public class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    public byte[] Render(Invoice invoice, Customer customer)
    {
        string Fmt(decimal amount) => $"{amount:N2} {invoice.Currency}";

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("INVOICE").FontSize(22).Bold();
                    c.Item().Text(invoice.Number ?? "DRAFT").FontSize(12);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text($"Issued: {invoice.IssuedOn:yyyy-MM-dd}");
                    c.Item().Text($"Due: {invoice.DueOn:yyyy-MM-dd}");
                    c.Item().Text($"Status: {invoice.Status}").Bold();
                });
            });

            page.Content().PaddingVertical(20).Column(col =>
            {
                col.Item().Text("Bill to").Bold();
                col.Item().Text(customer.Name);
                col.Item().Text(customer.Email);
                if (invoice.PeriodStart is { } start)
                    col.Item().PaddingTop(8).Text($"Service period: {start:yyyy-MM-dd} to {invoice.PeriodEnd:yyyy-MM-dd}");

                col.Item().PaddingTop(20).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(6);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        foreach (var title in new[] { "Description", "Qty", "Unit price", "Amount" })
                            h.Cell().BorderBottom(1).PaddingBottom(4).Text(title).Bold();
                    });
                    foreach (var line in invoice.Lines)
                    {
                        table.Cell().PaddingVertical(4).Text(line.Description);
                        table.Cell().PaddingVertical(4).Text(line.Quantity.ToString());
                        table.Cell().PaddingVertical(4).AlignRight().Text(Fmt(line.UnitPrice));
                        table.Cell().PaddingVertical(4).AlignRight().Text(Fmt(line.Amount));
                    }
                });

                col.Item().PaddingTop(16).AlignRight().Column(t =>
                {
                    t.Item().Text($"Subtotal: {Fmt(invoice.Subtotal)}");
                    t.Item().Text($"VAT ({invoice.TaxRate:P0}): {Fmt(invoice.Tax)}");
                    t.Item().Text($"Total: {Fmt(invoice.Total)}").FontSize(13).Bold();
                    if (invoice.AmountPaid > 0) t.Item().Text($"Paid: {Fmt(invoice.AmountPaid)}");
                });
            });

            page.Footer().AlignCenter().Text($"Payment due within the terms stated. Invoice {invoice.Number}.").FontSize(8);
        })).GeneratePdf();
    }
}
