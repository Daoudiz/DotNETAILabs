using Microsoft.Extensions.DataIngestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DotNetIALabs.Infrastructure
{
    public sealed class PdfDocumentReader : IngestionDocumentReader
    {
        public override Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // PdfPig ne ferme pas le flux fourni par l'appelant. Si le flux
            // n'est pas seekable, la bibliothèque crée elle-même un buffer.
            using var pdf = PdfDocument.Open(source);

            var document = new IngestionDocument(identifier);

            for (int pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = pdf.GetPage(pageNumber);
                var text = ContentOrderTextExtractor.GetText(page);

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var section = new IngestionDocumentSection
                {
                    PageNumber = page.Number
                };

                section.Elements.Add(
                    new IngestionDocumentParagraph(text));

                document.Sections.Add(section);
            }

            if (document.Sections.Count == 0)
            {
                throw new InvalidDataException(
                    $"No text could be extracted from PDF '{identifier}'. " +
                    "The document may be scanned and require OCR.");
            }

            return Task.FromResult(document);
        }

    }
}
